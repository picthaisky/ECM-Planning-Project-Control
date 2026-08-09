using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Cpm;

/// <summary>
/// S5-QA-01: the independent test matrix called for on top of <see cref="CpmEngineTests"/> (backend-
/// developer's own sanity check, per that class's remarks). Every fixture below is derived from
/// first principles against <see cref="RelationConstraints"/>'s own per-type forward/backward
/// formulas - not copied from <see cref="CpmEngineTests"/> or backend-developer's Sprint 5 report -
/// specifically to close two gaps found while verifying S5-BE-01..03:
///
/// <list type="bullet">
/// <item><description><b>SF was the one relation type with zero dedicated unit-test coverage.</b>
/// <see cref="CpmEngineTests"/> covers the canonical fixture (all FS), one SS-lag-2 case and one
/// FF-lag-1 case, but no fixture isolates SF - the DoD ("แยก test ต่อชนิด relation (ไม่ใช่แค่ FS)")
/// requires every relation type to have its own independently-verifiable case.</description></item>
/// <item><description>A second, independently-authored cross-check of the SS-lag-2/FF-lag-1
/// fixtures backend-developer constructed (flagged in the Sprint 5 handoff as "not sourced from an
/// authoritative reference"): hand re-derivation from <see cref="RelationConstraints"/> alone
/// (see the QA report) confirmed both of backend-developer's fixtures are arithmetically correct;
/// this file additionally re-exercises the same two formulas on a *different* edge/lag combination
/// (rather than re-asserting identical numbers) so a coincidental sign error in one specific edge
/// position could not hide behind both suites agreeing on the same single case.</description></item>
/// </list>
/// </summary>
public class CpmEngineQaIndependentTests
{
    private static (int Es, int Ef, int Ls, int Lf, int Tf, int Ff, bool IsCritical) Tuple(CpmActivityResult r) =>
        (r.EarlyStart, r.EarlyFinish, r.LateStart, r.LateFinish, r.TotalFloat, r.FreeFloat, r.IsCritical);

    [Fact]
    public void CPM_EDGE_SF_LAG_Backend_Constructed_Fixture_SF_Relation_With_Lag_1_QA_Derived()
    {
        // Same A/B/C/D network shape as CPM-NETWORK-1 (cpm-method.md's own canonical fixture), with
        // only B->D changed from FS(lag 0) to SF(lag 1); A->B, A->C, C->D stay FS(lag 0) - the same
        // "change exactly one edge" methodology CpmEngineTests used for SS-lag-2/FF-lag-1, applied
        // here independently for SF (cpm-method.md line 11: "SF: EF_i >= ES_p + L").
        //
        // Hand derivation (RelationConstraints.ForwardConstraint/BackwardConstraint's own SF cases):
        //   ES_A=0,EF_A=5 (unchanged - A has no predecessor).
        //   ES_B = EF_A+0 = 5 (A->B still FS(0)) -> EF_B = 5+3 = 8.
        //   ES_C = EF_A+0 = 5 -> EF_C = 5+6 = 11.
        //   ES_D: via B (SF lag1) -> ES_B + lag - D_D = 5+1-4 = 2; via C (FS lag0) -> EF_C+0 = 11.
        //     ES_D = max(2,11) = 11 -> EF_D = 15 (same project duration as the canonical fixture).
        //   Backward: LF_D=15 (no successor), LS_D=11.
        //   LF_C (successor D, FS lag0) = LS_D-0=11 -> LS_C=11-6=5 -> TF_C=5-5=0 (critical, unchanged).
        //   LF_B (successor D via SF lag1) = LF_D - lag + D_B = 15-1+3=17, clamped by the project-
        //     duration ceiling (15) since 17 is looser -> LF_B=15 -> LS_B=15-3=12 -> TF_B=12-5=7
        //     (NOT critical - SF is a much weaker link here than the canonical fixture's FS, so B
        //     picks up 7 days of float instead of 3).
        //   LF_A (successors B FS lag0, C FS lag0): LS_B-0=12; LS_C-0=5 -> min=5 -> LS_A=0 ->
        //     TF_A=0 (critical, unchanged).
        //   Free float: FF_D=TF_D=0 (no successor). FF_C=ES_D-(EF_C+0)=11-11=0.
        //   FF_B (via SF lag1): constraint=ES_B+lag-D_D=5+1-4=2 -> FF_B=ES_D-2=11-2=9.
        //   FF_A: via B(FS lag0)=ES_B-(EF_A+0)=5-5=0; via C(FS lag0)=ES_C-5=0 -> FF_A=0.
        //   Critical path unchanged: A -> C -> D, duration 15.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();

        var activities = new[]
        {
            new CpmActivityInput(a, 5), new CpmActivityInput(b, 3), new CpmActivityInput(c, 6), new CpmActivityInput(d, 4),
        };
        var relations = new[]
        {
            new CpmRelationInput(a, b, RelationType.FS, 0),
            new CpmRelationInput(a, c, RelationType.FS, 0),
            new CpmRelationInput(b, d, RelationType.SF, 1),
            new CpmRelationInput(c, d, RelationType.FS, 0),
        };

        var result = CpmEngine.Calculate(activities, relations);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(15, result.Value.ProjectDurationDays);

        var byId = result.Value.Activities.ToDictionary(r => r.ActivityId);
        Assert.Equal((0, 5, 0, 5, 0, 0, true), Tuple(byId[a]));
        Assert.Equal((5, 8, 12, 15, 7, 9, false), Tuple(byId[b]));
        Assert.Equal((5, 11, 5, 11, 0, 0, true), Tuple(byId[c]));
        Assert.Equal((11, 15, 11, 15, 0, 0, true), Tuple(byId[d]));
        Assert.Equal([a, c, d], result.Value.CriticalPath);
    }

    [Fact]
    public void CPM_EDGE_ALL_FOUR_RELATION_TYPES_CHAINED_QA_Derived()
    {
        // Independent Application-layer re-derivation of the same five-task network
        // `backend/tests/fixtures/goldenfiles/xer/all-relation-types.xer` describes (One->Two->
        // Three->Four->Five via FS(lag0)/SS(lag1)/FF(lag2)/SF(lag3) respectively, 8h-day durations
        // 2/1/1/1/1 days) - built directly against CpmActivityInput/CpmRelationInput rather than
        // through the XER parser, so this is a genuinely independent check of the same arithmetic
        // that P6ReconciliationTests (S5-QA-02) exercises end-to-end through the parser.
        //
        //   ES_1=0,EF_1=2 (D=2, no predecessor).
        //   ES_2 (FS lag0 from 1) = EF_1+0=2 -> EF_2=2+1=3.
        //   ES_3 (SS lag1 from 2) = ES_2+1=3 -> EF_3=3+1=4.
        //   ES_4 (FF lag2 from 3) = EF_3+2-D_4(1) = 4+2-1=5 -> EF_4=5+1=6.
        //   ES_5 (SF lag3 from 4) = ES_4+3-D_5(1) = 5+3-1=7 -> EF_5=7+1=8.
        //   Project duration = max(2,3,4,6,8) = 8.
        //   Because this is a single straight chain (no branching at all), the backward pass must
        //   retrace the forward pass exactly regardless of which relation types/lags are used - so
        //   every activity has TF=FF=0 (all critical), which is itself a useful invariant check
        //   independent of the per-type formulas: LS_5=7,LF_5=8; LF_4=LS_5-3+D_4... (SF backward,
        //   see class remarks)=... resolves to LS_4=5=ES_4; LF_3=LS_4-2(FF backward)=4=EF_3, so
        //   LS_3=3=ES_3; LF_2=LS_3-1+D_2(1)(SS backward)=3=EF_2, so LS_2=2=ES_2; LF_1=LS_2-0(FS
        //   backward)=2=EF_1, so LS_1=0=ES_1. TF_i=LS_i-ES_i=0 for all five.
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        var three = Guid.NewGuid();
        var four = Guid.NewGuid();
        var five = Guid.NewGuid();

        var activities = new[]
        {
            new CpmActivityInput(one, 2), new CpmActivityInput(two, 1), new CpmActivityInput(three, 1),
            new CpmActivityInput(four, 1), new CpmActivityInput(five, 1),
        };
        var relations = new[]
        {
            new CpmRelationInput(one, two, RelationType.FS, 0),
            new CpmRelationInput(two, three, RelationType.SS, 1),
            new CpmRelationInput(three, four, RelationType.FF, 2),
            new CpmRelationInput(four, five, RelationType.SF, 3),
        };

        var result = CpmEngine.Calculate(activities, relations);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(8, result.Value.ProjectDurationDays);

        var byId = result.Value.Activities.ToDictionary(r => r.ActivityId);
        Assert.Equal((0, 2, 0, 2, 0, 0, true), Tuple(byId[one]));
        Assert.Equal((2, 3, 2, 3, 0, 0, true), Tuple(byId[two]));
        Assert.Equal((3, 4, 3, 4, 0, 0, true), Tuple(byId[three]));
        Assert.Equal((5, 6, 5, 6, 0, 0, true), Tuple(byId[four]));
        Assert.Equal((7, 8, 7, 8, 0, 0, true), Tuple(byId[five]));
        Assert.Equal([one, two, three, four, five], result.Value.CriticalPath);
    }

    [Fact]
    public void Negative_Lag_Lead_Is_Also_Supported_For_A_Non_FS_Relation_Type_SS()
    {
        // CpmEngineTests already covers negative lag for FS; ActivityRelation.LagDays is documented
        // as "signed - negative = lead" with no relation-type restriction (Domain/Entities/
        // ActivityRelation.cs), so the same arithmetic must hold for SS too. Places the SS(-2) edge
        // in the *middle* of a three-activity chain (A->X FS(0), X->B SS(-2)) rather than straight
        // off the project start, so the lead genuinely pulls B's ES below X's ES without tripping
        // the "no predecessor => ES floor of 0" clamp CpmEngine.Calculate applies to root activities
        // (a distinct behaviour worth its own note, exercised separately below).
        //   ES_A=0,EF_A=5. ES_X=EF_A+0=5,EF_X=5+2=7.
        //   ES_B (SS lag -2 from X) = ES_X + (-2) = 3 -> EF_B = 3+3 = 6.
        //   Project duration = max(5,7,6) = 7.
        //   Backward: LF_B=7 (no succ), LS_B=7-3=4.
        //   LF_X (successor B via SS lag -2) = LS_B - (-2) + D_X(2) = 4+2+2 = 8, clamped to the
        //     project-duration ceiling 7 -> LF_X=7 -> LS_X=7-2=5 -> TF_X=5-5=0 (critical).
        //   LF_A (successor X FS lag0) = LS_X-0=5 -> LF_A=5 -> LS_A=0 -> TF_A=0 (critical).
        //   TF_B = LS_B - ES_B = 4-3 = 1 (not critical - B does not determine the 7-day project
        //     duration; X->EF_X=7 does).
        var a = Guid.NewGuid();
        var x = Guid.NewGuid();
        var b = Guid.NewGuid();

        var result = CpmEngine.Calculate(
            [new CpmActivityInput(a, 5), new CpmActivityInput(x, 2), new CpmActivityInput(b, 3)],
            [
                new CpmRelationInput(a, x, RelationType.FS, 0),
                new CpmRelationInput(x, b, RelationType.SS, -2),
            ]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(7, result.Value.ProjectDurationDays);

        var byId = result.Value.Activities.ToDictionary(r => r.ActivityId);
        Assert.Equal((0, 5, 0, 5, 0, 0, true), Tuple(byId[a]));
        Assert.Equal((5, 7, 5, 7, 0, 0, true), Tuple(byId[x]));
        Assert.Equal((3, 6, 4, 7, 1, 1, false), Tuple(byId[b]));
    }

    [Fact]
    public void A_Negative_Lag_Large_Enough_To_Precede_Project_Start_Is_Clamped_To_Day_Zero_Not_A_Negative_Date()
    {
        // A root activity (no predecessor) always starts at ES=0 by construction
        // (CpmEngine.Calculate: "var earlyStart = 0;" then Math.Max across predecessor constraints).
        // A successor whose *only* forward constraint resolves to a negative number (a lead deep
        // enough to want to start before project day 0) must still floor at 0, not go negative -
        // Math.Max(0, constraint) is exactly this floor. A(5)->B(3) FS lag -10 would want
        // ES_B = EF_A + (-10) = 5-10 = -5; the floor keeps it at 0.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var result = CpmEngine.Calculate(
            [new CpmActivityInput(a, 5), new CpmActivityInput(b, 3)],
            [new CpmRelationInput(a, b, RelationType.FS, -10)]);

        Assert.True(result.IsSuccess);
        var byId = result.Value.Activities.ToDictionary(r => r.ActivityId);
        Assert.Equal(0, byId[b].EarlyStart); // floored at project start, not -5
        Assert.Equal(3, byId[b].EarlyFinish);
    }

    [Fact]
    public void CPM_EDGE_DUPLICATE_RELATION_Same_Pair_Rejected_Even_When_The_Relation_Type_Differs()
    {
        // GraphValidatorTests (backend-developer) only exercises the same-type-twice case (FS + FS).
        // GraphValidator.Validate's own dedupe key is (PredecessorActivityId, SuccessorActivityId)
        // alone - RelationType/LagDays are not part of the key - so an FS and an SS relation between
        // the identical ordered pair must be rejected too, not silently accepted because the "second"
        // relation looks superficially different. Worth its own explicit case since it is not
        // obvious from reading the single same-type test that type is excluded from the dedupe key.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var result = GraphValidator.Validate(
            [new CpmActivityInput(a, 5), new CpmActivityInput(b, 3)],
            [
                new CpmRelationInput(a, b, RelationType.FS, 0),
                new CpmRelationInput(a, b, RelationType.SS, 2),
            ]);

        Assert.False(result.IsValid);
        Assert.Equal(CpmValidationErrorCodes.DuplicateRelation, result.ErrorCode);
    }
}
