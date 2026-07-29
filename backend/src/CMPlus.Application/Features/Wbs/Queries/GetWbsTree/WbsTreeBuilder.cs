using CMPlus.Application.Wbs;

namespace CMPlus.Application.Features.Wbs.Queries.GetWbsTree;

/// <summary>
/// S4-BE-01: nests a flat <see cref="WbsNodeFlatRow"/> list (one query's worth of data, already
/// loaded) into a <see cref="WbsTreeDto"/> - pure, in-memory, no I/O, so it is independently unit
/// testable and contributes nothing to the query's own database round-trip latency.
/// </summary>
public static class WbsTreeBuilder
{
    /// <summary>
    /// Sentinel key standing in for "no parent" (a root node) in <c>rowsByParent</c>/the traversal
    /// queue below. A plain <c>Guid?</c> can't be used as a <c>Dictionary</c>/<c>HashSet</c> key
    /// here - it is nullable, so it fails their <c>notnull</c> type-parameter constraint - and
    /// <c>Entity</c> (every <see cref="WbsNodeFlatRow.Id"/>'s ultimate source) always assigns a real
    /// <see cref="Guid.CreateVersion7"/> value, never <see cref="Guid.Empty"/>, so this can never
    /// collide with an actual node id.
    /// </summary>
    private static readonly Guid RootSentinel = Guid.Empty;

    public static WbsTreeDto Build(Guid projectId, IReadOnlyList<WbsNodeFlatRow> flatNodes)
    {
        // Sort once by Code, then bucket by parent while preserving that order - each parent's
        // children end up sorted by Code without a separate OrderBy per group.
        var rowsByParent = new Dictionary<Guid, List<WbsNodeFlatRow>>();
        foreach (var row in flatNodes.OrderBy(n => n.Code, StringComparer.Ordinal))
        {
            var parentKey = row.ParentWbsNodeId ?? RootSentinel;
            if (!rowsByParent.TryGetValue(parentKey, out var siblings))
            {
                siblings = [];
                rowsByParent[parentKey] = siblings;
            }

            siblings.Add(row);
        }

        // Breadth-first from the virtual root so every node is visited at most once and a node
        // unreachable from any root - e.g. an orphaned/cyclic chain that should never exist given
        // WBSNode.SetParent's own write-time cycle guard, but a read path must never *trust* that
        // structurally - is simply omitted rather than causing an infinite loop. An explicit queue
        // (never recursion) mirrors WBSNode.SetParent's own bounded-iteration discipline, so a
        // pathologically deep tree cannot overflow the call stack either.
        var visitOrder = new List<WbsNodeFlatRow>();
        var enqueuedIds = new HashSet<Guid> { RootSentinel };
        var pendingParentIds = new Queue<Guid>();
        pendingParentIds.Enqueue(RootSentinel);

        while (pendingParentIds.Count > 0)
        {
            var parentId = pendingParentIds.Dequeue();
            if (!rowsByParent.TryGetValue(parentId, out var childRows))
            {
                continue;
            }

            foreach (var childRow in childRows)
            {
                visitOrder.Add(childRow);
                if (enqueuedIds.Add(childRow.Id))
                {
                    pendingParentIds.Enqueue(childRow.Id);
                }
            }
        }

        // Build back-to-front: because visitOrder is parent-before-child (breadth-first), walking
        // it in reverse guarantees every node's children were already turned into completed DTOs
        // before the node itself is processed - so each parent can just look them up.
        var completedById = new Dictionary<Guid, WbsTreeNodeDto>();
        for (var i = visitOrder.Count - 1; i >= 0; i--)
        {
            var row = visitOrder[i];
            var childDtos = ChildrenOf(row.Id, rowsByParent, completedById);
            completedById[row.Id] = new WbsTreeNodeDto(
                row.Id, row.ParentWbsNodeId, row.Code, row.Title, row.WeightPercentage, row.ActivityCount, childDtos);
        }

        var rootNodes = ChildrenOf(RootSentinel, rowsByParent, completedById);
        return new WbsTreeDto(projectId, rootNodes);
    }

    private static IReadOnlyList<WbsTreeNodeDto> ChildrenOf(
        Guid parentKey,
        IReadOnlyDictionary<Guid, List<WbsNodeFlatRow>> rowsByParent,
        IReadOnlyDictionary<Guid, WbsTreeNodeDto> completedById) =>
        rowsByParent.TryGetValue(parentKey, out var childRows)
            ? childRows.Select(r => completedById[r.Id]).ToList()
            : [];
}
