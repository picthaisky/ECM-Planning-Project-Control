using CMPlus.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S14-BE-02: <see cref="IBaselineComparisonReader"/> against <see cref="CmPlusDbContext"/>.
/// <see cref="GetActivityComparisonAsync"/> is the one query whose shape matters for the
/// 10,000-activity performance target - a single left-join projection, never a per-activity round
/// trip, backed by <c>BaselineActivitySnapshotConfiguration</c>'s `(TenantId, BaselineId,
/// ActivityId)` index for the snapshot side and the `Activities` table's own clustered `Id` PK for
/// the joined side.</summary>
public sealed class BaselineComparisonReader(CmPlusDbContext dbContext) : IBaselineComparisonReader
{
    public Task<decimal?> GetCurrentBacAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => (decimal?)p.BAC)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<BaselineSummary?> FindActiveBaselineAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Baselines
            .AsNoTracking()
            .Where(b => b.ProjectId == projectId && b.IsActive)
            .Select(b => new BaselineSummary(b.Id, b.Name, b.CapturedAt, b.Bac))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<BaselineSummary?> FindBaselineAsync(Guid projectId, Guid baselineId, CancellationToken cancellationToken = default) =>
        dbContext.Baselines
            .AsNoTracking()
            .Where(b => b.ProjectId == projectId && b.Id == baselineId)
            .Select(b => new BaselineSummary(b.Id, b.Name, b.CapturedAt, b.Bac))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<BaselineActivityComparisonRow>> GetActivityComparisonAsync(
        Guid projectId, Guid baselineId, CancellationToken cancellationToken = default)
    {
        // Left join, snapshot-anchored (never inner join - a snapshot row with no current match must
        // still appear, see IBaselineComparisonReader's remarks). `projectId` is accepted for the
        // established explicit-scoping convention but not re-asserted in the predicate here - see
        // this interface's own remarks on why the BaselineId filter alone is already correct.
        var query =
            from s in dbContext.BaselineActivitySnapshots.AsNoTracking()
            where s.BaselineId == baselineId
            join a in dbContext.Activities.AsNoTracking() on s.ActivityId equals a.Id into activityJoin
            from a in activityJoin.DefaultIfEmpty()
            select new BaselineActivityComparisonRow(
                s.ActivityId,
                s.PlannedStart,
                s.PlannedFinish,
                s.DurationDays,
                s.BudgetCost,
                a != null ? a.ActivityCode : null,
                a != null ? a.Name : null,
                a != null ? (DateTimeOffset?)a.PlannedStart : null,
                a != null ? (DateTimeOffset?)a.PlannedFinish : null,
                a != null ? (int?)a.DurationDays : null,
                a != null ? (decimal?)a.BudgetCost : null,
                a != null ? (bool?)a.IsCritical : null);

        return await query.ToListAsync(cancellationToken);
    }
}
