using CMPlus.Application.Wbs;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// S4-BE-01: reads the whole WBS tree for a project as a flat row set in a single, lean projection
/// query - never entity-by-entity, never one query per node. This is the seam that keeps
/// <c>GetWbsTreeQueryHandler</c> free of EF Core (ADR-0001) while still letting Infrastructure issue
/// exactly the two bounded queries the < 100 ms / 5,000-node budget (docs/10 §6 S4-BE-01, docs/9 §1)
/// requires: one for the node shape, one for the per-node activity-count rollup (never one query per
/// node - that would be the classic parent/child N+1 trap a tree read is especially prone to).
/// Implemented in Infrastructure against <c>CmPlusDbContext</c>, using the
/// <c>(TenantId, ProjectId, ParentWbsNodeId)</c> index <c>WBSNodeConfiguration</c> already declares
/// (docs/db-conventions.md §6) - <c>database-engineer</c> verifies the resulting execution plan is
/// an index seek, not a scan, at 5,000-node/10,000-activity volume (S4-DB-01).
/// </summary>
public interface IWbsTreeReader
{
    /// <summary>Tenant-scoped by the global EF query filter (ADR-0002) - a project id belonging to
    /// another tenant is indistinguishable from "does not exist".</summary>
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every <c>WBSNode</c> in the project, each carrying only its own scalar columns plus a
    /// rolled-up <see cref="WbsNodeFlatRow.ActivityCount"/> - never the activities themselves. This
    /// is what keeps the initial WBS-tree payload proportional to the (bounded, ~5,000) node count
    /// rather than to the total activity count across the tree (US-4.1: "the payload supports
    /// lazy/paginated child-loading so the initial response does not scale linearly with total
    /// activity count") - a project's full activity list per node is fetched lazily/paginated by a
    /// separate query, out of this sprint's scope.
    /// </summary>
    Task<IReadOnlyList<WbsNodeFlatRow>> GetNodesWithActivityCountsAsync(
        Guid projectId, CancellationToken cancellationToken = default);
}
