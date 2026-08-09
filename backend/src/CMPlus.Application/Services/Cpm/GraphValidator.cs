namespace CMPlus.Application.Services.Cpm;

/// <summary>Stable error codes for a rejected CPM graph (S5-BE-03). <see cref="CpmEngine"/> folds
/// these into its own <c>Result&lt;CpmCalculationResult&gt;.Failure</c> message.</summary>
public static class CpmValidationErrorCodes
{
    public const string CycleDetected = "CpmCycleDetected";

    /// <summary>Same ordered (predecessor, successor) pair appears in more than one relation
    /// (regardless of <c>RelationType</c>/lag). Documented assumption, not a sourced rule: per
    /// cpm-fixtures.json's own gap note, no ADR currently resolves "reject or dedupe" for
    /// <c>ActivityRelation</c> - this validator rejects (fail-closed, consistent with ADR-0008's
    /// fail-closed precedent elsewhere in this codebase) and the choice is flagged back to
    /// system-architect/domain-expert in the Sprint 5 report to formalize with an ADR.</summary>
    public const string DuplicateRelation = "CpmDuplicateRelation";

    /// <summary>Defensive check: a relation references an activity id that was not supplied in the
    /// activity set at all (e.g. a caller-side data bug) - rejected before the engine ever
    /// dereferences a missing predecessor/successor duration.</summary>
    public const string UnknownActivityInRelation = "CpmUnknownActivityInRelation";
}

/// <summary>The outcome of <see cref="GraphValidator.Validate"/> - <see cref="CycleChain"/> is only
/// populated for <see cref="CpmValidationErrorCodes.CycleDetected"/>, and is the actual offending
/// chain (S5-BE-03 DoD: "cycle ... พร้อมคืน chain ที่ผิด" - reject with the offending chain), e.g.
/// <c>[A, B, A]</c> for the simplest 2-activity cycle.</summary>
public sealed class GraphValidationResult
{
    public bool IsValid { get; }

    public string? ErrorCode { get; }

    public IReadOnlyList<Guid>? CycleChain { get; }

    private GraphValidationResult(bool isValid, string? errorCode, IReadOnlyList<Guid>? cycleChain)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        CycleChain = cycleChain;
    }

    public static GraphValidationResult Valid() => new(true, null, null);

    public static GraphValidationResult Invalid(string errorCode, IReadOnlyList<Guid>? cycleChain = null) =>
        new(false, errorCode, cycleChain);
}

/// <summary>
/// S5-BE-03: rejects a relation cycle - with the offending chain - and any relation referencing an
/// activity outside the supplied set, BEFORE <see cref="CpmEngine"/>'s forward pass ever runs.
///
/// <para>Cannot literally reuse <c>CMPlus.Infrastructure.Parsers.Common.RelationGraphValidator</c>
/// (Sprint 3): that class is <c>internal</c> to <c>CMPlus.Infrastructure</c>, and per ADR-0001
/// dependencies point inward only - <c>CMPlus.Application</c> may never reference
/// <c>CMPlus.Infrastructure</c> at all, so importing it here would itself be a layering violation,
/// not a convenience. The true fix for literal code-sharing (extracting the DFS into a
/// zero-dependency <c>CMPlus.Domain.Common</c> helper both layers could depend on) would mean
/// touching Sprint 3's already-shipped, already-tested Infrastructure file - out of this sprint's
/// "keep to Sprint 5 scope" instruction and not backend-developer's call to make unilaterally on
/// previously-reviewed code. This class instead mirrors that validator's algorithm/style
/// (explicit, visible recursion-stack DFS, returns the ordered chain) rather than duplicating its
/// logic blindly - flagged back to system-architect in the Sprint 5 report as a follow-up worth a
/// deliberate refactor decision, not a silent one.</para>
///
/// <para>Unlike the Sprint 3 validator (genuine recursive <c>Visit()</c> calls, safe for XER/MSPDI
/// import-sized graphs), this one uses a fully iterative DFS with an explicit stack: the S4-DB-02
/// perf dataset's own relation network is a near-linear ~9,999-edge backbone chain, which is
/// exactly the shape that risks a call-stack overflow under real recursion at 10,000 activities.</para>
/// </summary>
public static class GraphValidator
{
    public static GraphValidationResult Validate(
        IReadOnlyCollection<CpmActivityInput> activities, IReadOnlyCollection<CpmRelationInput> relations)
    {
        var activityIds = new HashSet<Guid>(activities.Select(a => a.ActivityId));

        foreach (var relation in relations)
        {
            if (!activityIds.Contains(relation.PredecessorActivityId) || !activityIds.Contains(relation.SuccessorActivityId))
            {
                return GraphValidationResult.Invalid(CpmValidationErrorCodes.UnknownActivityInRelation);
            }
        }

        var seenPairs = new HashSet<(Guid Predecessor, Guid Successor)>();
        foreach (var relation in relations)
        {
            if (!seenPairs.Add((relation.PredecessorActivityId, relation.SuccessorActivityId)))
            {
                return GraphValidationResult.Invalid(CpmValidationErrorCodes.DuplicateRelation);
            }
        }

        var successors = BuildSuccessorAdjacency(relations);
        var cycle = FindCycle(activityIds, successors);

        return cycle is not null
            ? GraphValidationResult.Invalid(CpmValidationErrorCodes.CycleDetected, cycle)
            : GraphValidationResult.Valid();
    }

    private static Dictionary<Guid, List<Guid>> BuildSuccessorAdjacency(IReadOnlyCollection<CpmRelationInput> relations)
    {
        var successors = new Dictionary<Guid, List<Guid>>();
        foreach (var relation in relations)
        {
            if (!successors.TryGetValue(relation.PredecessorActivityId, out var list))
            {
                list = [];
                successors[relation.PredecessorActivityId] = list;
            }

            list.Add(relation.SuccessorActivityId);
        }

        return successors;
    }

    /// <summary>
    /// Iterative DFS (explicit stack of node + successor-enumerator, no native recursion) over
    /// <paramref name="successors"/> (predecessor -&gt; successors adjacency). Returns the cycle as
    /// an ordered chain (first activity repeated at the end, e.g. <c>[A, B, A]</c>), or
    /// <see langword="null"/> if the graph is acyclic.
    /// </summary>
    private static IReadOnlyList<Guid>? FindCycle(IReadOnlyCollection<Guid> activityIds, Dictionary<Guid, List<Guid>> successors)
    {
        var state = new Dictionary<Guid, int>(); // 0 = unvisited, 1 = in-stack, 2 = done
        var pathStack = new List<Guid>();
        var frames = new Stack<IEnumerator<Guid>>();

        foreach (var start in activityIds)
        {
            if (state.GetValueOrDefault(start) != 0)
            {
                continue;
            }

            state[start] = 1;
            pathStack.Add(start);
            frames.Push(GetSuccessors(start, successors));

            while (frames.Count > 0)
            {
                var enumerator = frames.Peek();

                if (enumerator.MoveNext())
                {
                    var next = enumerator.Current;
                    var nextState = state.GetValueOrDefault(next);

                    if (nextState == 1)
                    {
                        var startIndex = pathStack.IndexOf(next);
                        var cycle = pathStack.Skip(startIndex).ToList();
                        cycle.Add(next);
                        return cycle;
                    }

                    if (nextState == 0)
                    {
                        state[next] = 1;
                        pathStack.Add(next);
                        frames.Push(GetSuccessors(next, successors));
                    }
                }
                else
                {
                    frames.Pop();
                    var finished = pathStack[^1];
                    pathStack.RemoveAt(pathStack.Count - 1);
                    state[finished] = 2;
                }
            }
        }

        return null;
    }

    private static IEnumerator<Guid> GetSuccessors(Guid activityId, Dictionary<Guid, List<Guid>> successors) =>
        (successors.TryGetValue(activityId, out var list) ? list : Enumerable.Empty<Guid>()).GetEnumerator();
}
