namespace CMPlus.Application.Abstractions;

/// <summary>
/// S12-BE-02: resolves every already-aggregated scalar
/// <see cref="CMPlus.Application.Services.Manpower.ProductivityIndexCalculator.Compute"/> needs for
/// one scope over one half-open period, per domain-rules.md (manpower-equipment) §4.3/§4.5/§5. This
/// is the seam that keeps the pure calculator free of EF Core (ADR-0001) - all WBS-subtree
/// resolution (§4.3 Tier 1), row-to-budget matching, half-open bucketing (§4.5, shared with $AC$) and
/// the §5.5 reporting-cadence check happen here, in Infrastructure.
/// </summary>
public interface IProductivityIndexReader
{
    /// <summary>Tenant-scoped by the global EF query filter (ADR-0002) - a project id belonging to
    /// another tenant is indistinguishable from "does not exist".</summary>
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Of the given ids, the subset that are real <c>WBSNode</c> ids belonging to this
    /// project - used to fail closed (404, ADR-0002) on a cross-tenant/unknown scope id before ever
    /// computing anything (fixture M-06i/M-14b).</summary>
    Task<bool> WbsNodeExistsInProjectAsync(Guid projectId, Guid wbsNodeId, CancellationToken cancellationToken = default);

    /// <summary>Same fail-closed contract as <see cref="WbsNodeExistsInProjectAsync"/>, for
    /// <c>Activity</c> ids.</summary>
    Task<bool> ActivityExistsInProjectAsync(Guid projectId, Guid activityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="wbsNodeId"/> = <see langword="null"/> and <paramref name="activityId"/> =
    /// <see langword="null"/> together mean "the whole project" (§4.3's Tier 1 project scope);
    /// <paramref name="activityId"/> narrows to exactly that one activity (§4.3's <c>scope(l) = {ActivityId}</c>
    /// case); otherwise the scope is <paramref name="wbsNodeId"/>'s own subtree closure. The period is
    /// half-open, lower-exclusive <c>(periodStartExclusive, periodEndInclusive]</c> - identical to
    /// $AC$'s own convention (§4.5) - so a cumulative read passes a sentinel
    /// <paramref name="periodStartExclusive"/> far enough in the past that every activity's progress
    /// reads as 0 at that instant (ADR-0009).
    /// </summary>
    Task<Services.Manpower.ProductivityIndexAggregate> GetAggregateAsync(
        Guid projectId,
        Guid? wbsNodeId,
        Guid? activityId,
        DateTimeOffset periodStartExclusive,
        DateTimeOffset periodEndInclusive,
        CancellationToken cancellationToken = default);

    /// <summary>domain-rules.md §5.1/§9.3 - the manning ratio's two raw inputs for exactly one
    /// calendar day and scope: <c>WorkerCount</c> actually reported (summed over in-force log rows
    /// for the scope on that day, matched or not - manning is a staffing question, not a budgeted-
    /// hours one) and the <c>ManpowerPlan.PlannedWorkerCount</c> effective on that day for the same
    /// <paramref name="wbsNodeId"/> (<see langword="null"/> falls back to a project-wide plan row, if
    /// any). <see cref="ManpowerReportingInputs.PlannedWorkerCount"/> is <see langword="null"/> - never
    /// 0 - when no plan covers that day (ADR-0015 discipline).</summary>
    Task<Services.Manpower.ManpowerReportingInputs> GetManningInputsAsync(
        Guid projectId, Guid? wbsNodeId, DateTimeOffset logDate, CancellationToken cancellationToken = default);
}
