using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Evm;
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
                p.BAC, p.DataDate, p.EacVariantDefault, p.EacCustomPerformanceFactor, p.EacManualEtc))
            .SingleOrDefaultAsync(cancellationToken);

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
