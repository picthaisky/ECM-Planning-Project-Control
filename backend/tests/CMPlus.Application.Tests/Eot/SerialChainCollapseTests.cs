using CMPlus.Application.Services.Cpm;
using CMPlus.Application.Services.Eot;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Eot;

/// <summary>
/// S11-BE-02 (domain-expert ruling 2026-08-10, domain-rules.md weather-eot §5.2a): isolated unit tests
/// of <see cref="SerialChainCollapse.Collapse"/> itself - the comparability test (reachability in the
/// start/finish graph) and the antichain reduction (least-float-first, ties upstream-first) - decoupled
/// from <see cref="EotEvaluator"/>'s own network re-computation, so a defect in the collapse algorithm
/// itself is pinned at its own unit rather than only ever visible through a full network result.
/// <c>EotEvaluatorTests</c>'s W-17/W-19/W-20 prove the same rules end to end, including the impacted-
/// network arithmetic they drive; this file proves the antichain/reachability logic alone, with
/// hand-built graphs and no <see cref="CpmEngine"/> call anywhere.
/// </summary>
public class SerialChainCollapseTests
{
    private static readonly DateOnly Day1 = new(2026, 7, 8);
    private static readonly DateOnly Day2 = new(2026, 7, 9);

    private static Dictionary<DateOnly, Dictionary<Guid, decimal>> WeightsByDate(
        params (DateOnly Date, Guid ActivityId, decimal Weight)[] charges)
    {
        var result = new Dictionary<DateOnly, Dictionary<Guid, decimal>>();
        foreach (var (date, activityId, weight) in charges)
        {
            if (!result.TryGetValue(date, out var byActivity))
            {
                byActivity = [];
                result[date] = byActivity;
            }

            byActivity[activityId] = weight;
        }

        return result;
    }

    // ================================================================================================
    // N1-shaped topology (A->B->D, A->C->D, all FS/0) - matches domain-rules.md §10's own network so
    // the float/topo-index values below are directly comparable to the fixture table (TF_A=0, TF_B=3,
    // TF_C=0, TF_D=0; topological order A,B,C,D).
    // ================================================================================================

    private static readonly Guid AId = Guid.NewGuid();
    private static readonly Guid BId = Guid.NewGuid();
    private static readonly Guid CId = Guid.NewGuid();
    private static readonly Guid DId = Guid.NewGuid();

    private static readonly IReadOnlyList<CpmRelationInput> N1Relations =
    [
        new(AId, BId, RelationType.FS, 0),
        new(AId, CId, RelationType.FS, 0),
        new(BId, DId, RelationType.FS, 0),
        new(CId, DId, RelationType.FS, 0),
    ];

    private static readonly HashSet<Guid> N1ActivityIds = [AId, BId, CId, DId];
    private static readonly Dictionary<Guid, int> N1TotalFloat = new() { [AId] = 0, [BId] = 3, [CId] = 0, [DId] = 0 };
    private static readonly Dictionary<Guid, int> N1TopoIndex = new() { [AId] = 0, [BId] = 1, [CId] = 2, [DId] = 3 };
    private static readonly Dictionary<Guid, string> N1Code = new() { [AId] = "A", [BId] = "B", [CId] = "C", [DId] = "D" };

    [Fact]
    public void Two_Activities_On_Parallel_Branches_Are_Incomparable_And_Both_Kept()
    {
        // B and C share predecessor A but neither reaches the other (W-07's own shape, and §5.2a's own
        // worked example of why this is NOT collapsed).
        var weightsByDate = WeightsByDate((Day1, BId, 1m), (Day1, CId, 1m));

        var result = SerialChainCollapse.Collapse(weightsByDate, N1ActivityIds, N1Relations, N1TotalFloat, N1TopoIndex, N1Code);

        Assert.Equal(1m, Assert.Single(result.KeptWeightsByActivity[BId]));
        Assert.Equal(1m, Assert.Single(result.KeptWeightsByActivity[CId]));
        Assert.Empty(result.AbsorbedWeightsByActivity);
        Assert.Empty(result.AbsorbedIntoActivityIds);
    }

    [Fact]
    public void A_Predecessor_And_Successor_On_The_Same_Date_Collapse_To_The_Upstream_Activity_On_A_Float_Tie()
    {
        // A -> C, TF_A = TF_C = 0 (tie) -> "ties go upstream-first" -> keep A, absorb C (W-17's shape).
        var weightsByDate = WeightsByDate((Day1, AId, 1m), (Day1, CId, 1m));

        var result = SerialChainCollapse.Collapse(weightsByDate, N1ActivityIds, N1Relations, N1TotalFloat, N1TopoIndex, N1Code);

        Assert.Equal(1m, Assert.Single(result.KeptWeightsByActivity[AId]));
        Assert.False(result.KeptWeightsByActivity.ContainsKey(CId));
        Assert.Equal(1m, Assert.Single(result.AbsorbedWeightsByActivity[CId]));
        Assert.Equal(new[] { AId }, result.AbsorbedIntoActivityIds[CId]);
    }

    [Fact]
    public void Each_Date_Is_Decided_Independently_An_Activity_Can_Be_Kept_On_One_Date_And_Absorbed_On_Another()
    {
        // Day1: A and C both charged -> C absorbed into A (as above). Day2: C charged ALONE -> kept -
        // proof the reduction is genuinely per-date, never "once absorbed, always absorbed".
        var weightsByDate = WeightsByDate((Day1, AId, 1m), (Day1, CId, 1m), (Day2, CId, 1m));

        var result = SerialChainCollapse.Collapse(weightsByDate, N1ActivityIds, N1Relations, N1TotalFloat, N1TopoIndex, N1Code);

        Assert.Equal(1m, Assert.Single(result.KeptWeightsByActivity[AId])); // Day1 only.
        Assert.Equal(1m, Assert.Single(result.KeptWeightsByActivity[CId])); // Day2 only - C DOES have a kept weight.
        Assert.Equal(1m, Assert.Single(result.AbsorbedWeightsByActivity[CId])); // Day1's charge, separately absorbed.
        Assert.Equal(new[] { AId }, result.AbsorbedIntoActivityIds[CId]);
    }

    [Fact]
    public void An_Activity_Outside_The_Runs_Own_Network_Is_Silently_Dropped_Never_Compared()
    {
        // Defensive case (never exercised by any domain-rules.md fixture): a weight recorded for an
        // activity id this run does not know about must not blow up the comparability test or leak into
        // either output dictionary - mirrors EotEvaluator's own pre-existing "runActivityIds.Contains" guard.
        var strangerId = Guid.NewGuid();
        var weightsByDate = WeightsByDate((Day1, CId, 1m), (Day1, strangerId, 1m));

        var result = SerialChainCollapse.Collapse(weightsByDate, N1ActivityIds, N1Relations, N1TotalFloat, N1TopoIndex, N1Code);

        Assert.Equal(1m, Assert.Single(result.KeptWeightsByActivity[CId]));
        Assert.False(result.KeptWeightsByActivity.ContainsKey(strangerId));
        Assert.False(result.AbsorbedWeightsByActivity.ContainsKey(strangerId));
    }

    [Fact]
    public void A_Three_Activity_Chain_Charged_On_The_Same_Date_Keeps_Only_The_Most_Upstream_Member_When_Float_Ties()
    {
        // A -> C -> D (all FS/0), all three charged the same date, ALL tied at float 0 (A, C and D are
        // all on N1's own critical path) - the topological tie-break alone decides, keeping the most
        // upstream of the three (A); C and D are both absorbed into A (each is directly comparable with
        // A, which is the only member ever added to K_d) - proof the reduction generalises past pairs to
        // a whole chain, and that a later candidate is compared against what is ALREADY kept, not
        // pairwise across the whole candidate set.
        var weightsByDate = WeightsByDate((Day1, AId, 1m), (Day1, CId, 1m), (Day1, DId, 1m));

        var result = SerialChainCollapse.Collapse(weightsByDate, N1ActivityIds, N1Relations, N1TotalFloat, N1TopoIndex, N1Code);

        var kept = Assert.Single(result.KeptWeightsByActivity);
        Assert.Equal(AId, kept.Key);
        Assert.Equal(new[] { 1m }, kept.Value);

        Assert.Equal(new[] { AId }, result.AbsorbedIntoActivityIds[CId]);
        Assert.Equal(new[] { AId }, result.AbsorbedIntoActivityIds[DId]);
        Assert.Equal(1m, Assert.Single(result.AbsorbedWeightsByActivity[CId]));
        Assert.Equal(1m, Assert.Single(result.AbsorbedWeightsByActivity[DId]));
    }

    // ================================================================================================
    // N2-shaped topology (W-19's own network): P --SS--> Q, P --FS--> R, Q --FS--> R. All TF = 0.
    // ================================================================================================

    private static readonly Guid PId = Guid.NewGuid();
    private static readonly Guid QId = Guid.NewGuid();
    private static readonly Guid RId = Guid.NewGuid();

    private static readonly IReadOnlyList<CpmRelationInput> N2Relations =
    [
        new(PId, QId, RelationType.SS, 0),
        new(PId, RId, RelationType.FS, 0),
        new(QId, RId, RelationType.FS, 0),
    ];

    private static readonly HashSet<Guid> N2ActivityIds = [PId, QId, RId];
    private static readonly Dictionary<Guid, int> N2TotalFloat = new() { [PId] = 0, [QId] = 0, [RId] = 0 };
    private static readonly Dictionary<Guid, int> N2TopoIndex = new() { [PId] = 0, [QId] = 1, [RId] = 2 };
    private static readonly Dictionary<Guid, string> N2Code = new() { [PId] = "P", [QId] = "Q", [RId] = "R" };

    [Fact]
    public void An_SS_Linked_Pair_Is_Incomparable_Even_Though_A_Naive_Predecessor_Test_Would_Collapse_Them()
    {
        var weightsByDate = WeightsByDate((Day1, PId, 1m), (Day1, QId, 1m));

        var result = SerialChainCollapse.Collapse(weightsByDate, N2ActivityIds, N2Relations, N2TotalFloat, N2TopoIndex, N2Code);

        // Both kept - an SS edge only reaches the successor's Start from the predecessor's OWN Start,
        // never from the predecessor's Finish, so P^F cannot reach Q^S (and R has no edge back to Q^S
        // either): genuinely incomparable, exactly as domain-rules.md §5.2a's own worked example states.
        Assert.Equal(1m, Assert.Single(result.KeptWeightsByActivity[PId]));
        Assert.Equal(1m, Assert.Single(result.KeptWeightsByActivity[QId]));
        Assert.Empty(result.AbsorbedWeightsByActivity);

        // Negative assertion - the naive implementation this fixture exists to fail: a plain "is $v$ a
        // transitive successor of $u$" test sees P -> Q (via the SS edge) and collapses to {P} alone,
        // i.e. exactly one kept activity instead of two.
        Assert.Equal(2, result.KeptWeightsByActivity.Count);
    }

    [Fact]
    public void An_FF_Linked_Pair_Is_Incomparable_When_It_Is_Their_Only_Connection()
    {
        // P --FF--> Q (only), both charged the same date - an FF edge puts P^F within reach of Q^F, but
        // never of Q^S, so P cannot reach Q's START either: incomparable, both charges stand. Covers the
        // one relation type no domain-rules.md fixture exercises directly (FS: W-17/W-20; SS: W-19/above).
        var ffRelations = new List<CpmRelationInput> { new(PId, QId, RelationType.FF, 0) };
        var activityIds = new HashSet<Guid> { PId, QId };
        var totalFloat = new Dictionary<Guid, int> { [PId] = 0, [QId] = 0 };
        var topoIndex = new Dictionary<Guid, int> { [PId] = 0, [QId] = 1 };
        var codes = new Dictionary<Guid, string> { [PId] = "P", [QId] = "Q" };
        var weightsByDate = WeightsByDate((Day1, PId, 1m), (Day1, QId, 1m));

        var result = SerialChainCollapse.Collapse(weightsByDate, activityIds, ffRelations, totalFloat, topoIndex, codes);

        Assert.Equal(1m, Assert.Single(result.KeptWeightsByActivity[PId]));
        Assert.Equal(1m, Assert.Single(result.KeptWeightsByActivity[QId]));
        Assert.Empty(result.AbsorbedWeightsByActivity);
    }

    // ================================================================================================
    // N3-shaped topology (W-20's own network): A --FS--> C, X --FS--> C, C --FS--> D. TF_A=4, others 0.
    // ================================================================================================

    private static readonly Guid N3AId = Guid.NewGuid();
    private static readonly Guid N3XId = Guid.NewGuid();
    private static readonly Guid N3CId = Guid.NewGuid();
    private static readonly Guid N3DId = Guid.NewGuid();

    private static readonly IReadOnlyList<CpmRelationInput> N3Relations =
    [
        new(N3AId, N3CId, RelationType.FS, 0),
        new(N3XId, N3CId, RelationType.FS, 0),
        new(N3CId, N3DId, RelationType.FS, 0),
    ];

    private static readonly HashSet<Guid> N3ActivityIds = [N3AId, N3XId, N3CId, N3DId];
    private static readonly Dictionary<Guid, int> N3TotalFloat = new() { [N3AId] = 4, [N3XId] = 0, [N3CId] = 0, [N3DId] = 0 };
    private static readonly Dictionary<Guid, int> N3TopoIndex = new() { [N3AId] = 0, [N3XId] = 1, [N3CId] = 2, [N3DId] = 3 };
    private static readonly Dictionary<Guid, string> N3Code = new() { [N3AId] = "A", [N3XId] = "X", [N3CId] = "C", [N3DId] = "D" };

    [Fact]
    public void Least_Float_Wins_Over_Upstream_Position_When_The_Two_Rules_Disagree()
    {
        // A (float 4, upstream/predecessor) and C (float 0, downstream/successor) both charged - least
        // float (C) must be kept, NOT the upstream one (A), even though A comes first topologically.
        var weightsByDate = WeightsByDate((Day1, N3AId, 1m), (Day1, N3CId, 1m));

        var result = SerialChainCollapse.Collapse(weightsByDate, N3ActivityIds, N3Relations, N3TotalFloat, N3TopoIndex, N3Code);

        Assert.Equal(1m, Assert.Single(result.KeptWeightsByActivity[N3CId]));
        Assert.False(result.KeptWeightsByActivity.ContainsKey(N3AId));
        Assert.Equal(1m, Assert.Single(result.AbsorbedWeightsByActivity[N3AId]));
        Assert.Equal(new[] { N3CId }, result.AbsorbedIntoActivityIds[N3AId]);

        // Negative assertion - THE point of the fixture: a "keep the predecessor" rule (sorting by
        // topological position BEFORE float) would keep A and absorb C instead.
        Assert.NotEqual(N3AId, result.KeptWeightsByActivity.Keys.Single());
    }

    [Fact]
    public void An_Uncharged_Activity_Never_Appears_In_Either_Output()
    {
        // X and D are part of N3's network but never named in any weather entry - they must not appear
        // in KeptWeightsByActivity, AbsorbedWeightsByActivity or AbsorbedIntoActivityIds at all.
        var weightsByDate = WeightsByDate((Day1, N3AId, 1m), (Day1, N3CId, 1m));

        var result = SerialChainCollapse.Collapse(weightsByDate, N3ActivityIds, N3Relations, N3TotalFloat, N3TopoIndex, N3Code);

        Assert.False(result.KeptWeightsByActivity.ContainsKey(N3XId));
        Assert.False(result.KeptWeightsByActivity.ContainsKey(N3DId));
        Assert.False(result.AbsorbedWeightsByActivity.ContainsKey(N3XId));
        Assert.False(result.AbsorbedWeightsByActivity.ContainsKey(N3DId));
    }
}
