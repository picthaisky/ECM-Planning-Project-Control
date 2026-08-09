using CMPlus.Application.Abstractions;
using CMPlus.Application.Gantt;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S6-BE-01: <see cref="IGanttActivityReader"/> against <see cref="CmPlusDbContext"/>.</summary>
public sealed class GanttActivityReader(CmPlusDbContext dbContext) : IGanttActivityReader
{
    public async Task<IReadOnlyList<GanttActivityFlatRow>> GetActivitiesAsync(
        Guid projectId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
    {
        // AsNoTracking (read-only) - mirrors WbsTreeReader's "WBSNodes.Any(...)" join shape rather
        // than an `IN (...)` over up to ~5,000 node ids, and the TenantId predicate itself comes
        // from the global tenant query filter (ADR-0002) on both Activities and WBSNodes.
        var query = dbContext.Activities
            .AsNoTracking()
            .Where(a => dbContext.WBSNodes.Any(w => w.Id == a.WbsNodeId && w.ProjectId == projectId));

        // Date-range window (see IGanttActivityReader's remarks): evaluated against the planned
        // span only. PlannedFinish >= from and PlannedStart <= to together express "this activity's
        // planned bar overlaps [from, to]"; either bound may be absent independently.
        if (from is not null)
        {
            query = query.Where(a => a.PlannedFinish >= from.Value);
        }

        if (to is not null)
        {
            query = query.Where(a => a.PlannedStart <= to.Value);
        }

        return await query
            .Select(a => new GanttActivityFlatRow(
                a.Id,
                a.WbsNodeId,
                a.ActivityCode,
                a.Name,
                a.PlannedStart,
                a.PlannedFinish,
                a.ActualStart,
                a.ActualFinish,
                a.IsCritical,
                a.TotalFloat,
                a.FreeFloat))
            .ToListAsync(cancellationToken);
    }

    public async Task<DateTimeOffset> GetProjectDataDateAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.DataDate)
            .SingleAsync(cancellationToken);
}
