using CMPlus.Application.Abstractions;
using CMPlus.Application.Wbs;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S4-BE-01: <see cref="IWbsTreeReader"/> against <see cref="CmPlusDbContext"/>.</summary>
public sealed class WbsTreeReader(CmPlusDbContext dbContext) : IWbsTreeReader
{
    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, cancellationToken);

    public async Task<IReadOnlyList<WbsNodeFlatRow>> GetNodesWithActivityCountsAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        // Query 1/2: every WBSNode in the project, projected (Select) to only the scalar columns
        // the tree needs - AsNoTracking (read-only, no change-tracking overhead) and no lazy
        // loading. Filters on ProjectId, which is the second column of the
        // (TenantId, ProjectId, ParentWbsNodeId) index WBSNodeConfiguration declares (the TenantId
        // predicate itself comes from the global tenant query filter, ADR-0002) - the shape
        // database-engineer verifies as an index seek at 5,000-node volume (S4-DB-01).
        var nodes = await dbContext.WBSNodes
            .AsNoTracking()
            .Where(n => n.ProjectId == projectId)
            .Select(n => new
            {
                n.Id,
                n.ParentWbsNodeId,
                n.Code,
                n.Title,
                n.WeightPercentage,
            })
            .ToListAsync(cancellationToken);

        // Query 2/2: a single bounded aggregate (COUNT per WbsNodeId), never one query per node -
        // that would reintroduce the exact N+1 pattern this DoD forbids. Deliberately selects only
        // a count, never any per-activity field, so a 10,000-activity project's payload stays
        // proportional to the ~5,000 WBS nodes rather than to total activity count (US-4.1). Mirrors
        // ImportRepository.LoadActivitiesForProjectAsync's "WBSNodes.Any(...)" join shape (Sprint 3
        // precedent) rather than an `IN (...)` over up to 5,000 node ids.
        var activityCountsByNodeId = await dbContext.Activities
            .Where(a => dbContext.WBSNodes.Any(w => w.Id == a.WbsNodeId && w.ProjectId == projectId))
            .GroupBy(a => a.WbsNodeId)
            .Select(g => new { WbsNodeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WbsNodeId, x => x.Count, cancellationToken);

        return nodes
            .Select(n => new WbsNodeFlatRow(
                n.Id,
                n.ParentWbsNodeId,
                n.Code,
                n.Title,
                n.WeightPercentage,
                activityCountsByNodeId.GetValueOrDefault(n.Id)))
            .ToList();
    }
}
