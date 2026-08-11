namespace CMPlus.Application.Features.Gantt.Queries.GetGantt;

/// <summary>S6-BE-01 response shape: every activity bar the Gantt needs to draw for a project,
/// already in WBS-tree row order (see <see cref="GanttRowOrderer"/>) - the frontend renders this
/// array top-to-bottom directly, it does not re-sort or re-nest it.</summary>
/// <param name="DataDate"><c>Project.DataDate</c> - positions the Gantt's vertical data-date line
/// (gap closure, added after S6-FE-01 shipped with no source for this at all).</param>
public sealed record GanttDto(Guid ProjectId, DateTimeOffset DataDate, IReadOnlyList<GanttActivityDto> Activities);

/// <summary>
/// One activity bar - deliberately lean (ADR-0004's virtualized-rendering DoD: "คืนเฉพาะข้อมูลที่
/// จำเป็นต่อการวาด", return only what's needed to draw). No <c>ProgressPercentage</c>, no budget/cost
/// fields, no WBS title/weight - those belong to the WBS tree (Sprint 4) and progress
/// (Sprint 4/EVM) screens, not the Gantt bar itself.
/// </summary>
/// <param name="Id">Activity id.</param>
/// <param name="WbsNodeId">Correlates this bar to the WBS tree (<c>GetWbsTreeQuery</c>) node it
/// belongs to, so the frontend can align/group this row with that already-fetched tree without a
/// second, redundant hierarchy payload.</param>
/// <param name="ActivityCode">Left-side row label, primary line.</param>
/// <param name="Name">Left-side row label, secondary line.</param>
/// <param name="PlannedStart">Bar start on the planned/current schedule.</param>
/// <param name="PlannedFinish">Bar end on the planned/current schedule.</param>
/// <param name="ActualStart">Actual-progress overlay start, if recorded.</param>
/// <param name="ActualFinish">Actual-progress overlay end, if recorded.</param>
/// <param name="IsCritical">Critical (red) vs non-critical (slate-blue) bar coloring, per ADR-0006's
/// token scheme - written by the Sprint 5 CPM engine (<c>RecalculateCpmCommand</c>).</param>
/// <param name="TotalFloat">Days of total float, for a tooltip; null until CPM has run at least
/// once for this project.</param>
/// <param name="FreeFloat">Days of free float, for a tooltip; null until CPM has run at least once
/// for this project.</param>
public sealed record GanttActivityDto(
    Guid Id,
    Guid WbsNodeId,
    string ActivityCode,
    string Name,
    DateTimeOffset PlannedStart,
    DateTimeOffset PlannedFinish,
    DateTimeOffset? ActualStart,
    DateTimeOffset? ActualFinish,
    bool IsCritical,
    int? TotalFloat,
    int? FreeFloat);
