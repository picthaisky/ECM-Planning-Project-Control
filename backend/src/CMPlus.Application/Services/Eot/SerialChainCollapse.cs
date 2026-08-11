using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Services.Eot;

/// <summary>
/// S11-BE-02 (domain-expert ruling 2026-08-10 on `qa-engineer`'s S11-QA-01 cap-invariant escalation,
/// domain-rules.md weather-eot §5.2a): "one day, one path, one charge". Per countable date, reduces
/// the set of activities charged that date to a maximal antichain of CM+'s CPM comparability relation
/// $\prec_r$, so a single day of weather is never charged into more than one point of the same path.
/// This is the fix for §5.3's hypothesis H2 (the theorem the cap - "one calendar day, one EOT day" -
/// rests on): §3.7 stays deliberately permissive (a storm really can stop an activity and its own
/// predecessor on the same day, and that stays valid, recordable input), and the fix lives here, in
/// the summation, not in a countability gate.
///
/// <para>Pure, no I/O - mirrors <see cref="CpmEngine"/>/<see cref="EotCountabilityGate"/>'s own style,
/// and deliberately its own class (never inlined into <see cref="EotEvaluator"/>) so the comparability
/// test and the antichain reduction are independently unit-testable against domain-rules.md's own
/// worked fixtures (W-17, W-19, W-20) - see the dedicated <c>SerialChainCollapseTests</c>.</para>
///
/// <para><b>The comparability test is reachability in the start/finish graph - never "is a transitive
/// predecessor".</b> Two nodes per activity ($j^S$, $j^F$); one internal edge $j^S \to j^F$ per
/// activity (this is where the activity's own duration, and therefore its charged days, "live"), plus
/// one relation edge per (predecessor, successor) pair, by type: FS $p^F \to s^S$ · SS $p^S \to s^S$ ·
/// FF $p^F \to s^F$ · SF $p^S \to s^F$ (lag does not affect reachability and is ignored).
/// $u \prec_r v \iff v^S$ is reachable from $u^F$ - "there is a route through the logic on which $u$
/// must finish before $v$ starts", so both durations add into the length of at least one common path.
/// An SS/FF-only link between two activities leaves them <b>incomparable</b> under this test (their
/// durations never both enter one path's length, so both charges are real, e.g. two activities running
/// genuinely side by side) - substituting a naive "is $v$ a transitive successor of $u$" test collapses
/// them anyway and under-states $E$ by a day (fixture W-19 exists to fail exactly that
/// implementation).</para>
///
/// <para><b>The reduction is least-float-first, ties upstream-first - never "keep the
/// predecessor".</b> Per date, sort that date's charged activities ascending by
/// (<c>TotalFloatAtRun</c>, topological position in the governing run, <c>ActivityCode</c> ordinal)
/// and greedily keep each activity that is $\prec_r$-incomparable with every activity already kept in
/// that date's antichain; a rejected activity's charge for that date is <b>absorbed</b> - never
/// deleted, never given an <c>ExclusionReason</c> - and recorded against whichever already-kept
/// activity(ies) it was comparable with, for domain-rules.md §5.4's disclosure
/// (<c>SerialChainAbsorbedDays</c>/<c>AbsorbedIntoActivityCodes</c>). Fixture W-20 is the case that
/// separates the two rules by the whole answer: keeping the upstream activity yields $E = 0$; keeping
/// the least-float activity (here, the successor) yields the correct $E = 2$ - the activity with the
/// smaller float sits on the more binding path.</para>
/// </summary>
public static class SerialChainCollapse
{
    /// <summary>One governing run's collapse outcome: which (activity, date) weights survived into
    /// the network (feed <c>EotEvaluator</c>'s $\Delta D_j$) versus which were absorbed into a
    /// serially-chained activity, plus - per absorbed activity - the distinct set of activities that
    /// absorbed at least one of its dates (domain-rules.md §5.4's <c>AbsorbedIntoActivityCodes</c>,
    /// here still keyed by id; <c>EotEvaluator</c> resolves the codes).</summary>
    public sealed record CollapseResult(
        IReadOnlyDictionary<Guid, List<decimal>> KeptWeightsByActivity,
        IReadOnlyDictionary<Guid, List<decimal>> AbsorbedWeightsByActivity,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> AbsorbedIntoActivityIds);

    /// <param name="weightsByDate">Every activity charged on each countable date within one governing
    /// run, with that date's own (already §3.4-clamped) day-weight, before the collapse.</param>
    /// <param name="runActivityIds">Only activities the governing run's own network actually knows
    /// about can be positioned in it or compared at all (defensive - not exercised by any
    /// domain-rules.md fixture, mirrors <c>EotEvaluator</c>'s own pre-existing guard); any weight for
    /// an activity outside this set is silently dropped, exactly as before this change.</param>
    /// <param name="relations">The governing run's own captured topology (§5.2a: never the live/current
    /// network - re-sequencing after the fact must not retroactively change a past evaluation).</param>
    /// <param name="totalFloatAtRun">$TF_j^{(r)}$ per activity - the primary sort/tie-break key
    /// ("least float first").</param>
    /// <param name="topologicalIndex">Each activity's position in the governing run's own topological
    /// order (<c>CpmCalculationResult.Activities</c>'s own order, i.e. Kahn's algorithm order) - the
    /// secondary tie-break ("ties go upstream-first") and also how <c>AbsorbedIntoActivityIds</c> is
    /// ordered.</param>
    /// <param name="activityCode">The tertiary, final, deterministic tie-break ("ActivityCode
    /// ordinal").</param>
    public static CollapseResult Collapse(
        IReadOnlyDictionary<DateOnly, Dictionary<Guid, decimal>> weightsByDate,
        IReadOnlySet<Guid> runActivityIds,
        IReadOnlyList<CpmRelationInput> relations,
        IReadOnlyDictionary<Guid, int> totalFloatAtRun,
        IReadOnlyDictionary<Guid, int> topologicalIndex,
        IReadOnlyDictionary<Guid, string> activityCode)
    {
        var comparability = new StartFinishReachability(runActivityIds, relations);

        var kept = new Dictionary<Guid, List<decimal>>();
        var absorbed = new Dictionary<Guid, List<decimal>>();
        var absorbedInto = new Dictionary<Guid, List<Guid>>();

        // Deterministic date order - the algorithm itself is per-date-independent (a date's antichain
        // never depends on another date's), but a stable order keeps this reproducible/debuggable.
        foreach (var (_, activityWeights) in weightsByDate.OrderBy(kv => kv.Key))
        {
            var candidates = activityWeights.Keys
                .Where(runActivityIds.Contains)
                .OrderBy(id => totalFloatAtRun.GetValueOrDefault(id, int.MaxValue))
                .ThenBy(id => topologicalIndex.GetValueOrDefault(id, int.MaxValue))
                .ThenBy(id => activityCode.GetValueOrDefault(id, string.Empty), StringComparer.Ordinal)
                .ToList();

            var keptToday = new List<Guid>();
            foreach (var candidateId in candidates)
            {
                var absorbers = keptToday.Where(k => comparability.AreComparable(k, candidateId)).ToList();
                if (absorbers.Count == 0)
                {
                    keptToday.Add(candidateId);
                    continue;
                }

                Add(absorbed, candidateId, activityWeights[candidateId]);
                if (!absorbedInto.TryGetValue(candidateId, out var into))
                {
                    into = [];
                    absorbedInto[candidateId] = into;
                }

                foreach (var absorberId in absorbers)
                {
                    if (!into.Contains(absorberId))
                    {
                        into.Add(absorberId);
                    }
                }
            }

            foreach (var keptId in keptToday)
            {
                Add(kept, keptId, activityWeights[keptId]);
            }
        }

        var absorbedIntoSorted = absorbedInto.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<Guid>)kv.Value.OrderBy(id => topologicalIndex.GetValueOrDefault(id, int.MaxValue)).ToList());

        return new CollapseResult(kept, absorbed, absorbedIntoSorted);
    }

    private static void Add(Dictionary<Guid, List<decimal>> weights, Guid activityId, decimal weight)
    {
        if (!weights.TryGetValue(activityId, out var list))
        {
            list = [];
            weights[activityId] = list;
        }

        list.Add(weight);
    }

    /// <summary>domain-rules.md §5.2a's start/finish graph $G_r^{\pm}$ - built once per governing run
    /// and reused for every date's antichain reduction within it. "Cost": charged activities per run
    /// are a handful, so one BFS from each charged activity's own Finish node (memoized per activity,
    /// never recomputed) decides every pair it is ever asked about - no transitive closure of the
    /// whole network, and no change to <see cref="CpmEngine"/> itself.</summary>
    private sealed class StartFinishReachability
    {
        private readonly Dictionary<Node, List<Node>> _graph = [];
        private readonly Dictionary<Guid, HashSet<Node>> _reachableFromFinishCache = [];

        public StartFinishReachability(IReadOnlySet<Guid> activityIds, IReadOnlyList<CpmRelationInput> relations)
        {
            foreach (var activityId in activityIds)
            {
                AddEdge(new Node(activityId, IsFinish: false), new Node(activityId, IsFinish: true));
            }

            foreach (var relation in relations)
            {
                var (from, to) = relation.RelationType switch
                {
                    RelationType.FS => (new Node(relation.PredecessorActivityId, true), new Node(relation.SuccessorActivityId, false)),
                    RelationType.SS => (new Node(relation.PredecessorActivityId, false), new Node(relation.SuccessorActivityId, false)),
                    RelationType.FF => (new Node(relation.PredecessorActivityId, true), new Node(relation.SuccessorActivityId, true)),
                    RelationType.SF => (new Node(relation.PredecessorActivityId, false), new Node(relation.SuccessorActivityId, true)),
                    _ => throw new ArgumentOutOfRangeException(nameof(relations), relation.RelationType, "Unsupported CPM relation type."),
                };
                AddEdge(from, to);
            }
        }

        /// <summary>Two activities are comparable - i.e. their charged days can never both survive
        /// the same date's antichain - iff either one's Finish node reaches the other's Start node.</summary>
        public bool AreComparable(Guid a, Guid b) => IsPredecessor(a, b) || IsPredecessor(b, a);

        /// <summary>$u \prec_r v \iff v^S$ is reachable from $u^F$.</summary>
        private bool IsPredecessor(Guid u, Guid v)
        {
            if (!_reachableFromFinishCache.TryGetValue(u, out var reachable))
            {
                reachable = Bfs(new Node(u, IsFinish: true));
                _reachableFromFinishCache[u] = reachable;
            }

            return reachable.Contains(new Node(v, IsFinish: false));
        }

        private HashSet<Node> Bfs(Node source)
        {
            var visited = new HashSet<Node> { source };
            var queue = new Queue<Node>();
            queue.Enqueue(source);

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (!_graph.TryGetValue(node, out var neighbours))
                {
                    continue;
                }

                foreach (var neighbour in neighbours)
                {
                    if (visited.Add(neighbour))
                    {
                        queue.Enqueue(neighbour);
                    }
                }
            }

            return visited;
        }

        private void AddEdge(Node from, Node to)
        {
            if (!_graph.TryGetValue(from, out var list))
            {
                list = [];
                _graph[from] = list;
            }

            list.Add(to);
        }

        private readonly record struct Node(Guid ActivityId, bool IsFinish);
    }
}
