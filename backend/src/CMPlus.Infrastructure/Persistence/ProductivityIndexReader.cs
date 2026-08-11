using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Manpower;
using CMPlus.Application.Wbs;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// S12-BE-02: <see cref="IProductivityIndexReader"/> against <see cref="CmPlusDbContext"/> -
/// domain-rules.md (manpower-equipment) §4.3 (Tier 1 subtree matching), §4.5 (half-open bucketing,
/// shared with $AC$), §4.7 (the effective log set), §5.5 (the reporting-cadence gate). This is the
/// one place all of that DB-shaped work happens; <see cref="ProductivityIndexCalculator"/> itself
/// never touches EF Core.
///
/// <para><b>Row-to-scope matching, uniformly (§4.3).</b> Both "does this log row belong to the
/// queried scope at all" and "is this log row's own scope(l) matched to a budgeted activity" reduce
/// to the same primitive: <c>scope(l)</c>, the set of activities a row's own <c>ActivityId</c>/
/// <c>WbsNodeId</c> resolves to (<see cref="ResolveRowScope"/>). A row belongs to the query iff
/// <c>scope(l)</c> overlaps the query's own target activity set (so a row logged at a broader/
/// ancestor node legitimately counts toward a narrower query underneath it, and a project-level/
/// unattributed row - <c>scope(l) = every activity</c> - belongs to every query); it is "matched"
/// (contributes to <see cref="ProductivityIndexAggregate.ActualManHoursInScope"/> rather than being
/// excluded) iff <c>scope(l)</c> contains at least one activity with a non-null <c>BudgetManHours</c>,
/// evaluated against the row's own scope regardless of the query's narrower target - §4.3's exclusion
/// rule is a property of the row, not of whichever query happens to be reading it.</para>
///
/// <para><b>Bounded, load-once-compute-in-memory</b>, mirroring this codebase's established readers
/// (<c>WbsTreeReader</c>'s node set, <c>EotEffectiveLogSet</c>'s correction-chain resolution): the WBS
/// node hierarchy and the project's activities are both bounded per-project datasets (~5,000 nodes/
/// 10,000 activities, the same budget <c>IWbsTreeReader</c> is built against), so this reader loads
/// them once per call and resolves subtree closures/row-matching in memory rather than issuing a
/// query per row or a recursive CTE.</para>
/// </summary>
public sealed class ProductivityIndexReader(CmPlusDbContext dbContext) : IProductivityIndexReader
{
    private readonly record struct ActivityInfo(Guid Id, Guid WbsNodeId, decimal? BudgetManHours);

    private readonly record struct LogRowInfo(
        Guid Id, Guid? WbsNodeId, Guid? ActivityId, decimal ManHours, int WorkerCount, ManpowerLogEntryKind EntryKind);

    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, cancellationToken);

    public Task<bool> WbsNodeExistsInProjectAsync(Guid projectId, Guid wbsNodeId, CancellationToken cancellationToken = default) =>
        dbContext.WBSNodes.AsNoTracking().AnyAsync(n => n.Id == wbsNodeId && n.ProjectId == projectId, cancellationToken);

    public Task<bool> ActivityExistsInProjectAsync(Guid projectId, Guid activityId, CancellationToken cancellationToken = default) =>
        dbContext.Activities.AsNoTracking()
            .AnyAsync(a => a.Id == activityId && dbContext.WBSNodes.Any(n => n.Id == a.WbsNodeId && n.ProjectId == projectId), cancellationToken);

    public async Task<ProductivityIndexAggregate> GetAggregateAsync(
        Guid projectId,
        Guid? wbsNodeId,
        Guid? activityId,
        DateTimeOffset periodStartExclusive,
        DateTimeOffset periodEndInclusive,
        CancellationToken cancellationToken = default)
    {
        var nodeLinks = await dbContext.WBSNodes.AsNoTracking()
            .Where(n => n.ProjectId == projectId)
            .Select(n => new WbsNodeParentLink(n.Id, n.ParentWbsNodeId))
            .ToListAsync(cancellationToken);

        var allActivities = await dbContext.Activities.AsNoTracking()
            .Where(a => dbContext.WBSNodes.Any(n => n.Id == a.WbsNodeId && n.ProjectId == projectId))
            .Select(a => new ActivityInfo(a.Id, a.WbsNodeId, a.BudgetManHours))
            .ToListAsync(cancellationToken);
        var activitiesById = allActivities.ToDictionary(a => a.Id);

        // §4.3's scope(l) for the QUERY itself - the target activity set PI is being computed over.
        var targetActivityIds = ResolveTargetActivityIds(wbsNodeId, activityId, nodeLinks, allActivities);
        var anyActivityInScope = targetActivityIds.Count > 0;
        var budgetedTargetActivityIds = targetActivityIds
            .Where(id => activitiesById[id].BudgetManHours is not null)
            .ToHashSet();
        var anyBudgetedActivityInScope = budgetedTargetActivityIds.Count > 0;

        // §5.2: EMH = sum_i BMH_i * (P_i(b) - P_i(a))/100, using the identical ADR-0009 step
        // function EvmDataReader already uses for EV, at the same <= t boundary (§7.1's binding rule).
        var earnedManHours = 0m;
        if (budgetedTargetActivityIds.Count > 0)
        {
            var progressAtStart = await GetProgressAsOfAsync(budgetedTargetActivityIds, periodStartExclusive, cancellationToken);
            var progressAtEnd = await GetProgressAsOfAsync(budgetedTargetActivityIds, periodEndInclusive, cancellationToken);

            foreach (var id in budgetedTargetActivityIds)
            {
                var bmh = activitiesById[id].BudgetManHours!.Value;
                var deltaP = progressAtEnd.GetValueOrDefault(id) - progressAtStart.GetValueOrDefault(id);
                earnedManHours += bmh * deltaP / 100m;
            }
        }

        // §5.5's reporting-cadence gate: at least one progress observation for the budgeted target
        // activities inside the half-open bucket.
        var hasProgressObservationInPeriod = budgetedTargetActivityIds.Count > 0 && await dbContext.ActivityProgressLogs
            .AsNoTracking()
            .AnyAsync(
                l => budgetedTargetActivityIds.Contains(l.ActivityId)
                     && l.PeriodEndDate > periodStartExclusive
                     && l.PeriodEndDate <= periodEndInclusive,
                cancellationToken);

        var inForceRows = await LoadInForceRowsAsync(projectId, periodStartExclusive, periodEndInclusive, cancellationToken);

        var actualManHoursInScope = 0m;
        var actualManHoursTotal = 0m;
        var logEntryCount = 0;

        foreach (var row in inForceRows)
        {
            var rowScope = ResolveRowScope(row.ActivityId, row.WbsNodeId, nodeLinks, allActivities);

            // §4.3: a row belongs to this query iff its own scope(l) overlaps the query's target -
            // a row logged at a broader/ancestor node legitimately counts toward a narrower query
            // underneath it, and an unattributed row (scope(l) = every activity) belongs to every
            // query.
            if (!rowScope.Overlaps(targetActivityIds))
            {
                continue;
            }

            logEntryCount++;
            actualManHoursTotal += row.ManHours;

            // §4.3's exclusion rule is a property of the row's own scope, independent of the
            // query's narrower target.
            var matched = rowScope.Any(id => activitiesById.TryGetValue(id, out var a) && a.BudgetManHours is not null);
            if (matched)
            {
                actualManHoursInScope += row.ManHours;
            }
        }

        // §5.7(f)'s approximation for a multi-activity scope: any target-budgeted activity carries
        // an explicit 0.00 (not null) while the overall matched total is positive.
        var hasExplicitZeroBudgetWithMatchedHours = actualManHoursInScope > 0m
            && budgetedTargetActivityIds.Any(id => activitiesById[id].BudgetManHours == 0m);

        return new ProductivityIndexAggregate(
            earnedManHours,
            actualManHoursInScope,
            actualManHoursTotal,
            logEntryCount,
            anyActivityInScope,
            anyBudgetedActivityInScope,
            hasProgressObservationInPeriod,
            hasExplicitZeroBudgetWithMatchedHours);
    }

    public async Task<ManpowerReportingInputs> GetManningInputsAsync(
        Guid projectId, Guid? wbsNodeId, DateTimeOffset logDate, CancellationToken cancellationToken = default)
    {
        var periodStartExclusive = logDate.AddDays(-1);
        var rows = await LoadInForceRowsAsync(projectId, periodStartExclusive, logDate, cancellationToken);

        // Manning is a bare staffing question (§5.1) - every reported head for the exact scope,
        // matched or not to a budget, unlike PI's own AMH split. Exact-node match only (not a
        // subtree roll-up): the manning tile is a single scope's staffing compliance, not a rollup.
        var actualWorkerCount = rows.Where(r => r.WbsNodeId == wbsNodeId).Sum(r => r.WorkerCount);

        var plannedWorkerCount = await dbContext.ManpowerPlans.AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.WbsNodeId == wbsNodeId && p.EffectiveFrom <= logDate && p.EffectiveTo >= logDate)
            .Select(p => p.PlannedWorkerCount)
            .FirstOrDefaultAsync(cancellationToken);

        return new ManpowerReportingInputs(actualWorkerCount, plannedWorkerCount);
    }

    /// <summary>§4.7's effective log set (in force iff nothing points at it and it is not itself a
    /// Retraction - the identical rule <c>EotEffectiveLogSet</c> uses for <c>DailyWeatherLog</c>) +
    /// §4.5's half-open bucket, in one bounded pair of queries (candidates, then their
    /// <c>CorrectsLogId</c> pointers) rather than loading the project's whole history.</summary>
    private async Task<IReadOnlyList<LogRowInfo>> LoadInForceRowsAsync(
        Guid projectId, DateTimeOffset periodStartExclusive, DateTimeOffset periodEndInclusive, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.ManpowerEquipmentLogs.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.LogDate > periodStartExclusive && m.LogDate <= periodEndInclusive)
            .Select(m => new LogRowInfo(m.Id, m.WbsNodeId, m.ActivityId, m.ManHours, m.WorkerCount, m.EntryKind))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return candidates;
        }

        var candidateIds = candidates.Select(c => c.Id).ToList();
        var pointedAt = await dbContext.ManpowerEquipmentLogs.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.CorrectsLogId != null && candidateIds.Contains(m.CorrectsLogId!.Value))
            .Select(m => m.CorrectsLogId!.Value)
            .ToListAsync(cancellationToken);
        var pointedAtSet = pointedAt.ToHashSet();

        return candidates
            .Where(c => c.EntryKind != ManpowerLogEntryKind.Retraction && !pointedAtSet.Contains(c.Id))
            .ToList();
    }

    /// <summary>ADR-0009's step function, batched for a set of activity ids at one instant - the
    /// identical query shape <c>EvmDataReader.GetActivityInputsAsync</c> already uses per-activity,
    /// generalised here to two call sites (period start/end) over the same activity set.</summary>
    private async Task<IReadOnlyDictionary<Guid, decimal>> GetProgressAsOfAsync(
        IReadOnlyCollection<Guid> activityIds, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var idList = activityIds.ToList();
        var rows = await dbContext.Activities.AsNoTracking()
            .Where(a => idList.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                Progress = dbContext.ActivityProgressLogs
                    .Where(l => l.ActivityId == a.Id && l.PeriodEndDate <= asOf)
                    .OrderByDescending(l => l.PeriodEndDate)
                    .ThenByDescending(l => l.RecordedAt)
                    .Select(l => (decimal?)l.ProgressPercentage)
                    .FirstOrDefault() ?? 0m,
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => r.Progress);
    }

    /// <summary>§4.3's <c>scope(l)</c> for the QUERY: <c>{ActivityId}</c> when narrowed to one
    /// activity; every activity in <c>subtree(WbsNodeId)</c> (Tier 1) when narrowed to a node;
    /// otherwise every activity in the project.</summary>
    private static HashSet<Guid> ResolveTargetActivityIds(
        Guid? wbsNodeId, Guid? activityId, IReadOnlyList<WbsNodeParentLink> nodeLinks, IReadOnlyList<ActivityInfo> allActivities)
    {
        if (activityId is { } singleActivityId)
        {
            return allActivities.Any(a => a.Id == singleActivityId) ? [singleActivityId] : [];
        }

        if (wbsNodeId is { } rootWbsNodeId)
        {
            var subtree = WbsSubtreeResolver.ResolveSubtree(nodeLinks, rootWbsNodeId);
            return allActivities.Where(a => subtree.Contains(a.WbsNodeId)).Select(a => a.Id).ToHashSet();
        }

        return allActivities.Select(a => a.Id).ToHashSet();
    }

    /// <summary>§4.3's <c>scope(l)</c> for one log ROW: <c>{ActivityId}</c> when known; otherwise
    /// every activity in <c>subtree(WbsNodeId)</c> (Tier 1); otherwise (both null - a fully
    /// project-level/unattributed row) every activity in the project.</summary>
    private static HashSet<Guid> ResolveRowScope(
        Guid? rowActivityId, Guid? rowWbsNodeId, IReadOnlyList<WbsNodeParentLink> nodeLinks, IReadOnlyList<ActivityInfo> allActivities)
    {
        if (rowActivityId is { } activityId)
        {
            return [activityId];
        }

        if (rowWbsNodeId is { } wbsNodeId)
        {
            var subtree = WbsSubtreeResolver.ResolveSubtree(nodeLinks, wbsNodeId);
            return allActivities.Where(a => subtree.Contains(a.WbsNodeId)).Select(a => a.Id).ToHashSet();
        }

        return allActivities.Select(a => a.Id).ToHashSet();
    }
}
