using CMPlus.Application.Features.Weather;
using CMPlus.Application.Features.Weather.Commands.RecordWeatherLog;
using CMPlus.Application.Services.Cpm;
using CMPlus.Application.Services.Eot;
using CMPlus.Application.Tests.Features.Weather;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Eot;

/// <summary>
/// S11-QA-01: independent verification of docs/specs/weather-eot/domain-rules.md. Every expected
/// number in this file is re-derived by hand from the domain document's own formulas (§3-§6),
/// documented inline, and cross-checked against the real <see cref="EotEvaluator"/> - never copied
/// from <c>EotEvaluatorTests</c> or from the fixture table's own worked answers. Where this file's
/// scenarios differ from the domain document's numbered fixtures (different day-counts, a brand new
/// network for the T1/T2 contrast, a genuinely new scenario for §5.3's cap), that is deliberate: the
/// point is to prove the *rule*, not merely reproduce the *fixture instance* backend-developer's own
/// suite already covers.
///
/// <para><b>Section C contains one escalated, deliberately-skipped finding</b> - a reproducible
/// violation of §5.3's own stated cap invariant, discovered independently while re-deriving these
/// numbers. Per docs/10. §10 / this task's brief: a fixture (or an invariant) that disagrees with a
/// computed value is escalated to <c>domain-expert</c>, never quietly edited to match the code. The
/// test is committed in a <see langword="Skip"/>ped, loudly-commented state rather than deleted or
/// softened, so the gap is discoverable and cannot silently regress into a false green.</para>
/// </summary>
public class QaIndependentVerificationTests
{
    // ============================================================================================
    // Section A - §3.6 Notice. Zero prior test coverage anywhere in the codebase (confirmed by
    // grep across tests/ before writing this) despite being fully implemented. Every number below
    // is computed straight from §3.6's formula:
    //   t_notice = max{d : d countable} + NoticePeriodDays CALENDAR days
    //   NoticeWindowExpired = (EvaluatedAt.Date > t_notice)
    // ============================================================================================

    [Fact]
    public void Notice_LatestNoticeDate_Is_The_Max_Countable_Date_Plus_NoticePeriodDays_Calendar_Days()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        // Single countable stoppage on 2026-07-08 (Wed, working day, matches W-01's own date so the
        // countability side of this fixture is already independently proven correct elsewhere).
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        // Thai standard-form contract's own 15-day figure, cited in domain-rules.md §2.2/§8.1 itself
        // (not an arbitrary test constant) - NoticePeriodDays is CALENDAR days per §3.6's own words.
        var policy = EotPolicySettings.Default with { NoticePeriodDays = 15 };
        var input = EotFixtures.BuildInput([entry], run, policy: policy, evaluatedAt: EotFixtures.D(2026, 7, 20));

        var outcome = EotEvaluator.Evaluate(input).Value;

        // 2026-07-08 + 15 calendar days = 2026-07-23 (8 + 15 = 23, no month rollover - hand-checked,
        // not run through any date library before asserting).
        Assert.Equal(new DateOnly(2026, 7, 23), outcome.LatestNoticeDate);
        // EvaluatedAt (07-20) is before the notice deadline (07-23) - not yet expired.
        Assert.False(outcome.NoticeWindowExpired);
        // §3.6 is explicit this has ZERO arithmetic effect on E - re-confirms W-01's own answer (1)
        // is unchanged by turning Notice on.
        Assert.Equal(1, outcome.EotEligibleDays);
    }

    [Fact]
    public void Notice_Is_Not_Yet_Expired_Exactly_On_The_Boundary_Date_The_Comparison_Is_Strictly_Greater_Than()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var policy = EotPolicySettings.Default with { NoticePeriodDays = 15 };
        // EvaluatedAt is exactly the notice deadline itself (2026-07-23) - the boundary the code's
        // ">" (not ">=") comparison decides. Nothing in domain-rules.md's prose pins this edge
        // explicitly; verified here directly against the implementation's own literal formula.
        var input = EotFixtures.BuildInput([entry], run, policy: policy, evaluatedAt: EotFixtures.D(2026, 7, 23));

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(new DateOnly(2026, 7, 23), outcome.LatestNoticeDate);
        Assert.False(outcome.NoticeWindowExpired); // same-day is NOT expired - "today > t_notice", not ">=".
    }

    [Fact]
    public void Notice_Is_Expired_The_Calendar_Day_After_The_Boundary()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var policy = EotPolicySettings.Default with { NoticePeriodDays = 15 };
        var input = EotFixtures.BuildInput([entry], run, policy: policy, evaluatedAt: EotFixtures.D(2026, 7, 24));

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.True(outcome.NoticeWindowExpired);
    }

    [Fact]
    public void Notice_Fields_Are_Omitted_Entirely_Not_Zeroed_When_NoticePeriodDays_Is_Not_Configured()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        // EotPolicySettings.Default has NoticePeriodDays = null - the documented "not configured" state.
        var input = EotFixtures.BuildInput([entry], run, evaluatedAt: EotFixtures.D(2026, 12, 31));

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Null(outcome.LatestNoticeDate);
        Assert.Null(outcome.NoticeWindowExpired); // null, not false - "omitted", per §3.6's own words.
    }

    [Fact]
    public void Notice_Fields_Are_Omitted_When_There_Is_No_Countable_Day_Even_If_NoticePeriodDays_Is_Configured()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        // No entries at all - W-16a's vacuous case, replayed with Notice configured to prove the
        // "no countable date to measure from" guard actually fires (nothing in EotEvaluatorTests
        // exercises Notice at all, let alone this combination).
        var policy = EotPolicySettings.Default with { NoticePeriodDays = 15 };
        var input = EotFixtures.BuildInput([], run, policy: policy);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Null(outcome.LatestNoticeDate);
        Assert.Null(outcome.NoticeWindowExpired);
    }

    [Fact]
    public void Notice_Uses_The_Latest_COUNTABLE_Date_Never_A_Later_Excluded_Entrys_Date()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        // 07-08 (Wed) is countable. 07-12 (Sun, non-working, no exception) is LATER but excluded -
        // if the implementation naively used "the latest entry date" instead of "the latest
        // COUNTABLE date" it would wrongly anchor notice on the 12th.
        var countable = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var excludedLater = EotFixtures.Entry(EotFixtures.D(2026, 7, 12), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var policy = EotPolicySettings.Default with { NoticePeriodDays = 15 };
        var input = EotFixtures.BuildInput([countable, excludedLater], run, policy: policy);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(new DateOnly(2026, 7, 23), outcome.LatestNoticeDate); // anchored on the 8th, not the 12th.
    }

    // ============================================================================================
    // Section B - independent re-derivation of §6.1's float-exhaustion rule (W-03's rule, generalized
    // to day-counts W-03 itself does not use: 4 and 6, instead of W-03's 5). Hand-derived CPM by
    // walking N1's forward/backward pass myself (shown in each comment) before ever running the code.
    // ============================================================================================

    [Fact]
    public void FloatExhaustion_Four_Stoppage_Days_Against_Float_Three_Yields_Exactly_One_Eot_Day()
    {
        // Hand derivation (impacted network, Dur_B = 3+4 = 7):
        //   EF_A=5; EF_B=5+7=12; EF_C=5+6=11; ES_D=max(12,11)=12; EF_D=16 -> D_imp=16, E=16-15=1.
        // Backward pass confirms the swap direction: LS_D=12, LS_B=12-7=5 -> TF_B=5-5=0 (critical);
        // LS_C=12-6=6 -> TF_C=6-5=1 (float ROSE from 0 to 1, not 2 as in W-03's 5-day case - the
        // rise is exactly S_B-TF_B(orig) = 4-3 = 1 in both places, which is the cross-check).
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[] { 8, 9, 10, 11 } // four working days (Wed/Thu/Fri/Sat under TH-6Day).
            .Select(day => EotFixtures.Entry(EotFixtures.D(2026, 7, day), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.BId))
            .ToList();
        var input = EotFixtures.BuildInput(entries, run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(1, outcome.EotEligibleDays);
        Assert.Equal(16, outcome.ImpactedDurationDays);
        var driver = Assert.Single(outcome.Drivers);
        Assert.Equal(4, driver.StoppageDays);
        Assert.Equal(3, driver.TotalFloatAtRun);
        Assert.Equal(1, driver.IndicativeEotDays); // max(0, 4-3) = 1, matches the network figure (single-activity closed form).
        Assert.True(driver.IsOnImpactedCriticalPath); // float fully exhausted plus one day over -> now critical.
        Assert.Equal(0, driver.RemainingFloatAfter);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void FloatExhaustion_Six_Stoppage_Days_Against_Float_Three_Yields_Exactly_Three_Eot_Days()
    {
        // Hand derivation (Dur_B = 3+6 = 9): EF_B=5+9=14; EF_C=11; ES_D=max(14,11)=14; EF_D=18 ->
        // D_imp=18, E=18-15=3 = max(0,6-3), matching the closed form exactly (only B affected).
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[] { 6, 7, 8, 9, 10, 13 } // six working days, deliberately NOT the same set W-03/W-07 use.
            .Select(day => EotFixtures.Entry(EotFixtures.D(2026, 7, day), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.BId))
            .ToList();
        var input = EotFixtures.BuildInput(entries, run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(3, outcome.EotEligibleDays);
        Assert.Equal(18, outcome.ImpactedDurationDays);
        var driver = Assert.Single(outcome.Drivers);
        Assert.Equal(6, driver.StoppageDays);
        Assert.Equal(3, driver.IndicativeEotDays);
        Assert.Equal(0, driver.RemainingFloatAfter);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void FloatExhaustion_Exactly_At_The_Float_Boundary_Three_Days_Against_Float_Three_Yields_Zero()
    {
        // The complementary boundary W-02 (1 day) and W-03 (5 days) do not pin: S_j == TF_j exactly.
        // Hand check: Dur_B=3+3=6; EF_B=5+6=11; EF_C=11; ES_D=max(11,11)=11; EF_D=15 -> D_imp=15,
        // unchanged from D_as=15, E=0. Per §6.1 "as long as S_j <= TF_j the stoppage is absorbed".
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[] { 8, 9, 10 }
            .Select(day => EotFixtures.Entry(EotFixtures.D(2026, 7, day), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.BId))
            .ToList();
        var input = EotFixtures.BuildInput(entries, run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(0, outcome.EotEligibleDays);
        Assert.Equal(15, outcome.ImpactedDurationDays); // D_imp == D_as exactly at the boundary.
        var driver = Assert.Single(outcome.Drivers);
        Assert.Equal(0, driver.IndicativeEotDays);
        Assert.Equal(0, driver.RemainingFloatAfter); // float fully consumed, none left over.
        // CORRECTED after this test first failed against my own initial (wrong) hand derivation: the
        // backward pass at this exact boundary is LF_D=15,LS_D=11; LS_B=11-6=5,TF_B=5-5=0. B's
        // impacted length (6) exactly TIES C's original length (6, already critical), so B joins the
        // critical path too (a second, parallel critical path A-B-D alongside A-C-D) even though the
        // PROJECT duration does not move (E=0). Re-verified by hand a second time before fixing this
        // assertion - never adjusted to match the code without re-deriving independently first.
        Assert.True(driver.IsOnImpactedCriticalPath);
        EotFixtures.AssertCap(outcome);
    }

    // ============================================================================================
    // Section C - independent re-derivation of §5.3's double-count cap (W-07's rule), PLUS an
    // escalated finding discovered while doing so.
    // ============================================================================================

    [Fact]
    public void Cap_Three_Parallel_Activity_Days_A_Different_Daycount_Than_W07_Stays_Within_The_Cap()
    {
        // Independent re-confirmation of W-07's rule at a different day-count (3, not 5), so the cap
        // holding is not an artifact specific to exactly five days. Hand derivation:
        // Dur_B=3+3=6, Dur_C=6+3=9. EF_B=5+6=11, EF_C=5+9=14. ES_D=max(11,14)=14. EF_D=18. D_imp=18.
        // E=18-15=3. DistinctCountableDateCount=3 (same 3 dates for both) -> tight cap again.
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[] { 8, 9, 10 }
            .Select(day => EotFixtures.Entry(EotFixtures.D(2026, 7, day), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.BId, EotFixtures.CId))
            .ToList();
        var input = EotFixtures.BuildInput(entries, run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(3, outcome.EotEligibleDays);
        Assert.Equal(3, outcome.DistinctCountableDateCount);
        EotFixtures.AssertCap(outcome);

        var driverB = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.BId);
        var driverC = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.CId);
        Assert.Equal(0, driverB.IndicativeEotDays); // exactly consumes B's float (3 stoppage days == 3 float), no excess.
        Assert.Equal(3, driverC.IndicativeEotDays); // C was already critical - all 3 days are indicative EOT.
        // Unlike W-07 (7 > 5), the naive per-activity sum here (0+3=3) does NOT exceed E - a
        // different, still-valid data point: the guard does not depend on the naive sum overshooting.
        Assert.Equal(outcome.EotEligibleDays, driverB.IndicativeEotDays + driverC.IndicativeEotDays);

        // §5.2a re-confirmation (added 2026-08-10, backend-developer): B and C are on PARALLEL branches
        // here too, so the serial-chain collapse must not engage - re-verified explicitly, not assumed,
        // per this task's own instruction that "no other computed result moves".
        Assert.Equal(0, driverB.SerialChainAbsorbedDays);
        Assert.Equal(0, driverC.SerialChainAbsorbedDays);
        Assert.Equal(0, outcome.SerialChainAbsorbedDayCount);
    }

    /// <summary>
    /// <b>RESOLVED 2026-08-10</b> - domain-expert ruling on this file's own escalated S11-QA-01
    /// finding (docs/specs/weather-eot/domain-rules.md §5.2a, new fixture <b>W-17</b>, which this test
    /// reproduces exactly). The reproduced defect was real (naively summing each activity's charge
    /// independently gives $E=4$ against 2 countable dates - a genuine breach of §5.3's cap), but the
    /// ruling is explicit that the fix is <b>in the summation, not the gate</b>: §3.7 gains no
    /// network-topology test, and an entry naming an activity together with its own predecessor stays
    /// valid, countable input (a storm really can stop both) - what changes is that, per date, only one
    /// activity per CPM path may be charged into the network (the serial-chain collapse,
    /// <see cref="SerialChainCollapse"/>); the other's charge is <i>absorbed</i>, never excluded.
    ///
    /// <para>Re-pointed at W-17's <b>full</b> expected set, not merely the cap assertion - the original
    /// escalation's own instruction, because <b>the cap alone is necessary but not sufficient</b>: W-20
    /// (domain-rules.md) shows a wrong answer (0, from keeping the upstream activity instead of the
    /// least-float one) satisfies the cap just as comfortably as the right answer (2) does. Every value
    /// below is re-derived independently against the domain document's own §5.2a worked example, not
    /// copied from <c>EotEvaluatorTests.W17_...</c>.</para>
    /// </summary>
    [Fact]
    public void Cap_Chain_Predecessor_And_Successor_Charged_The_Same_Two_Dates_Collapses_To_W17s_Full_Expected_Set()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var day1 = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.AId, EotFixtures.CId);
        var day2 = EotFixtures.Entry(EotFixtures.D(2026, 7, 9), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.AId, EotFixtures.CId);
        var input = EotFixtures.BuildInput([day1, day2], run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        // Hand derivation (N1: A(5)->B(3)->D(4), A(5)->C(6)->D(4)). A precedes C (FS), so A^F reaches
        // C^S: comparable. TF_A(R1)=TF_C(R1)=0 (both critical) - the float key ties, so the topological
        // tie-break keeps the UPSTREAM activity: K_d = {A} on both dates, C's charge absorbed both times.
        // Impacted: Dur_A=5+2=7 (C unchanged at 6, fully absorbed). EF_A=7; EF_B=7+3=10; EF_C=7+6=13;
        // ES_D=max(10,13)=13; EF_D=17 -> D_imp=17, E=17-15=2.
        Assert.Equal(2, outcome.EotEligibleDays);
        Assert.Equal(2, outcome.DistinctCountableDateCount);
        EotFixtures.AssertCap(outcome); // now TIGHT (2<=2) - contrast W-20, where the cap alone cannot catch a wrong answer of 0.

        // §3.7's ruling: no ExclusionReason anywhere for this shape - both dates, for both activities,
        // are valid countable input; both source rows are present and unexcluded.
        Assert.Equal(2, outcome.Sources.Count);
        Assert.All(outcome.Sources, s => Assert.Null(s.ExclusionReason));

        // Nothing is hidden (§5.2a) - BOTH A and C keep a driver row, even though C's charged
        // StoppageDays is zero.
        var driverA = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.AId);
        var driverC = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.CId);

        Assert.Equal(2, driverA.StoppageDays);
        Assert.Equal(0, driverA.SerialChainAbsorbedDays);
        Assert.Null(driverA.AbsorbedIntoActivityCodes);
        Assert.Equal(2, driverA.IndicativeEotDays); // max(0, 2-0).
        Assert.Equal(2, driverA.MarginalEotDays);

        // The negative case the original escalation's naive-sum defect (E=4) came from: C is NOT
        // independently charged 2 days on top of A's 2 - it is charged ZERO, and the 2 recorded days
        // are disclosed as absorbed into A instead (never silently dropped, never an ExclusionReason).
        Assert.Equal(0, driverC.StoppageDays);
        Assert.Equal(2, driverC.SerialChainAbsorbedDays);
        Assert.Equal("A", driverC.AbsorbedIntoActivityCodes);
        Assert.Equal(0, driverC.IndicativeEotDays); // max(0, 0-0) - a fully-absorbed activity reads 0, not a phantom 2.
        Assert.Equal(0, driverC.MarginalEotDays); // removing a charge that was never applied changes nothing.

        Assert.Equal(2, outcome.CountableStoppageDayCount); // what the schedule was charged (both days, on A).
        Assert.Equal(2, outcome.SerialChainAbsorbedDayCount); // what the evidence additionally recorded (both days, on C).

        // Negative assertion - the exact defect this escalation reproduced must not recur: summing A's
        // and C's charges independently gives 4, which breaches the §5.3 cap. 2 is the only answer that
        // is both internally consistent (network-computed) and within the physical bound.
        Assert.NotEqual(4, outcome.EotEligibleDays);
    }

    // ============================================================================================
    // Section D - independent re-derivation of §4's contemporaneous ruling (W-11b's rule), on a
    // brand-new network (P/Q/R/S, not N1/N1') so this does not merely replay backend-developer's own
    // fixture data through the same code path with relabelled variable names.
    // ============================================================================================

    private static readonly Guid PId = Guid.NewGuid();
    private static readonly Guid QId = Guid.NewGuid();
    private static readonly Guid RId = Guid.NewGuid();
    private static readonly Guid SId = Guid.NewGuid();

    // Network NX1: P(10)->R(2)->S(3); P(10)->Q(6)->S(3).
    // Forward: EF_P=10; EF_R=12; EF_Q=16; ES_S=max(12,16)=16; EF_S=19 -> D_as=19.
    // Backward: LS_S=16; LS_Q=16-6=10 -> TF_Q=0 (critical); LS_R=16-2=14 -> TF_R=4 (float 4).
    private static readonly IReadOnlyList<CpmRelationInput> NxRelations =
    [
        new(PId, RId, RelationType.FS, 0),
        new(PId, QId, RelationType.FS, 0),
        new(RId, SId, RelationType.FS, 0),
        new(QId, SId, RelationType.FS, 0),
    ];

    private static Dictionary<Guid, EotActivityContext> NxActivityContexts()
    {
        var start = EotFixtures.D(2026, 6, 15);
        return new Dictionary<Guid, EotActivityContext>
        {
            [PId] = new(PId, "P", "Piling", start, start, null),
            [QId] = new(QId, "Q", "Foundation", start, start, null),
            [RId] = new(RId, "R", "Retaining Wall", start, start, null),
            [SId] = new(SId, "S", "Superstructure", start, start, null),
        };
    }

    [Fact]
    public void Contemporaneous_Ruling_Independent_Network_Governing_Run_Absorbs_The_Stoppage_Via_Float()
    {
        // RunX1 (CalculatedAt 2026-07-01): P(10)->R(2)->S(3); P(10)->Q(6)->S(3). D_as=19, TF_R=4.
        var runX1 = EotFixtures.BuildRun(
            EotFixtures.D(2026, 7, 1), [(PId, 10), (QId, 6), (RId, 2), (SId, 3)], NxRelations);
        Assert.Equal(19, runX1.ProjectDurationDays); // sanity check my own hand CPM before using it.

        // One stoppage on R, 2026-07-10 (Fri, a working day under TH-6Day, no exception nearby).
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 10), WeatherImpact.FullStoppage, 8.00m, null, RId);
        var governingRunsByDate = new Dictionary<DateOnly, CpmRun> { [entry.LogDate] = runX1 };
        var input = EotFixtures.BuildInput(
            [entry], runX1, activities: NxActivityContexts(), governingRunsByDate: governingRunsByDate, earliestRunFallback: runX1);

        var outcome = EotEvaluator.Evaluate(input).Value;

        // Hand check: Dur_R=2+1=3. EF_R=10+3=13 (still < EF_Q=16, so ES_S unchanged at 16). D_imp=19.
        // E=19-19=0 - R's float of 4 easily absorbs 1 stoppage day (T2, the ruled-correct reading).
        Assert.Equal(0, outcome.EotEligibleDays);
        Assert.Equal(EotCriticalityBasis.Contemporaneous, outcome.CriticalityBasis);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void Contemporaneous_Cross_Check_The_Later_Reschedule_Would_Have_Wrongly_Charged_One_Day()
    {
        // RunX2 (CalculatedAt 2026-07-20, AFTER the 07-10 stoppage - must NOT govern it under T2):
        // P(10)->R(7)->S(3); P(10)->Q(6)->S(3) - R's duration grew 2->7, swapping the critical path.
        // Forward: EF_P=10; EF_R=17; EF_Q=16; ES_S=max(17,16)=17; EF_S=20 -> D_as=20.
        // Backward: LS_S=17; LS_R=17-7=10 -> TF_R=0 (critical now); LS_Q=17-6=11 -> TF_Q=1 (float now).
        var runX2 = EotFixtures.BuildRun(
            EotFixtures.D(2026, 7, 20), [(PId, 10), (QId, 6), (RId, 7), (SId, 3)], NxRelations);
        Assert.Equal(20, runX2.ProjectDurationDays);

        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 10), WeatherImpact.FullStoppage, 8.00m, null, RId);
        // Deliberately wiring the LATER run as governing - the T1 (wrong) reading, kept only as a
        // contrast, exactly like EotEvaluatorTests' own W11b_Cross_Check does for N1/N1'. Never how
        // EvaluateEotCommandHandler actually resolves a governing run in production (§4.1).
        var governingRunsByDate = new Dictionary<DateOnly, CpmRun> { [entry.LogDate] = runX2 };
        var input = EotFixtures.BuildInput(
            [entry], runX2, activities: NxActivityContexts(), governingRunsByDate: governingRunsByDate, earliestRunFallback: runX2);

        var outcome = EotEvaluator.Evaluate(input).Value;

        // Hand check: Dur_R=7+1=8. EF_R=10+8=18. EF_Q=16. ES_S=max(18,16)=18. EF_S=21 -> D_imp=21.
        // E=21-20=1 (R is critical under RunX2 with TF=0, so 1 stoppage day = 1 EOT day).
        Assert.Equal(1, outcome.EotEligibleDays); // T1, the WRONG reading - contrast only.

        // Same weather, same activity: 0 days (T2, ruled correct, above) vs 1 day (T1, here) -
        // independently reproduces W-11b's own point ("same weather ... entitlement 1 day or 0 days
        // depending purely on which schedule you ask") on a network W-11b never uses.
    }

    // ============================================================================================
    // Section E - W-16f/g (cross-project / cross-tenant activity attribution). Investigative: proves
    // what the code ACTUALLY does, which is coarser than the fixture's literal text describes.
    // ============================================================================================

    /// <summary>
    /// domain-rules.md W-16f literally specifies <c>422 ActivityNotInProject</c> for an activity that
    /// exists in the caller's own tenant but a DIFFERENT project. The actual implementation has no
    /// such code at all - <see cref="RecordWeatherLogCommandHandler"/> only ever returns the single,
    /// generic <see cref="WeatherLogErrorCodes.UnknownActivity"/> (400) once an id fails to resolve,
    /// with no distinction between "wrong project, same tenant" and "wrong tenant entirely" (both
    /// collapse to the same repository call, <c>FindExistingActivityIdsAsync</c>, scoped by
    /// <c>ProjectId</c> alone - see that method's own implementation). This is a genuine, reproducible
    /// gap against the fixture's literal text - flagged as a finding, not asserted as a hard failure,
    /// because the ADR-0002 tenant-isolation SECURITY property (never confirm existence outside your
    /// own scope) is still satisfied, just less granularly than the fixture describes.
    /// </summary>
    [Fact]
    public async Task W16f_An_Activity_In_Another_Project_Resolves_To_The_Same_Generic_UnknownActivity_Code_Not_A_Distinct_422()
    {
        var repository = new FakeDailyWeatherLogRepository { ProjectExists = true, ExistingActivityIds = [] };
        var otherProjectActivityId = Guid.NewGuid(); // exists in the DB, but NOT in this project - the
                                                       // fake models this the same way the real EF query
                                                       // does: simply absent from ExistingActivityIds.
        var handler = new RecordWeatherLogCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()), new FakeClock(DateTimeOffset.UtcNow));

        var command = new RecordWeatherLogCommand(
            Guid.NewGuid(), DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"), WeatherCondition.HeavyRain, null,
            42.5m, WeatherImpact.FullStoppage, null, 8.00m, [otherProjectActivityId]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        // ACTUAL behaviour: the same generic code the "different tenant" case also produces (see the
        // next test) - not the fixture's distinct 422 ActivityNotInProject.
        Assert.Equal(WeatherLogErrorCodes.UnknownActivity, result.Error);
    }

    /// <summary>Mirrors the previous test for W-16g's "different tenant entirely" case - proves it
    /// is, today, INDISTINGUISHABLE at the Application layer from W-16f's "different project, same
    /// tenant" case (same assertion, same error code) - which happens to still satisfy ADR-0002's
    /// actual security requirement ("must not even confirm existence") even more conservatively than
    /// the fixture's proposed 404 would, since there is no signal at all differentiating the two
    /// causes. Flagged so nobody mistakes the identical codes for a bug in this test rather than a
    /// documented gap versus the fixture text.</summary>
    [Fact]
    public async Task W16g_An_Activity_In_Another_Tenant_Resolves_To_The_Same_Generic_Code_Not_A_Distinct_404()
    {
        var repository = new FakeDailyWeatherLogRepository { ProjectExists = true, ExistingActivityIds = [] };
        var otherTenantActivityId = Guid.NewGuid();
        var handler = new RecordWeatherLogCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()), new FakeClock(DateTimeOffset.UtcNow));

        var command = new RecordWeatherLogCommand(
            Guid.NewGuid(), DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"), WeatherCondition.HeavyRain, null,
            42.5m, WeatherImpact.FullStoppage, null, 8.00m, [otherTenantActivityId]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WeatherLogErrorCodes.UnknownActivity, result.Error); // same code as W16f above - see that test's remarks.
    }

    // ============================================================================================
    // Section F - one of the three areas backend-developer explicitly flagged for independent
    // scrutiny: "EotEvaluationDriver correlates via CpmRunId" (one row per (governing run, activity)
    // pair, not one row per activity). W-15's own test (EotEvaluatorTests) asserts two Runs but never
    // once inspects outcome.Drivers - so the actual row-shape claim was, until now, completely
    // unverified anywhere in the suite. Reuses W-15's own N1/N1' data deliberately (this section
    // verifies a STRUCTURAL property - row count/shape - of an already-independently-verified
    // scenario, not the E arithmetic again).
    // ============================================================================================

    [Fact]
    public void Driver_Rows_Correlate_By_CpmRunId_One_Row_Per_Governing_Run_When_The_Same_Activity_Spans_Two_Runs()
    {
        var r1 = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var r2 = EotFixtures.BuildN1PrimeRun(EotFixtures.D(2026, 7, 15));
        var e1 = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var e2 = EotFixtures.Entry(EotFixtures.D(2026, 7, 20), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var governingRunsByDate = new Dictionary<DateOnly, CpmRun> { [e1.LogDate] = r1, [e2.LogDate] = r2 };
        var input = EotFixtures.BuildInput([e1, e2], r1, governingRunsByDate: governingRunsByDate, earliestRunFallback: r1);

        var outcome = EotEvaluator.Evaluate(input).Value;

        // The load-bearing structural assertion nothing else in the suite makes: TWO driver rows for
        // the SAME activity (C), not one merged/overwritten row - because C was charged under two
        // different governing runs with two different float readings.
        var cDrivers = outcome.Drivers.Where(d => d.ActivityId == EotFixtures.CId).ToList();
        Assert.Equal(2, cDrivers.Count);
        Assert.Contains(cDrivers, d => d.CpmRunId == r1.Id && d.TotalFloatAtRun == 0); // TF_C@R1 = 0, per §10.0's own N1 table.
        Assert.Contains(cDrivers, d => d.CpmRunId == r2.Id && d.TotalFloatAtRun == 1); // TF_C@R2 = 1, per W-11b's own N1' derivation.
        // The two rows are genuinely distinct entity instances (an EotEvaluationDriverInput each),
        // not the same row's CpmRunId merely re-read twice.
        Assert.NotEqual(cDrivers[0], cDrivers[1]);
    }

    // ============================================================================================
    // Section G - the second flagged area: the Mixed confidence path "has no numbered fixture".
    // Existing coverage (EotEvaluatorTests.Confidence_Is_Mixed_...) only asserts the CriticalityBasis/
    // Confidence flags, using the SAME CpmRun object for both the direct-hit and the fallback path -
    // which never actually exercises a fallback run with genuinely different characteristics. This
    // uses two DIFFERENT runs (N1 and N1') and asserts the real computed E, hand-derived below.
    // ============================================================================================

    [Fact]
    public void Mixed_Confidence_With_Two_Genuinely_Different_Runs_Produces_The_Hand_Derived_Combined_Total()
    {
        // NOTE: the first version of this test used an orphan date of 2026-05-20, which failed with
        // CriticalityBasis=Contemporaneous instead of the expected Mixed - not a product bug, MY setup
        // error: DefaultActivityContexts() sets every activity's ActualStart to 2026-06-15, so a
        // 05-20 stoppage fails §3.7 (ActivityNotYetScheduled) for every named activity, chargedAny
        // stays false, and the date never reaches distinctCountableDates at all - so it silently never
        // participates in the Confidence classification either. Fixed by moving the orphan date to
        // exactly the activity's own ActualStart (06-15, a Monday, inclusive per §3.7's ">=") and the
        // earliest run's CalculatedAt to the day after it (06-16) - re-verified against the running
        // code, not just re-guessed, before trusting the corrected version below.
        var earliestRun = EotFixtures.BuildN1Run(EotFixtures.D(2026, 6, 16)); // the ONLY run at or before the orphan date.
        var laterRun = EotFixtures.BuildN1PrimeRun(EotFixtures.D(2026, 7, 15)); // direct hit for the other date.

        // Orphan date: 2026-06-15 (Monday, working day; exactly ActualStart, passes §3.7's inclusive
        // ">="), strictly BEFORE earliestRun's own CalculatedAt (06-16) - genuinely "no run at or
        // before this date", not merely "omitted from the dictionary out of laziness". Falls back to
        // earliestRun (N1, TF_C=0). Hand check (same arithmetic as W-01): Dur_C=6+1=7, EF_C=12,
        // EF_D=16, D_as=15 -> delta=1.
        var orphanEntry = EotFixtures.Entry(EotFixtures.D(2026, 6, 15), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);

        // Direct-hit date: 2026-07-20, on/after laterRun's CalculatedAt (07-15) - governed by N1'
        // (TF_C=1). Hand check: Dur_C=6+1=7, EF_C=5+7=12, EF_B=12 (N1's B=7, unaffected), ES_D=
        // max(12,12)=12, EF_D=16, D_as(N1')=16 -> delta=16-16=0 (the 1 stoppage day is exactly
        // absorbed by C's float of 1 under N1', per §6.1).
        var directHitEntry = EotFixtures.Entry(EotFixtures.D(2026, 7, 20), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);

        var governingRunsByDate = new Dictionary<DateOnly, CpmRun> { [directHitEntry.LogDate] = laterRun };
        var input = EotFixtures.BuildInput(
            [orphanEntry, directHitEntry], laterRun,
            windowStart: EotFixtures.D(2026, 6, 1), windowEnd: EotFixtures.D(2026, 7, 31),
            governingRunsByDate: governingRunsByDate, earliestRunFallback: earliestRun);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(EotCriticalityBasis.Mixed, outcome.CriticalityBasis);
        Assert.Equal(EotConfidence.Provisional, outcome.Confidence);
        // E = 1 (earliestRun's window, the orphan) + 0 (laterRun's window, fully absorbed) = 1 - the
        // number nothing in the existing suite computes for a genuinely Mixed evaluation.
        Assert.Equal(1, outcome.EotEligibleDays);
        Assert.Equal(2, outcome.Runs.Count);
        var earliestRunOutcome = Assert.Single(outcome.Runs, r => r.CpmRunId == earliestRun.Id);
        Assert.Equal(1, earliestRunOutcome.DeltaDays);
        var laterRunOutcome = Assert.Single(outcome.Runs, r => r.CpmRunId == laterRun.Id);
        Assert.Equal(0, laterRunOutcome.DeltaDays);
        EotFixtures.AssertCap(outcome);
    }
}
