namespace CMPlus.Infrastructure.Parsers.Common;

/// <summary>
/// Detects a cycle in a predecessor -&gt; successor relation graph, over a file's own external ids
/// (P6 <c>task_id</c>, MSPDI <c>UID</c>, ...), before any <see cref="Domain.Entities.ActivityRelation"/>
/// is constructed. Shared by the XER (S3-BE-01) and MSPDI (S3-BE-02) parsers - both DoDs require a
/// relation cycle to be rejected, with the offending chain identified, before anything is persisted.
/// An explicit iterative-recursion DFS with a visible recursion stack - not the graph algorithm CPM
/// itself will need in Sprint 5 (that one also computes float/critical path over an already-
/// validated, acyclic graph; this one only needs to answer "is there a cycle, and if so which
/// activity codes are in it").
/// </summary>
internal static class RelationGraphValidator
{
    /// <summary>
    /// <paramref name="edges"/>: predecessor external id -&gt; successor external id. <paramref name="codeOf"/>
    /// resolves an external id to its human-readable <c>ActivityCode</c> for the reported chain.
    /// Returns the cycle as an ordered list of activity codes (first repeated at the end,
    /// e.g. <c>["A-1010", "A-1020", "A-1030", "A-1010"]</c>), or <c>null</c> if the graph is acyclic.
    /// </summary>
    public static IReadOnlyList<string>? FindCycle(
        IReadOnlyDictionary<string, List<string>> edges, Func<string, string> codeOf)
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0=unvisited,1=in-stack,2=done
        var pathStack = new List<string>();

        foreach (var start in edges.Keys)
        {
            if (state.GetValueOrDefault(start) == 0)
            {
                var cycle = Visit(start, edges, state, pathStack);
                if (cycle is not null)
                {
                    return cycle.Select(codeOf).ToList();
                }
            }
        }

        return null;
    }

    private static List<string>? Visit(
        string node,
        IReadOnlyDictionary<string, List<string>> edges,
        Dictionary<string, int> state,
        List<string> pathStack)
    {
        state[node] = 1;
        pathStack.Add(node);

        if (edges.TryGetValue(node, out var successors))
        {
            foreach (var next in successors)
            {
                var nextState = state.GetValueOrDefault(next);
                if (nextState == 1)
                {
                    // Found the back-edge that closes the cycle: slice the stack from the first
                    // occurrence of `next` onward, then close the loop by repeating `next`.
                    var startIndex = pathStack.IndexOf(next);
                    var cycle = pathStack.Skip(startIndex).ToList();
                    cycle.Add(next);
                    return cycle;
                }

                if (nextState == 0)
                {
                    var found = Visit(next, edges, state, pathStack);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }
        }

        pathStack.RemoveAt(pathStack.Count - 1);
        state[node] = 2;
        return null;
    }
}
