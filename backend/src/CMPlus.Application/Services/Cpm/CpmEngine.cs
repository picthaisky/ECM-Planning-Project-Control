using CMPlus.Domain.Common;

namespace CMPlus.Application.Services.Cpm;

/// <summary>
/// S5-BE-01: pure Application-layer CPM engine - forward pass (ES/EF), backward pass (LS/LF),
/// Total Float, Free Float and the critical path, per cpm-method.md. No dependency on EF Core (or
/// any other package beyond MediatR/FluentValidation, neither of which this class uses) anywhere
/// in this class or its immediate collaborators (<see cref="RelationConstraints"/>,
/// <see cref="GraphValidator"/>) - the whole <c>Services/Cpm</c> folder is unit-testable with
/// nothing but plain CLR collections, per ADR-0001's "engines unit-test without DB".
///
/// <para>ES/EF/LS/LF are plain working-day-count integers (day 0 = project start), never calendar
/// dates - this is what makes cpm-method.md's canonical fixture (project start day 0, "ES_A=0,
/// EF_A=5; ES_B=5, EF_B=8; ..." etc.) reproduce byte-for-byte regardless of which real calendar a
/// project uses (see <see cref="WorkingCalendar"/> for the separate calendar-date-facing half of
/// S5-BE-02). Total/Free Float, being simple differences of these day-counts, are therefore also
/// origin-independent - exactly the two numbers (plus <c>IsCritical</c>) that
/// <c>RecalculateCpmCommand</c> persists onto <c>Activity</c>.</para>
/// </summary>
public static class CpmEngine
{
    public static Result<CpmCalculationResult> Calculate(
        IReadOnlyCollection<CpmActivityInput> activities, IReadOnlyCollection<CpmRelationInput> relations)
    {
        if (activities.Count == 0)
        {
            // No crash on an empty graph - a degenerate case one level beyond S5-BE-03's "isolated
            // activity" DoD (a whole project with zero activities yet), handled the same
            // fail-safe way.
            return Result<CpmCalculationResult>.Success(new CpmCalculationResult([], 0, []));
        }

        var validation = GraphValidator.Validate(activities, relations);
        if (!validation.IsValid)
        {
            var detail = validation.CycleChain is { Count: > 0 }
                ? $"{validation.ErrorCode}: {string.Join(" -> ", validation.CycleChain)}"
                : validation.ErrorCode!;
            return Result<CpmCalculationResult>.Failure(detail);
        }

        var durationByActivity = activities.ToDictionary(a => a.ActivityId, a => a.DurationDays);

        var predecessorsOf = activities.ToDictionary(a => a.ActivityId, _ => new List<CpmRelationInput>());
        var successorsOf = activities.ToDictionary(a => a.ActivityId, _ => new List<CpmRelationInput>());
        foreach (var relation in relations)
        {
            predecessorsOf[relation.SuccessorActivityId].Add(relation);
            successorsOf[relation.PredecessorActivityId].Add(relation);
        }

        var topoOrder = TopologicalSort(activities, successorsOf, predecessorsOf);

        // --- Forward pass: ES/EF (cpm-method.md step 2). No predecessor => ES = project start (0). ---
        var es = new Dictionary<Guid, int>(topoOrder.Count);
        var ef = new Dictionary<Guid, int>(topoOrder.Count);
        foreach (var id in topoOrder)
        {
            var duration = durationByActivity[id];
            var earlyStart = 0;
            foreach (var relation in predecessorsOf[id])
            {
                var predecessorId = relation.PredecessorActivityId;
                var constraint = RelationConstraints.ForwardConstraint(
                    es[predecessorId], ef[predecessorId], relation.RelationType, relation.LagDays, duration);
                earlyStart = Math.Max(earlyStart, constraint);
            }

            es[id] = earlyStart;
            ef[id] = earlyStart + duration;
        }

        var projectDuration = ef.Values.Max();

        // --- Backward pass: LS/LF (cpm-method.md step 3). No successor => LF = project finish. ---
        var lf = new Dictionary<Guid, int>(topoOrder.Count);
        var ls = new Dictionary<Guid, int>(topoOrder.Count);
        for (var i = topoOrder.Count - 1; i >= 0; i--)
        {
            var id = topoOrder[i];
            var duration = durationByActivity[id];
            var lateFinish = projectDuration;
            foreach (var relation in successorsOf[id])
            {
                var successorId = relation.SuccessorActivityId;
                var constraint = RelationConstraints.BackwardConstraint(
                    ls[successorId], lf[successorId], relation.RelationType, relation.LagDays, duration);
                lateFinish = Math.Min(lateFinish, constraint);
            }

            lf[id] = lateFinish;
            ls[id] = lateFinish - duration;
        }

        // --- Total Float: TF_i = LS_i - ES_i (cpm-method.md step 4). ---
        var totalFloat = topoOrder.ToDictionary(id => id, id => ls[id] - es[id]);

        // --- Free Float: generalises "FF_i = min(ES_succ) - EF_i (FS relations)" (cpm-method.md
        // step 4) to all four relation types via RelationConstraints.ForwardConstraint's own
        // per-type formula: for each successor edge, the slack is "how much this edge's forward
        // constraint (computed from i's *actual* ES/EF) could still grow before it would exceed
        // the successor's *actual* ES" - which collapses to the classic ES_s - EF_i formula for
        // FS(lag 0) and is the natural, mechanically-derived generalisation for SS/FF/SF (not
        // itself spelled out per-type in cpm-method.md - documented as a modelling decision in the
        // Sprint 5 report; verified to reproduce the canonical fixture's FF_A=0/FF_B=3/FF_C=0/
        // FF_D=0 exactly). An activity with no successors has nothing to be constrained by, so its
        // free float equals its total float.
        var freeFloat = new Dictionary<Guid, int>(topoOrder.Count);
        foreach (var id in topoOrder)
        {
            var succs = successorsOf[id];
            if (succs.Count == 0)
            {
                freeFloat[id] = totalFloat[id];
                continue;
            }

            var minSlack = int.MaxValue;
            foreach (var relation in succs)
            {
                var successorId = relation.SuccessorActivityId;
                var constraint = RelationConstraints.ForwardConstraint(
                    es[id], ef[id], relation.RelationType, relation.LagDays, durationByActivity[successorId]);
                minSlack = Math.Min(minSlack, es[successorId] - constraint);
            }

            freeFloat[id] = minSlack;
        }

        var results = topoOrder
            .Select(id => new CpmActivityResult(
                id, es[id], ef[id], ls[id], lf[id], totalFloat[id], freeFloat[id], IsCritical: totalFloat[id] == 0))
            .ToList();

        var criticalPath = topoOrder.Where(id => totalFloat[id] == 0).ToList();

        return Result<CpmCalculationResult>.Success(new CpmCalculationResult(results, projectDuration, criticalPath));
    }

    /// <summary>
    /// Kahn's algorithm. <see cref="GraphValidator.Validate"/> already proved the graph acyclic, so
    /// this always terminates having visited every activity exactly once (an isolated activity -
    /// S5-BE-03's "no relations at all" DoD - starts with in-degree 0 like any other root and is
    /// included without any special-casing).
    /// </summary>
    private static List<Guid> TopologicalSort(
        IReadOnlyCollection<CpmActivityInput> activities,
        Dictionary<Guid, List<CpmRelationInput>> successorsOf,
        Dictionary<Guid, List<CpmRelationInput>> predecessorsOf)
    {
        var remainingInDegree = activities.ToDictionary(a => a.ActivityId, a => predecessorsOf[a.ActivityId].Count);
        var queue = new Queue<Guid>(activities.Where(a => remainingInDegree[a.ActivityId] == 0).Select(a => a.ActivityId));
        var order = new List<Guid>(activities.Count);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            order.Add(id);

            foreach (var relation in successorsOf[id])
            {
                var successorId = relation.SuccessorActivityId;
                if (--remainingInDegree[successorId] == 0)
                {
                    queue.Enqueue(successorId);
                }
            }
        }

        return order;
    }
}
