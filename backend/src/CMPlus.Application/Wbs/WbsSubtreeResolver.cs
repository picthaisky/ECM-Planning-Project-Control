namespace CMPlus.Application.Wbs;

/// <summary>A node's own id and parent pointer only - the minimal shape
/// <see cref="WbsSubtreeResolver"/> needs, decoupled from <see cref="WbsNodeFlatRow"/>'s wider
/// tree-endpoint payload (Code/Title/WeightPercentage/ActivityCount) so any caller can build one
/// straight from a lean <c>Select(n =&gt; new { n.Id, n.ParentWbsNodeId })</c> projection.</summary>
public readonly record struct WbsNodeParentLink(Guid Id, Guid? ParentWbsNodeId);

/// <summary>
/// Pure in-memory WBS subtree closure - domain-rules.md (manpower-equipment) §4.3's
/// <c>subtree(WbsNodeId)</c> (Tier 1 scope matching). Operates over an already-loaded flat row set
/// (the same "load once, build tree in memory" shape <c>WbsTreeBuilder</c> uses for the WBS-tree
/// endpoint itself) rather than a recursive SQL query, so it is trivially unit-testable and reusable
/// by any reader that needs "this node and everything under it", not only the tree endpoint.
/// </summary>
public static class WbsSubtreeResolver
{
    /// <summary>Every node id reachable from <paramref name="rootId"/> (inclusive) by following
    /// <see cref="WbsNodeParentLink.ParentWbsNodeId"/> downward. Iterative (never recursive), so an
    /// unusually deep tree cannot cause a stack overflow - the same discipline
    /// <c>WBSNode.SetParent</c>'s own ancestor walk already uses.</summary>
    public static IReadOnlySet<Guid> ResolveSubtree(IReadOnlyList<WbsNodeParentLink> allNodes, Guid rootId)
    {
        var childrenByParent = allNodes
            .Where(n => n.ParentWbsNodeId is not null)
            .GroupBy(n => n.ParentWbsNodeId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());

        var result = new HashSet<Guid> { rootId };
        var stack = new Stack<Guid>();
        stack.Push(rootId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var childId in children)
            {
                if (result.Add(childId))
                {
                    stack.Push(childId);
                }
            }
        }

        return result;
    }
}
