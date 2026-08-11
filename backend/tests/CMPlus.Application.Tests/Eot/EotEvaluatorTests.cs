using CMPlus.Application.Services.Cpm;
using CMPlus.Application.Services.Eot;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Eot;

/// <summary>
/// S11-BE-02 (backend-developer's own sanity check, per CLAUDE.md's "implement the worked examples
/// as your own sanity check before handing to QA" - S11-QA-01 builds the fuller, independent suite
/// from the same fixture table, domain-rules.md weather-eot §10/§13). Every <c>Wxx</c> test name
/// below matches the fixture id it reproduces exactly, so a diff against the domain document is
/// mechanical. <see cref="EotFixtures.AssertCap"/> (the §5.3 double-count guard) is asserted on every
/// fixture, per the domain document's own instruction that it is "cheap and ... the single most
/// effective guard here" - not just trusted.
///
/// <para>W-03, W-07, W-10 and W-11b are the four fixtures <c>domain-expert</c> starred; W-10's
/// correction-chain mechanics (405/409/422 responses, the <c>AuditLog</c> invariant) are already
/// covered end to end by S11-BE-01's own suite (<c>DailyWeatherLogTests</c>,
/// <c>CMPlus.Integration.Tests/Weather</c>) - this file's <c>EotEffectiveLogSetTests</c> sibling
/// covers the half specific to this task (does a stale/corrected evaluation compute the right new
/// number), and <c>CMPlus.Integration.Tests.Eot</c> covers the full write-then-evaluate walk end to
/// end against a real (InMemory) database.</para>
/// </summary>
public class EotEvaluatorTests
{
    [Fact]
    public void W01_Critical_Activity_One_Full_Stoppage_Day_Yields_One_Eot_Day()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, 61.00m, EotFixtures.CId);
        var input = EotFixtures.BuildInput([entry], run);

        var result = EotEvaluator.Evaluate(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        var outcome = result.Value;
        Assert.Equal(15, outcome.AsScheduledDurationDays);
        Assert.Equal(16, outcome.ImpactedDurationDays);
        Assert.Equal(1, outcome.EotEligibleDays);
        Assert.Equal(EotCriticalityBasis.Contemporaneous, outcome.CriticalityBasis);
        Assert.Equal(EotConfidence.Substantiated, outcome.Confidence);
        Assert.Equal(1, outcome.DistinctCountableDateCount);
        EotFixtures.AssertCap(outcome);

        var driver = Assert.Single(outcome.Drivers);
        Assert.Equal(EotFixtures.CId, driver.ActivityId);
        Assert.True(driver.WasCriticalAtRun);
        Assert.Equal(1, driver.StoppageDays);
        Assert.Equal(1, driver.IndicativeEotDays);
        Assert.Equal(1, driver.MarginalEotDays);
        Assert.True(driver.IsOnImpactedCriticalPath);
        Assert.Equal(0, driver.RemainingFloatAfter);
    }

    [Fact]
    public void W02_NonCritical_Activity_Within_Float_Yields_Zero_Eot_And_Reports_Float_Consumed()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.BId);
        var input = EotFixtures.BuildInput([entry], run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(15, outcome.ImpactedDurationDays);
        Assert.Equal(0, outcome.EotEligibleDays);
        EotFixtures.AssertCap(outcome);

        var driver = Assert.Single(outcome.Drivers);
        Assert.Equal(EotFixtures.BId, driver.ActivityId);
        Assert.False(driver.WasCriticalAtRun);
        Assert.Equal(0, driver.IndicativeEotDays);
        Assert.Equal(0, driver.MarginalEotDays);
        Assert.False(driver.IsOnImpactedCriticalPath);
        Assert.Equal(2, driver.RemainingFloatAfter); // impacted TF_B = 7 - 5 = 2, per the fixture text.

        // The evaluator never touches Activity at all - EotEvaluator has no repository dependency,
        // so "Activity.TotalFloat for B is still 3" (§2.1) is a structural guarantee here, not merely
        // an assertion; NoSideEffectsTests (Integration) proves it end to end against a real DbContext.
    }

    /// <summary>★ "Build this one first" - the fixture where the naive reading and the correct
    /// reading disagree.</summary>
    [Fact]
    public void W03_Repeated_Stoppages_Exhaust_Float_And_The_Activity_Becomes_Critical()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[] { 8, 9, 10, 11, 13 }
            .Select(day => EotFixtures.Entry(EotFixtures.D(2026, 7, day), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.BId))
            .ToList();
        var input = EotFixtures.BuildInput(entries, run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        // Naive "not critical -> 0" and naive "count every day -> 5" are both wrong; correct is 2.
        Assert.Equal(2, outcome.EotEligibleDays);
        Assert.Equal(17, outcome.ImpactedDurationDays);
        Assert.Equal(15, outcome.AsScheduledDurationDays);
        EotFixtures.AssertCap(outcome);

        var driverB = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.BId);
        Assert.Equal(5, driverB.StoppageDays);
        Assert.Equal(3, driverB.TotalFloatAtRun);
        Assert.False(driverB.WasCriticalAtRun);
        Assert.Equal(2, driverB.IndicativeEotDays); // max(0, 5-3) = 2, matches the network figure exactly (single-activity closed form).
        Assert.Equal(0, driverB.RemainingFloatAfter);
        Assert.True(driverB.IsOnImpactedCriticalPath); // criticality swap: B is now critical in the impacted network.

        // §2.1: no persisted Activity is touched - see W02's identical remark.
    }

    /// <summary>Direct <c>CpmEngine</c> cross-check of domain-rules.md's own worked numbers for
    /// W-03's impacted network - proves the criticality swap (C's float rises 0->2, A/D stay
    /// critical) that a driver-row-only view cannot show for C, which had zero countable days of its
    /// own and therefore never gets a driver row under this evaluator's "charged activities only"
    /// design (see EotEvaluationDriver's remarks).</summary>
    [Fact]
    public void W03_Impacted_Network_Cross_Check_Critical_Path_Moves_From_ACD_To_ABD()
    {
        var calc = CpmEngine.Calculate(
            [new(EotFixtures.AId, 5), new(EotFixtures.BId, 8), new(EotFixtures.CId, 6), new(EotFixtures.DId, 4)],
            EotFixtures.N1Relations);

        Assert.True(calc.IsSuccess);
        var byId = calc.Value.Activities.ToDictionary(a => a.ActivityId);
        Assert.Equal(17, calc.Value.ProjectDurationDays);
        Assert.Equal(0, byId[EotFixtures.AId].TotalFloat);
        Assert.Equal(0, byId[EotFixtures.BId].TotalFloat);
        Assert.True(byId[EotFixtures.BId].IsCritical);
        Assert.Equal(2, byId[EotFixtures.CId].TotalFloat);
        Assert.False(byId[EotFixtures.CId].IsCritical);
        Assert.Equal(0, byId[EotFixtures.DId].TotalFloat);
    }

    [Fact]
    public void W04_NonWorking_Days_And_Holidays_Are_Not_Countable()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entrySat = EotFixtures.Entry(EotFixtures.D(2026, 7, 11), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var entrySun = EotFixtures.Entry(EotFixtures.D(2026, 7, 12), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var entryHoliday = EotFixtures.Entry(EotFixtures.D(2026, 7, 28), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var input = EotFixtures.BuildInput([entrySat, entrySun, entryHoliday], run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(1, outcome.EotEligibleDays); // only the Saturday counts.
        Assert.Equal(3, outcome.Sources.Count); // nothing silently dropped.
        Assert.Contains(outcome.Sources, s => s.DailyWeatherLogId == entrySat.Id && s.ExclusionReason == null);
        Assert.Contains(outcome.Sources, s => s.DailyWeatherLogId == entrySun.Id && s.ExclusionReason == EotExclusionReason.NonWorkingDay);
        Assert.Contains(outcome.Sources, s => s.DailyWeatherLogId == entryHoliday.Id && s.ExclusionReason == EotExclusionReason.CalendarHoliday);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void W04b_An_Added_Working_Day_Exception_Overrides_An_Otherwise_NonWorking_Sunday()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var calendarEntity = new Calendar(EotFixtures.TenantId, EotFixtures.ProjectId, "TH-6Day+Sun", EotFixtures.Th6Day, isDefault: true);
        var exceptions = new List<CalendarException>(EotFixtures.Th6DayExceptions())
        {
            calendarEntity.AddException(EotFixtures.D(2026, 7, 12).DateTime, isWorkingDay: true, "เทคอนกรีตวันอาทิตย์ตามแผน"),
        };

        var entrySat = EotFixtures.Entry(EotFixtures.D(2026, 7, 11), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var entrySun = EotFixtures.Entry(EotFixtures.D(2026, 7, 12), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var entryHoliday = EotFixtures.Entry(EotFixtures.D(2026, 7, 28), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var input = EotFixtures.BuildInput([entrySat, entrySun, entryHoliday], run) with
        {
            Calendar = new EotCalendarContext(EotFixtures.Th6Day, exceptions),
        };

        var outcome = EotEvaluator.Evaluate(input).Value;

        // This is the direction implementations forget - must pass.
        Assert.Equal(2, outcome.EotEligibleDays);
        Assert.Contains(outcome.Sources, s => s.DailyWeatherLogId == entrySun.Id && s.ExclusionReason == null);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void W05_ThresholdWholeDay_Default_Counts_Two_Of_Three_Partial_Days_Inclusive_At_The_Threshold()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[]
        {
            EotFixtures.Entry(EotFixtures.D(2026, 7, 6), WeatherImpact.PartialStoppage, 3.50m, null, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 7), WeatherImpact.PartialStoppage, 4.00m, null, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.PartialStoppage, 6.00m, null, EotFixtures.CId),
        };
        var input = EotFixtures.BuildInput(entries, run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(2, outcome.EotEligibleDays);
        Assert.Equal(17, outcome.ImpactedDurationDays);
        EotFixtures.AssertCap(outcome);
        // Single activity charged - §5.2a's collapse never engages (nothing to compare C against).
        Assert.Equal(0, outcome.SerialChainAbsorbedDayCount);
    }

    [Fact]
    public void W05_FractionalAccrual_Floors_The_Summed_Hours_And_Reports_The_Unclaimed_Remainder()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[]
        {
            EotFixtures.Entry(EotFixtures.D(2026, 7, 6), WeatherImpact.PartialStoppage, 3.50m, null, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 7), WeatherImpact.PartialStoppage, 4.00m, null, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.PartialStoppage, 6.00m, null, EotFixtures.CId),
        };
        var input = EotFixtures.BuildInput(entries, run, policy: EotFixtures.WithPartialDayPolicy(EotPartialDayPolicy.FractionalAccrual));

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(1, outcome.EotEligibleDays); // floor(13.5/8) = 1
        Assert.Equal(16, outcome.ImpactedDurationDays);
        var driver = Assert.Single(outcome.Drivers);
        Assert.Equal(1, driver.StoppageDays);
        Assert.Equal(5.50m, driver.UnclaimedFractionalHours);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void W05_FullDayOnly_Counts_None_Of_The_Three_Partial_Days()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[]
        {
            EotFixtures.Entry(EotFixtures.D(2026, 7, 6), WeatherImpact.PartialStoppage, 3.50m, null, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 7), WeatherImpact.PartialStoppage, 4.00m, null, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.PartialStoppage, 6.00m, null, EotFixtures.CId),
        };
        var input = EotFixtures.BuildInput(entries, run, policy: EotFixtures.WithPartialDayPolicy(EotPartialDayPolicy.FullDayOnly));

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(0, outcome.EotEligibleDays);
        // None of the three days clears FullDayOnly's >= 8.00h gate, so no governing run is ever
        // charged at all - the vacuous "no countable day" case (W-16a), not a network re-run that
        // happens to net to zero. AsScheduled/ImpactedDurationDays both stay 0 accordingly (their sum
        // is only ever taken over runs that were actually charged - see
        // EotEvaluation.AsScheduledDurationDays's own remarks).
        Assert.Equal(0, outcome.AsScheduledDurationDays);
        Assert.Equal(0, outcome.ImpactedDurationDays);
        Assert.Empty(outcome.Runs);
        Assert.All(outcome.Sources, s => Assert.Equal(EotExclusionReason.BelowHoursThreshold, s.ExclusionReason));
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void W06_Rainfall_Threshold_Configured_Excludes_Below_Threshold_And_Unmeasured_Days()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var policy = CMPlus.Application.Services.Eot.EotPolicySettings.Default with { MinRainfallMmForCountableDay = 20.00m };
        var entries = new[]
        {
            EotFixtures.Entry(EotFixtures.D(2026, 7, 6), WeatherImpact.FullStoppage, 8.00m, 18.40m, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 7), WeatherImpact.FullStoppage, 8.00m, 20.00m, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, 42.50m, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 9), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId),
        };
        var input = EotFixtures.BuildInput(entries, run, policy: policy);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(2, outcome.EotEligibleDays);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void W06_No_Rainfall_Threshold_Configured_Counts_All_Four_Days_Including_Unmeasured()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[]
        {
            EotFixtures.Entry(EotFixtures.D(2026, 7, 6), WeatherImpact.FullStoppage, 8.00m, 18.40m, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 7), WeatherImpact.FullStoppage, 8.00m, 20.00m, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, 42.50m, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 9), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId),
        };
        var input = EotFixtures.BuildInput(entries, run); // default policy: MinRainfallMmForCountableDay = NULL.

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(4, outcome.EotEligibleDays);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void W06_CountUnmeasuredRainfall_OptIn_Counts_The_Unmeasured_Day_Too()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var policy = CMPlus.Application.Services.Eot.EotPolicySettings.Default with
        {
            MinRainfallMmForCountableDay = 20.00m,
            CountUnmeasuredRainfallWhenThresholdSet = true,
        };
        var entries = new[]
        {
            EotFixtures.Entry(EotFixtures.D(2026, 7, 6), WeatherImpact.FullStoppage, 8.00m, 18.40m, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 7), WeatherImpact.FullStoppage, 8.00m, 20.00m, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, 42.50m, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 9), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId),
        };
        var input = EotFixtures.BuildInput(entries, run, policy: policy);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(3, outcome.EotEligibleDays);
        EotFixtures.AssertCap(outcome);
    }

    /// <summary>★ The double-count guard - "this is why the cap is a useful guard": tight
    /// (E == DistinctCountableDateCount == 5).</summary>
    [Fact]
    public void W07_Two_Activities_Stopped_On_The_Same_Days_Never_Double_Counts_The_Calendar_Day()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[] { 8, 9, 10, 11, 13 }
            .Select(day => EotFixtures.Entry(
                EotFixtures.D(2026, 7, day), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.BId, EotFixtures.CId))
            .ToList();
        var input = EotFixtures.BuildInput(entries, run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(5, outcome.EotEligibleDays);
        Assert.Equal(5, outcome.DistinctCountableDateCount);
        EotFixtures.AssertCap(outcome); // tight: this is exactly why the cap is a useful guard.

        var driverB = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.BId);
        var driverC = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.CId);
        Assert.Equal(2, driverB.IndicativeEotDays);
        Assert.Equal(5, driverC.IndicativeEotDays);
        // The naive "sum the activities" answer (7) violates the cap - proof the network figure,
        // not the per-activity sum, must govern.
        Assert.Equal(7, driverB.IndicativeEotDays + driverC.IndicativeEotDays);
        Assert.True(driverB.IndicativeEotDays + driverC.IndicativeEotDays > outcome.EotEligibleDays);

        Assert.Equal(0, driverB.MarginalEotDays);
        Assert.Equal(3, driverC.MarginalEotDays);
        Assert.True(Math.Max(driverB.MarginalEotDays, driverC.MarginalEotDays) <= outcome.EotEligibleDays);
        Assert.True(driverB.IndicativeEotDays >= driverB.MarginalEotDays);
        Assert.True(driverC.IndicativeEotDays >= driverC.MarginalEotDays);

        // §5.2a: B and C sit on PARALLEL branches (neither reaches the other) - the collapse does not
        // engage here, "so the two fixtures [W-07 and W-17] cannot be confused" (domain-rules.md's own
        // words). Pinned explicitly, not merely assumed - both activities are charged in full.
        Assert.Equal(0, driverB.SerialChainAbsorbedDays);
        Assert.Null(driverB.AbsorbedIntoActivityCodes);
        Assert.Equal(0, driverC.SerialChainAbsorbedDays);
        Assert.Null(driverC.AbsorbedIntoActivityCodes);
        Assert.Equal(0, outcome.SerialChainAbsorbedDayCount);
    }

    [Fact]
    public void W08a_Stoppage_After_ActualFinish_Is_Excluded_As_ActivityAlreadyComplete()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var activities = EotFixtures.DefaultActivityContexts();
        activities[EotFixtures.AId] = activities[EotFixtures.AId] with { ActualFinish = EotFixtures.D(2026, 7, 3) };
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.AId);
        var input = EotFixtures.BuildInput([entry], run, activities: activities);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(0, outcome.EotEligibleDays);
        Assert.Equal(outcome.AsScheduledDurationDays, outcome.ImpactedDurationDays);
        var source = Assert.Single(outcome.Sources);
        Assert.Equal(EotExclusionReason.ActivityAlreadyComplete, source.ExclusionReason);
        Assert.Empty(outcome.Drivers);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void W08b_Stoppage_Before_The_Activity_Could_Start_Is_Excluded_As_ActivityNotYetScheduled()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var activities = EotFixtures.DefaultActivityContexts();
        activities[EotFixtures.DId] = activities[EotFixtures.DId] with { ActualStart = null, PlannedStart = EotFixtures.D(2026, 7, 20) };
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.DId);
        var input = EotFixtures.BuildInput([entry], run, activities: activities);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(0, outcome.EotEligibleDays);
        var source = Assert.Single(outcome.Sources);
        Assert.Equal(EotExclusionReason.ActivityNotYetScheduled, source.ExclusionReason);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void W09_Unattributed_Stoppage_Contributes_Zero_And_Is_Counted_Not_Distributed()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, 55.00m); // no activity ids at all.
        var input = EotFixtures.BuildInput([entry], run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(0, outcome.EotEligibleDays);
        Assert.Equal(1, outcome.UnattributedStoppageDayCount);
        var source = Assert.Single(outcome.Sources);
        Assert.Equal(EotExclusionReason.NoAffectedActivity, source.ExclusionReason);
        Assert.Empty(outcome.Drivers); // never spread across in-progress activities.
        EotFixtures.AssertCap(outcome);
    }

    /// <summary>§4.4's degraded mode: no run at or before the stoppage date at all - falls back to
    /// the earliest available run, flagged Retrospective/Provisional rather than silently presented
    /// as substantiated. "Assert the two flags, not only the number - a test that checks 1 alone
    /// passes an implementation that has quietly dropped the contemporaneity rule."</summary>
    [Fact]
    public void W11a_No_Run_At_Or_Before_The_Stoppage_Falls_Back_To_The_Earliest_Run_As_Retrospective_Provisional()
    {
        // Only run: R1, CalculatedAt 2026-07-20 - strictly AFTER the 2026-07-08 stoppage below, so
        // there is no direct governing-run hit for that date at all.
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 20));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        // No entry for entry.LogDate in governingRunsByDate - this is precisely what "no direct hit"
        // means; EarliestRunFallback (R1, the only run in the project) is what must be used instead.
        var input = EotFixtures.BuildInput(
            [entry], run, governingRunsByDate: new Dictionary<DateOnly, CpmRun>(), earliestRunFallback: run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        // Same arithmetic as W-01 (R1's network, C stopped one day) - the number alone would pass a
        // broken implementation that silently dropped the contemporaneity rule, hence also asserting
        // the two flags below.
        Assert.Equal(1, outcome.EotEligibleDays);
        Assert.Equal(EotCriticalityBasis.Retrospective, outcome.CriticalityBasis);
        Assert.Equal(EotConfidence.Provisional, outcome.Confidence);
        EotFixtures.AssertCap(outcome);
    }

    /// <summary>★ "This is the fixture that pins §4's ruling" - same weather, same activity, 1 day
    /// under T2 (ruled correct) vs 0 under T1.</summary>
    [Fact]
    public void W11b_Contemporaneous_Ruling_Uses_The_Run_Governing_The_Stoppage_Date()
    {
        var r1 = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var governingRunsByDate = new Dictionary<DateOnly, CpmRun> { [entry.LogDate] = r1 };
        var input = EotFixtures.BuildInput([entry], r1, governingRunsByDate: governingRunsByDate, earliestRunFallback: r1);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(1, outcome.EotEligibleDays); // T2, ruled correct.
        Assert.Equal(EotCriticalityBasis.Contemporaneous, outcome.CriticalityBasis);
        Assert.Equal(EotConfidence.Substantiated, outcome.Confidence);
        var run = Assert.Single(outcome.Runs);
        Assert.Equal(r1.Id, run.CpmRunId);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void W11b_Cross_Check_Using_The_Later_Schedule_As_Governing_Would_Have_Yielded_Zero()
    {
        // Proves T1 and T2 genuinely diverge on identical weather/activity data - the fixture's own
        // point. Never wired into production (EvaluateEotCommandHandler always resolves the
        // contemporaneous run, §4.1) - this only exists to demonstrate what the wrong reading gives.
        var r2 = EotFixtures.BuildN1PrimeRun(EotFixtures.D(2026, 7, 15));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var governingRunsByDate = new Dictionary<DateOnly, CpmRun> { [entry.LogDate] = r2 };
        var input = EotFixtures.BuildInput([entry], r2, governingRunsByDate: governingRunsByDate, earliestRunFallback: r2);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(0, outcome.EotEligibleDays); // T1 - the wrong reading, kept only as a contrast.
    }

    /// <summary>§4.4's middle degraded-mode row - the one W-11a/W-11b/W-15 do not individually cover:
    /// some countable days have a direct governing-run hit, some do not (use the earliest run for the
    /// orphans) - Mixed/Provisional, not Retrospective (all-fallback) and not Contemporaneous
    /// (all-direct-hit).</summary>
    [Fact]
    public void Confidence_Is_Mixed_Provisional_When_Only_Some_Countable_Days_Have_A_Direct_Governing_Run_Hit()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var directHitEntry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var fallbackOnlyEntry = EotFixtures.Entry(EotFixtures.D(2026, 7, 20), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);

        // Only directHitEntry's date resolves a governing run directly; fallbackOnlyEntry's date has
        // no entry at all in the dictionary, so it falls back to EarliestRunFallback (§4.4).
        var governingRunsByDate = new Dictionary<DateOnly, CpmRun> { [directHitEntry.LogDate] = run };
        var input = EotFixtures.BuildInput(
            [directHitEntry, fallbackOnlyEntry], run, governingRunsByDate: governingRunsByDate, earliestRunFallback: run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(EotCriticalityBasis.Mixed, outcome.CriticalityBasis);
        Assert.Equal(EotConfidence.Provisional, outcome.Confidence);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void W15_Two_Governing_Runs_Sum_Their_Own_Windows_Rather_Than_Using_One_Baseline()
    {
        var r1 = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var r2 = EotFixtures.BuildN1PrimeRun(EotFixtures.D(2026, 7, 15));
        var e1 = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var e2 = EotFixtures.Entry(EotFixtures.D(2026, 7, 9), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var e3 = EotFixtures.Entry(EotFixtures.D(2026, 7, 20), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);
        var e4 = EotFixtures.Entry(EotFixtures.D(2026, 7, 21), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId);

        var governingRunsByDate = new Dictionary<DateOnly, CpmRun>
        {
            [e1.LogDate] = r1,
            [e2.LogDate] = r1,
            [e3.LogDate] = r2,
            [e4.LogDate] = r2,
        };
        var input = EotFixtures.BuildInput([e1, e2, e3, e4], r1, governingRunsByDate: governingRunsByDate, earliestRunFallback: r1);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(3, outcome.EotEligibleDays); // 2 (R1's window) + 1 (R2's window), never 4 (T3 over-grants).
        Assert.Equal(EotCriticalityBasis.Contemporaneous, outcome.CriticalityBasis); // every date had a direct hit.
        Assert.Equal(2, outcome.Runs.Count);

        var run1 = Assert.Single(outcome.Runs, r => r.CpmRunId == r1.Id);
        Assert.Equal(2, run1.DeltaDays);
        Assert.Equal(new DateOnly(2026, 7, 8), run1.WindowFrom);
        Assert.Equal(new DateOnly(2026, 7, 9), run1.WindowTo);

        var run2 = Assert.Single(outcome.Runs, r => r.CpmRunId == r2.Id);
        Assert.Equal(1, run2.DeltaDays);
        Assert.Equal(new DateOnly(2026, 7, 20), run2.WindowFrom);
        Assert.Equal(new DateOnly(2026, 7, 21), run2.WindowTo);

        // AsScheduled/ImpactedDurationDays are the reconciling SUM across runs (15+16, 17+17), not a
        // single project-duration figure - see EotEvaluation.AsScheduledDurationDays's own remarks.
        Assert.Equal(31, outcome.AsScheduledDurationDays);
        Assert.Equal(34, outcome.ImpactedDurationDays);
        Assert.Equal(outcome.EotEligibleDays, outcome.ImpactedDurationDays - outcome.AsScheduledDurationDays);
        EotFixtures.AssertCap(outcome);
    }

    /// <summary>★ ADR-0020: absolute counting, "over exceedance and over a per-project switch" -
    /// this evaluator has no <c>CountingBasis</c> configuration point at all, so there is nothing to
    /// select; every countable day is charged. See EotFixtures/ProjectEotPolicy's remarks for why
    /// ExceedanceOverBaseline (the fixture's "2 days" alternative reading) is not implemented.</summary>
    [Fact]
    public void W13_Absolute_Counting_ADR0020_Charges_Every_Genuinely_Lost_Day()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[] { 6, 7, 8, 9, 10 }
            .Select(day => EotFixtures.Entry(EotFixtures.D(2026, 7, day), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId))
            .ToList();
        var input = EotFixtures.BuildInput(entries, run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(5, outcome.EotEligibleDays); // never the 2-day FIDIC-exceedance alternative reading.
        Assert.Equal(20, outcome.ImpactedDurationDays);
        EotFixtures.AssertCap(outcome);
        Assert.Equal(0, outcome.SerialChainAbsorbedDayCount); // single activity charged - nothing to collapse.
    }

    [Fact]
    public void W16a_No_Weather_Entries_At_All_Still_Produces_A_Valid_Substantiated_Zero_Evaluation()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var input = EotFixtures.BuildInput([], run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(0, outcome.EotEligibleDays);
        Assert.Equal(EotConfidence.Substantiated, outcome.Confidence);
        Assert.Equal(EotCriticalityBasis.Contemporaneous, outcome.CriticalityBasis);
        Assert.Empty(outcome.Runs);
        Assert.Empty(outcome.Drivers);
        Assert.Empty(outcome.Sources);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void W16b_Entries_With_No_Impact_Contribute_Zero_With_NoStoppageRecorded_Reason()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.NoImpact, null, null, EotFixtures.CId);
        var input = EotFixtures.BuildInput([entry], run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(0, outcome.EotEligibleDays);
        var source = Assert.Single(outcome.Sources);
        Assert.Equal(EotExclusionReason.NoStoppageRecorded, source.ExclusionReason);
        EotFixtures.AssertCap(outcome);
    }

    [Fact]
    public void W16d_Zero_Activities_In_The_Project_Does_Not_Crash_And_Yields_Zero()
    {
        var emptyRun = CpmRun.Capture(
            EotFixtures.TenantId, EotFixtures.ProjectId, EotFixtures.D(2026, 7, 1), dataDate: null, projectDurationDays: 0,
            EotFixtures.ActorUserId, CpmRunTrigger.Manual, [], []);
        var input = EotFixtures.BuildInput([], emptyRun, activities: new Dictionary<Guid, EotActivityContext>());

        var result = EotEvaluator.Evaluate(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(0, result.Value.EotEligibleDays);
        Assert.Equal(0, result.Value.AsScheduledDurationDays);
    }

    [Fact]
    public void W16e_Entry_Dated_Outside_The_Window_Is_Excluded_And_Not_Counted()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 8, 15), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.CId); // outside [Jul 1, Jul 31].
        var input = EotFixtures.BuildInput([entry], run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(0, outcome.EotEligibleDays);
        Assert.Equal(0, outcome.CountableStoppageDayCount);
        Assert.Empty(outcome.Sources); // pre-filtered before candidate selection - see EotExclusionReason's remarks.
    }

    // ================================================================================================
    // W-17 through W-20 (added 2026-08-10, domain-expert ruling on qa-engineer's S11-QA-01 escalation
    // of the §5.3 cap breach - §5.2a's serial-chain collapse). W-17 is "the escalated fixture ... build
    // this one first" (right after W-07); W-18 pins H1 (the FullDayHours clamp), W-19/W-20 pin H2's two
    // failure directions (collapsing too little vs too much). Every fixture asserts its own stated
    // negative case(s), not merely the cap - per the ruling, the cap alone is necessary but not
    // sufficient (W-20's wrong answer of 0 satisfies it comfortably).
    // ================================================================================================

    /// <summary>★ The escalated fixture - proves §5.3's hypothesis H2. Predecessor A and successor C
    /// (A -&gt; C in N1) are both named on the SAME two dates. Naively charging both independently gives
    /// $E=4$ against 2 countable dates (the reproduced defect); the serial-chain collapse keeps A (float
    /// ties at 0 for both, so the topological tie-break keeps the upstream activity) and absorbs C's
    /// charge - disclosed, never dropped.</summary>
    [Fact]
    public void W17_Predecessor_And_Successor_Charged_The_Same_Days_Collapses_Into_The_Upstream_Activity()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[] { 8, 9 }
            .Select(day => EotFixtures.Entry(EotFixtures.D(2026, 7, day), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.AId, EotFixtures.CId))
            .ToList();
        var input = EotFixtures.BuildInput(entries, run);

        var outcome = EotEvaluator.Evaluate(input).Value;

        // Impacted: Dur_A=5+2=7 (C unchanged - fully absorbed). EF_A=7; EF_B=10; EF_C=7+6=13;
        // ES_D=max(10,13)=13; EF_D=17 -> D_imp=17, E=17-15=2.
        Assert.Equal(2, outcome.EotEligibleDays);
        Assert.Equal(17, outcome.ImpactedDurationDays);
        Assert.Equal(2, outcome.DistinctCountableDateCount);
        EotFixtures.AssertCap(outcome); // tight: 2 <= 2.
        Assert.Equal(2, Assert.Single(outcome.Runs).DeltaDays);

        var driverA = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.AId);
        var driverC = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.CId);

        Assert.Equal(2, driverA.StoppageDays);
        Assert.Equal(0, driverA.SerialChainAbsorbedDays);
        Assert.Null(driverA.AbsorbedIntoActivityCodes);
        Assert.True(driverA.WasCriticalAtRun);
        Assert.True(driverA.IsOnImpactedCriticalPath);
        Assert.Equal(2, driverA.IndicativeEotDays);
        Assert.Equal(2, driverA.MarginalEotDays);
        Assert.Equal(0, driverA.RemainingFloatAfter);

        // C: charged ZERO into the network - not the naive "2" - but the evidence is disclosed, not dropped.
        Assert.Equal(0, driverC.StoppageDays);
        Assert.Equal(2, driverC.SerialChainAbsorbedDays);
        Assert.Equal("A", driverC.AbsorbedIntoActivityCodes);
        Assert.True(driverC.WasCriticalAtRun); // still critical AT THE RUN (TF_C(R1)=0) - unaffected by the collapse.
        Assert.True(driverC.IsOnImpactedCriticalPath); // no criticality swap here (contrast W-03) - A's extension propagates through C.
        Assert.Equal(0, driverC.IndicativeEotDays); // max(0, 0-0) - a fully-absorbed activity reads 0, not a phantom 2.
        Assert.Equal(0, driverC.MarginalEotDays); // removing a charge that was never applied changes nothing.
        Assert.Equal(0, driverC.RemainingFloatAfter);

        Assert.Equal(2, outcome.CountableStoppageDayCount);
        Assert.Equal(2, outcome.SerialChainAbsorbedDayCount);
        Assert.Equal(0, outcome.UnattributedStoppageDayCount);

        // §3.7 stays permissive (the domain document's ruling) - no ExclusionReason anywhere.
        Assert.Equal(2, outcome.Sources.Count);
        Assert.All(outcome.Sources, s => Assert.Null(s.ExclusionReason));

        // --- Negative assertions - what distinguishes a correct implementation. ---
        // (1) Charging both activities independently (the reproduced defect): E=4, which breaches the cap.
        Assert.NotEqual(4, outcome.EotEligibleDays);
        // (2) Rejecting/excluding the entry (destroying evidence a storm genuinely produced) instead of
        // absorbing: no source may carry an exclusion reason for this shape (re-asserted explicitly,
        // this fixture's own stated negative case).
        Assert.DoesNotContain(outcome.Sources, s => s.ExclusionReason != null);
        // (3) The collapse must not silently drop C's row entirely.
        Assert.Contains(outcome.Drivers, d => d.ActivityId == EotFixtures.CId);
    }

    /// <summary>W-18a - one calendar day cannot buy three: without the §3.4 clamp,
    /// <c>FractionalAccrual</c> would turn a single date's <c>HoursLost = 24.00</c> into
    /// $\lfloor 24/8 \rfloor = 3$ duration days, breaching the §5.3 cap ($3 \le 1$ is false). With the
    /// clamp, one calendar day buys exactly one duration day.</summary>
    [Fact]
    public void W18a_FractionalAccrual_Clamps_A_Single_24Hour_Day_To_One_Duration_Day()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 24.00m, null, EotFixtures.CId);
        var input = EotFixtures.BuildInput([entry], run, policy: EotFixtures.WithPartialDayPolicy(EotPartialDayPolicy.FractionalAccrual));

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(1, outcome.EotEligibleDays);
        Assert.Equal(16, outcome.ImpactedDurationDays);
        Assert.Equal(1, outcome.DistinctCountableDateCount);
        EotFixtures.AssertCap(outcome); // tight: 1 <= 1.

        var driver = Assert.Single(outcome.Drivers);
        Assert.Equal(1, driver.StoppageDays);
        Assert.Equal(0.00m, driver.UnclaimedFractionalHours); // 8.00 charged hours, exactly one full day, nothing left over.

        var source = Assert.Single(outcome.Sources);
        Assert.True(source.HoursLostClampedToFullDay);

        // Negative assertion - the un-clamped defect this fixture exists to catch: floor(24/8)=3 would
        // give E=3 against ONE countable date (3<=1 is false). Assert the real answer explicitly.
        Assert.NotEqual(3, outcome.EotEligibleDays);
    }

    /// <summary>W-18b - the clamp is a <c>FractionalAccrual</c>-only concern and perturbs nothing under
    /// the default <c>ThresholdWholeDay</c> policy: the same 24.00h entry produces exactly the same
    /// answer as W-01's 8.00h entry, and <c>HoursLostClampedToFullDay</c> stays <see langword="false"/>
    /// even though <c>HoursLost</c> (24.00) exceeds <c>FullDayHours</c> (8.00).</summary>
    [Fact]
    public void W18b_ThresholdWholeDay_Default_Policy_Is_Unaffected_By_The_Clamp()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entry = EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 24.00m, null, EotFixtures.CId);
        var input = EotFixtures.BuildInput([entry], run); // default policy: ThresholdWholeDay.

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(1, outcome.EotEligibleDays);
        Assert.Equal(16, outcome.ImpactedDurationDays);
        EotFixtures.AssertCap(outcome);

        var driver = Assert.Single(outcome.Drivers);
        Assert.Equal(1, driver.StoppageDays);

        // The point of the fixture: NOT clamped, despite HoursLost(24.00) > FullDayHours(8.00) - the
        // day weight was already 1.00 (a whole day) before the clamp could ever apply.
        var source = Assert.Single(outcome.Sources);
        Assert.False(source.HoursLostClampedToFullDay);
    }

    /// <summary>W-18c - the clamp is applied per date, before summation: 24.00h (clamped to 8.00) plus
    /// 6.00h (unclamped, already below H) sums to 14.00h, floors to exactly 1 day (not 2), and reports
    /// 6.00h unclaimed - never 24.00 + 6.00 = 30.00 summed first and then clamped, which would silently
    /// discard the second day's worth of evidence entirely.</summary>
    [Fact]
    public void W18c_The_Clamp_Applies_Per_Date_Before_Summation_Not_After()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));
        var entries = new[]
        {
            EotFixtures.Entry(EotFixtures.D(2026, 7, 8), WeatherImpact.FullStoppage, 24.00m, null, EotFixtures.CId),
            EotFixtures.Entry(EotFixtures.D(2026, 7, 9), WeatherImpact.FullStoppage, 6.00m, null, EotFixtures.CId),
        };
        var input = EotFixtures.BuildInput(entries, run, policy: EotFixtures.WithPartialDayPolicy(EotPartialDayPolicy.FractionalAccrual));

        var outcome = EotEvaluator.Evaluate(input).Value;

        Assert.Equal(1, outcome.EotEligibleDays);
        Assert.Equal(16, outcome.ImpactedDurationDays);
        Assert.Equal(2, outcome.DistinctCountableDateCount);
        EotFixtures.AssertCap(outcome); // slack: 1 <= 2.

        var driver = Assert.Single(outcome.Drivers);
        Assert.Equal(1, driver.StoppageDays);
        Assert.Equal(6.00m, driver.UnclaimedFractionalHours); // 14.00 charged hours - (1 * 8.00) = 6.00.

        var clampedSource = Assert.Single(outcome.Sources, s => s.CountableDays == 8.00m);
        Assert.True(clampedSource.HoursLostClampedToFullDay); // the 24.00h day.
        var unclampedSource = Assert.Single(outcome.Sources, s => s.CountableDays == 6.00m);
        Assert.False(unclampedSource.HoursLostClampedToFullDay); // the 6.00h day - never exceeded H.
    }

    /// <summary>★ The negative of W-17 - an SS-overlapped pair must NOT be collapsed. A naive "is a
    /// transitive predecessor" test would collapse P into Q's charge here and under-state $E$ by a day
    /// (to 1); the correct reachability-in-the-start/finish-graph test leaves them incomparable, because
    /// an SS edge only ever puts the predecessor's START in reach of the successor's START, never the
    /// predecessor's FINISH - so both activities' durations genuinely never enter the same path's length,
    /// and both charges are real.</summary>
    [Fact]
    public void W19_An_SS_Overlapped_Pair_Is_Not_Collapsed_Both_Charges_Stand()
    {
        var run = EotFixtures.BuildN2Run(EotFixtures.D(2026, 7, 1));
        var activities = EotFixtures.N2ActivityContexts();
        var entries = new[] { 8, 9 }
            .Select(day => EotFixtures.Entry(
                EotFixtures.D(2026, 7, day), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.N2PId, EotFixtures.N2QId))
            .ToList();
        var input = EotFixtures.BuildInput(entries, run, activities: activities);

        var outcome = EotEvaluator.Evaluate(input).Value;

        // Impacted: Dur_P=5+2=7, Dur_Q=6+2=8. EF_P=7; ES_Q=0 (SS only reads P's ES, unaffected), EF_Q=8;
        // ES_R=max(7,8)=8, EF_R=12 -> D_imp=12, E=12-10=2.
        Assert.Equal(2, outcome.EotEligibleDays);
        Assert.Equal(12, outcome.ImpactedDurationDays);
        Assert.Equal(2, outcome.DistinctCountableDateCount);
        EotFixtures.AssertCap(outcome); // tight: 2 <= 2.

        var driverP = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.N2PId);
        var driverQ = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.N2QId);

        // Both charges stand - NEITHER activity absorbs the other.
        Assert.Equal(2, driverP.StoppageDays);
        Assert.Equal(0, driverP.SerialChainAbsorbedDays);
        Assert.Null(driverP.AbsorbedIntoActivityCodes);
        Assert.Equal(2, driverQ.StoppageDays);
        Assert.Equal(0, driverQ.SerialChainAbsorbedDays);
        Assert.Null(driverQ.AbsorbedIntoActivityCodes);

        Assert.True(driverP.IsOnImpactedCriticalPath);
        Assert.True(driverQ.IsOnImpactedCriticalPath);

        // Sum (4) > E (2) - another non-additivity data point (§5.4), not a bug.
        Assert.Equal(2, driverP.IndicativeEotDays);
        Assert.Equal(2, driverQ.IndicativeEotDays);
        Assert.Equal(0, driverP.MarginalEotDays);
        Assert.Equal(1, driverQ.MarginalEotDays);
        Assert.True(Math.Max(driverP.MarginalEotDays, driverQ.MarginalEotDays) <= outcome.EotEligibleDays);

        Assert.Equal(0, outcome.SerialChainAbsorbedDayCount);
        Assert.Equal(4, outcome.CountableStoppageDayCount); // 2+2, both fully charged - no absorption at all.

        // Negative assertion - THE point of the fixture: a naive transitive-predecessor collapse keeps
        // only P (dropping Q's charge) and gives E=1 - UNDER-stating by a day, while still satisfying the
        // cap (1<=2). The cap cannot catch this; only the fixture's own expected value (2) can.
        Assert.NotEqual(1, outcome.EotEligibleDays);
    }

    /// <summary>★ Pins §5.2a's least-float-first rule, separating it from "keep the upstream activity"
    /// by the WHOLE answer (0 vs 2). A predecessor's float means it is not the one that truly lost
    /// production on the binding path - the collapse must keep the least-float activity (here, the
    /// successor C, float 0), not whichever one happens to be upstream (A, float 4).</summary>
    [Fact]
    public void W20_The_Collapse_Keeps_The_Least_Float_Activity_Not_Merely_The_Upstream_One()
    {
        var run = EotFixtures.BuildN3Run(EotFixtures.D(2026, 7, 1));
        var activities = EotFixtures.N3ActivityContexts();
        var entries = new[] { 8, 9 }
            .Select(day => EotFixtures.Entry(
                EotFixtures.D(2026, 7, day), WeatherImpact.FullStoppage, 8.00m, null, EotFixtures.N3AId, EotFixtures.N3CId))
            .ToList();
        var input = EotFixtures.BuildInput(entries, run, activities: activities);

        var outcome = EotEvaluator.Evaluate(input).Value;

        // Impacted: Dur_C=6+2=8. EF_A=5, EF_X=9, ES_C=max(5,9)=9, EF_C=17, ES_D=17, EF_D=21 -> D_imp=21.
        Assert.Equal(2, outcome.EotEligibleDays); // NOT 0 - the whole point of the fixture.
        Assert.Equal(21, outcome.ImpactedDurationDays);
        Assert.Equal(2, outcome.DistinctCountableDateCount);
        EotFixtures.AssertCap(outcome); // tight (2<=2) - but the cap alone cannot distinguish this from the wrong answer of 0.

        var driverC = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.N3CId);
        var driverA = Assert.Single(outcome.Drivers, d => d.ActivityId == EotFixtures.N3AId);

        // C (the successor, float 0 - the MORE BINDING activity) is kept.
        Assert.Equal(2, driverC.StoppageDays);
        Assert.Equal(0, driverC.SerialChainAbsorbedDays);
        Assert.Null(driverC.AbsorbedIntoActivityCodes);
        Assert.True(driverC.WasCriticalAtRun);
        Assert.True(driverC.IsOnImpactedCriticalPath);
        Assert.Equal(2, driverC.IndicativeEotDays);
        Assert.Equal(2, driverC.MarginalEotDays);
        Assert.Equal(0, driverC.RemainingFloatAfter);

        // A (the predecessor, float 4) is absorbed into C - and keeps its OWN float unchanged, because
        // it was never actually charged into the network.
        Assert.Equal(0, driverA.StoppageDays);
        Assert.Equal(2, driverA.SerialChainAbsorbedDays);
        Assert.Equal("C", driverA.AbsorbedIntoActivityCodes);
        Assert.False(driverA.WasCriticalAtRun);
        Assert.False(driverA.IsOnImpactedCriticalPath);
        Assert.Equal(0, driverA.IndicativeEotDays);
        Assert.Equal(0, driverA.MarginalEotDays);
        Assert.Equal(4, driverA.RemainingFloatAfter); // unchanged - A was absorbed, so it keeps its float.

        Assert.Equal(2, outcome.CountableStoppageDayCount);
        Assert.Equal(2, outcome.SerialChainAbsorbedDayCount);

        // Negative assertion - THE point of the fixture: keeping the upstream activity (A) instead of
        // the least-float one (C) gives E=0, a wrong answer that STILL satisfies the §5.3 cap (0<=2) -
        // proof the cap is necessary but not sufficient. Only this fixture's own expected value (2) catches it.
        Assert.NotEqual(0, outcome.EotEligibleDays);
    }
}
