using CMPlus.Application.Gantt;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// S6-BE-01: reads the lean <see cref="GanttActivityFlatRow"/> set for a project's Gantt chart.
/// Deliberately separate from <see cref="IWbsTreeReader"/> - <c>GetGanttQueryHandler</c> reuses that
/// existing reader for the WBS hierarchy/ordering (its <c>ProjectExistsAsync</c> and
/// <c>GetNodesWithActivityCountsAsync</c> are exactly what a Gantt read also needs for row order,
/// per this feature's "reuse Sprint 4's tree-building approach rather than inventing a third shape"
/// instruction) and only needs this reader for the activity bars themselves. Implemented in
/// Infrastructure against <c>CmPlusDbContext</c> (ADR-0001), tenant-scoped by the global EF query
/// filter (ADR-0002).
/// </summary>
public interface IGanttActivityReader
{
    /// <summary>
    /// Every <c>Activity</c> under <paramref name="projectId"/>'s WBS, optionally windowed to those
    /// whose <em>planned</em> span overlaps <c>[from, to]</c> (either bound may be omitted
    /// independently - a one-sided window is "from this date onward"/"up to this date").
    ///
    /// <para>Design decision (S6-BE-01, "รองรับ query ตามช่วงวันที่"): windowing is evaluated
    /// against <c>PlannedStart</c>/<c>PlannedFinish</c> only, not <c>ActualStart</c>/<c>ActualFinish</c>
    /// - the planned dates are what position a bar on the Gantt's timeline axis in the first place,
    /// so they are also what decide whether that bar falls inside a requested time window. Actual
    /// dates are still returned on every row (for the actual-vs-planned overlay a real Gantt needs)
    /// but do not independently pull an activity into or out of the window; an activity that
    /// finished later than planned is windowed by where its bar started, not by how late it ran.
    /// This keeps the filter semantics simple and unambiguous for the frontend's zoom/scroll paging
    /// (S6-FE-02) at the cost of a (rare, and already visible via the returned actual-date fields)
    /// edge case: an activity whose planned window is just outside <c>[from, to]</c> but whose
    /// actual run overlaps it will not appear until the window is widened.</para>
    /// </summary>
    Task<IReadOnlyList<GanttActivityFlatRow>> GetActivitiesAsync(
        Guid projectId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default);

    /// <summary>Sprint 6 gap-closure: <c>Project.DataDate</c>, needed for the Gantt's vertical
    /// data-date line (S6-FE-01 DoD, "มีเส้น data date ตำแหน่งถูกต้อง") - previously exposed by no
    /// reachable endpoint at all (frontend-developer's own flagged gap; `GanttChart` shipped with a
    /// `dataDateIso: null` fallback pending this). Caller (<see cref="GetGanttQueryHandler"/>) has
    /// already confirmed the project exists via <c>IWbsTreeReader.ProjectExistsAsync</c>, so this
    /// returns a plain scalar, not a <c>Result</c>/nullable-for-not-found shape.</summary>
    Task<DateTimeOffset> GetProjectDataDateAsync(Guid projectId, CancellationToken cancellationToken = default);
}
