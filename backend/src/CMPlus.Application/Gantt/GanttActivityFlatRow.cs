namespace CMPlus.Application.Gantt;

/// <summary>
/// S6-BE-01: a single, lean projection of an <c>Activity</c> row carrying only the fields a Gantt
/// bar needs to draw itself - never the full entity (<see cref="CMPlus.Domain.Entities.Activity"/>
/// also carries <c>ProgressPercentage</c>/progress-cache metadata the Gantt chart itself does not
/// render; a progress cell/tooltip is a different screen's concern). Returned by
/// <see cref="CMPlus.Application.Abstractions.IGanttActivityReader"/> in one shot for a project
/// (optionally windowed by <c>from</c>/<c>to</c> - see that interface's remarks), mirroring
/// <c>WbsNodeFlatRow</c>'s "flat projection + pure in-memory ordering" shape
/// (<c>Features/Gantt/Queries/GetGantt/GanttRowOrderer</c>) rather than inventing a third payload
/// shape alongside <c>WbsTreeDto</c>'s nested tree and this flat-row convention.
/// </summary>
public sealed record GanttActivityFlatRow(
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
