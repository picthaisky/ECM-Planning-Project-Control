using CMPlus.Domain.Enums;

namespace CMPlus.Application.Services.Cpm;

/// <summary>
/// S5-BE-01: one activity as the CPM engine sees it - just enough to run the forward/backward
/// pass (an id to key results by, and a duration in working days). Deliberately decoupled from
/// <see cref="CMPlus.Domain.Entities.Activity"/> (which needs a WbsNodeId/ActivityCode/Name/
/// BudgetCost/etc. just to construct) so <see cref="CpmEngine"/> is trivially unit-testable with
/// nothing but the cpm-method.md fixture's own <c>{id, durationDays}</c> shape.
/// <see cref="RecalculateCpmCommandHandler"/> maps the real <c>Activity</c> graph into this shape
/// and maps <see cref="CpmActivityResult"/> back onto the tracked entities.
/// </summary>
public sealed record CpmActivityInput(Guid ActivityId, int DurationDays);

/// <summary>One CPM precedence relation as the engine sees it - mirrors
/// <see cref="CMPlus.Domain.Entities.ActivityRelation"/>'s four fields exactly (S5-BE-02).</summary>
public sealed record CpmRelationInput(
    Guid PredecessorActivityId, Guid SuccessorActivityId, RelationType RelationType, int LagDays);

/// <summary>
/// One activity's CPM results. <see cref="EarlyStart"/>/<see cref="EarlyFinish"/>/
/// <see cref="LateStart"/>/<see cref="LateFinish"/> are plain working-day-count integers (day 0 =
/// project start), not calendar dates - see <see cref="CpmEngine"/>'s remarks. Only
/// <see cref="TotalFloat"/>, <see cref="FreeFloat"/> and <see cref="IsCritical"/> are persisted by
/// <c>RecalculateCpmCommand</c> (the three fields <c>Activity.SetCpmResults</c> exposes); the four
/// day-count fields are still returned here because they are what <see cref="TotalFloat"/>/
/// <see cref="FreeFloat"/> are derived from, useful for the engine's own tests and diagnostics.
/// </summary>
public sealed record CpmActivityResult(
    Guid ActivityId,
    int EarlyStart,
    int EarlyFinish,
    int LateStart,
    int LateFinish,
    int TotalFloat,
    int FreeFloat,
    bool IsCritical);

/// <summary>
/// The whole-project outcome of one <see cref="CpmEngine.Calculate"/> call. <see cref="CriticalPath"/>
/// is every <see cref="CpmActivityResult.IsCritical"/> activity in the graph's own topological
/// order - for a single critical chain (the common case, and the only shape cpm-method.md's
/// canonical fixture exercises) this is exactly "the" critical path; for a network with multiple
/// disjoint critical chains it is one merged, topologically-consistent ordering rather than
/// separate paths per chain (a documented simplification - each activity's own
/// <see cref="CpmActivityResult.IsCritical"/> flag, which is what actually gets persisted, is
/// unaffected by this and is always correct).
/// </summary>
public sealed record CpmCalculationResult(
    IReadOnlyList<CpmActivityResult> Activities,
    int ProjectDurationDays,
    IReadOnlyList<Guid> CriticalPath);
