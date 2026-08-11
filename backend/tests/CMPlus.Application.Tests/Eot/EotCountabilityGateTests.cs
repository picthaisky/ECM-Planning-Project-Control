using CMPlus.Application.Services.Eot;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Eot;

/// <summary>Focused gate-level tests for edge cases not tied to a specific numbered
/// domain-rules.md fixture (§3.4's null-<c>HoursLost</c> fallback, only ever reachable for
/// legacy/imported rows since <c>HoursLost</c> is mandatory-by-validation on today's write path).</summary>
public class EotCountabilityGateTests
{
    [Fact]
    public void Evaluate_Treats_Null_HoursLost_With_An_Impact_As_A_Full_Day()
    {
        var entry = new EotWeatherEntryInput(
            Guid.NewGuid(), new DateOnly(2026, 7, 8), WeatherImpact.FullStoppage, HoursLost: null, RainfallMm: null,
            [EotFixtures.CId]);

        var result = EotCountabilityGate.Evaluate(entry, EotPolicySettings.Default, EotFixtures.Th6DayCalendar());

        Assert.True(result.IsCountable);
        Assert.Equal(1m, result.DayWeight); // 8.00h assumed (FullDayHours) >= MinHoursLostForCountableDay (4.00).
    }

    [Fact]
    public void Evaluate_Excludes_A_Sunday_With_No_Calendar_Exception_As_NonWorkingDay()
    {
        var sunday = new DateOnly(2026, 7, 12);
        var entry = new EotWeatherEntryInput(
            Guid.NewGuid(), sunday, WeatherImpact.FullStoppage, HoursLost: 8.00m, RainfallMm: null, [EotFixtures.CId]);

        var result = EotCountabilityGate.Evaluate(entry, EotPolicySettings.Default, EotFixtures.Th6DayCalendar());

        Assert.False(result.IsCountable);
        Assert.Equal(EotExclusionReason.NonWorkingDay, result.ExclusionReason);
    }

    [Fact]
    public void CheckInWindow_Returns_Null_For_An_InProgress_Activity()
    {
        var activity = new EotActivityContext(
            EotFixtures.CId, "C", "Activity C", ActualStart: EotFixtures.D(2026, 6, 15), PlannedStart: EotFixtures.D(2026, 6, 15),
            ActualFinish: null);

        Assert.Null(EotCountabilityGate.CheckInWindow(activity, new DateOnly(2026, 7, 8)));
    }

    // ================================================================================================
    // W-18 (§3.4/§5.3's hypothesis H1): the FullDayHours clamp, isolated at the gate - before any
    // per-activity summation (EotEvaluatorTests.W18a/b/c cover the full network-level effect).
    // ================================================================================================

    [Fact]
    public void Evaluate_FractionalAccrual_Clamps_HoursLost_At_FullDayHours_And_Flags_It()
    {
        var entry = new EotWeatherEntryInput(
            Guid.NewGuid(), new DateOnly(2026, 7, 8), WeatherImpact.FullStoppage, HoursLost: 24.00m, RainfallMm: null,
            [EotFixtures.CId]);
        var policy = EotFixtures.WithPartialDayPolicy(EotPartialDayPolicy.FractionalAccrual);

        var result = EotCountabilityGate.Evaluate(entry, policy, EotFixtures.Th6DayCalendar());

        Assert.True(result.IsCountable);
        // NOT 24.00 - the un-clamped value would let one calendar day buy floor(24/8)=3 duration days.
        Assert.Equal(8.00m, result.DayWeight);
        Assert.True(result.HoursLostClampedToFullDay);
    }

    [Fact]
    public void Evaluate_FractionalAccrual_Does_Not_Clamp_HoursLost_At_Or_Below_FullDayHours()
    {
        var entry = new EotWeatherEntryInput(
            Guid.NewGuid(), new DateOnly(2026, 7, 8), WeatherImpact.FullStoppage, HoursLost: 8.00m, RainfallMm: null,
            [EotFixtures.CId]);
        var policy = EotFixtures.WithPartialDayPolicy(EotPartialDayPolicy.FractionalAccrual);

        var result = EotCountabilityGate.Evaluate(entry, policy, EotFixtures.Th6DayCalendar());

        Assert.Equal(8.00m, result.DayWeight);
        Assert.False(result.HoursLostClampedToFullDay); // exactly at H - the clamp only bites ABOVE H.
    }

    [Fact]
    public void Evaluate_ThresholdWholeDay_Never_Sets_The_Clamp_Flag_Even_With_HoursLost_Above_FullDayHours()
    {
        // W-18b: the clamp is a FractionalAccrual-only concern - ThresholdWholeDay (default) compares
        // HoursLost against a threshold and never sums it, so a value above FullDayHours changes
        // nothing and must never be reported as "clamped".
        var entry = new EotWeatherEntryInput(
            Guid.NewGuid(), new DateOnly(2026, 7, 8), WeatherImpact.FullStoppage, HoursLost: 24.00m, RainfallMm: null,
            [EotFixtures.CId]);

        var result = EotCountabilityGate.Evaluate(entry, EotPolicySettings.Default, EotFixtures.Th6DayCalendar());

        Assert.True(result.IsCountable);
        Assert.Equal(1m, result.DayWeight); // day weight is 1.00 regardless of how many hours were recorded.
        Assert.False(result.HoursLostClampedToFullDay);
    }

    [Fact]
    public void Evaluate_FullDayOnly_Never_Sets_The_Clamp_Flag_Even_With_HoursLost_Above_FullDayHours()
    {
        var entry = new EotWeatherEntryInput(
            Guid.NewGuid(), new DateOnly(2026, 7, 8), WeatherImpact.FullStoppage, HoursLost: 24.00m, RainfallMm: null,
            [EotFixtures.CId]);
        var policy = EotFixtures.WithPartialDayPolicy(EotPartialDayPolicy.FullDayOnly);

        var result = EotCountabilityGate.Evaluate(entry, policy, EotFixtures.Th6DayCalendar());

        Assert.True(result.IsCountable);
        Assert.Equal(1m, result.DayWeight);
        Assert.False(result.HoursLostClampedToFullDay);
    }
}
