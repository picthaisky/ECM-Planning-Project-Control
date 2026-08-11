using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Evm;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// S7-BE-01/03: <see cref="IEvmDataReader"/> against <see cref="CmPlusDbContext"/>.
/// <see cref="GetActivityInputsAsync"/> issues exactly one query (a per-activity correlated
/// subquery for the ADR-0009 step-function lookup, the same "latest `PeriodEndDate` &lt;= asOf, ties
/// broken by `RecordedAt`" shape <see cref="ActivityProgressReader.GetProgressAsOfAsync"/> already
/// uses per-activity) rather than one round trip per activity - never an N+1 loop over a project's
/// activities. Backed by the same `(TenantId, ActivityId, PeriodEndDate DESC)` index Sprint 1 already
/// created for that reader (S1-DB-02); database-engineer's S7-DB-02 owns confirming this plan is
/// still an index seek, not a scan, at the 10,000-activity/26-period scale against real SQL Server.
/// </summary>
public sealed class EvmDataReader(CmPlusDbContext dbContext) : IEvmDataReader
{
    public Task<ProjectEvmSettings?> GetProjectSettingsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new ProjectEvmSettings(
                p.DataDate, p.EacVariantDefault, p.EacCustomPerformanceFactor, p.EacManualEtc, p.EacManualEtcStaleSince))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// domain-rules.md §5.5(c). Sprint-10 security review H-02: <c>OriginalBac</c> is now NOT NULL
    /// (backfilled by migration), so this projects it directly with no client-side <c>??</c> fallback
    /// - the fallback used to read as the LIVE <c>BAC</c> for a "legacy" row, which double-counted
    /// every already-approved VO (<c>BAC</c> already includes them) and inflated every historical EVM
    /// read upward. Still a narrow, single-column projection (not
    /// <c>Project.EffectiveOriginalBac</c> directly) purely for query-shape consistency with the rest
    /// of this reader - that computed property is <c>Ignore()</c>'d from the EF model
    /// (ProjectConfiguration.cs) precisely so it is never relied on to translate inside a
    /// LINQ-to-Entities query.
    /// </summary>
    public async Task<decimal> GetBacAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var originalBac = await dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => (decimal?)p.OriginalBac)
            .SingleOrDefaultAsync(cancellationToken);

        if (originalBac is null)
        {
            // Caller (EvmComputationService) already confirmed the project exists via
            // GetProjectSettingsAsync before ever calling this - mirrors GetActivityInputsAsync's own
            // "benign default, never a second not-found signal" contract.
            return 0m;
        }

        // Sprint 10's own filtered covering index (VariationOrderConfiguration.cs:
        // IX_VariationOrders_TenantId_ProjectId_Approved, INCLUDE (Amount), WHERE Status = 3) does not
        // include ApprovedAt, so this seeks that index and then filters ApprovedAt <= asOf rather than
        // being fully covered by it - acceptable here (an EVM read, not the WBS-tree <100ms path) and
        // narrower than a full scan regardless (still Status = Approved AND ProjectId = this project).
        var approvedVoTotal = await dbContext.VariationOrders
            .AsNoTracking()
            .Where(v => v.ProjectId == projectId && v.Status == VariationOrderStatus.Approved && v.ApprovedAt <= asOf)
            .SumAsync(v => (decimal?)v.Amount, cancellationToken) ?? 0m;

        return originalBac.Value + approvedVoTotal;
    }

    public async Task<IReadOnlyList<EvmActivityProgressInput>> GetActivityInputsAsync(
        Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var query =
            from a in dbContext.Activities.AsNoTracking()
            where dbContext.WBSNodes.Any(w => w.Id == a.WbsNodeId && w.ProjectId == projectId)
            select new EvmActivityProgressInput(
                a.Id,
                a.BudgetCost,
                a.PlannedStart,
                a.PlannedFinish,
                dbContext.ActivityProgressLogs
                    .Where(l => l.ActivityId == a.Id && l.PeriodEndDate <= asOf)
                    .OrderByDescending(l => l.PeriodEndDate)
                    .ThenByDescending(l => l.RecordedAt)
                    .Select(l => (decimal?)l.ProgressPercentage)
                    .FirstOrDefault() ?? 0m);

        return await query.ToListAsync(cancellationToken);
    }
}
