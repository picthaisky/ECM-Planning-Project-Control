using CMPlus.Application.Services.Manpower;

namespace CMPlus.Application.Tests.Manpower;

/// <summary>
/// docs/specs/manpower-equipment/domain-rules.md §6, fixtures M-03 ★ (cross-scope) and M-04 ★
/// (cross-time) - "ratio of sums, never average of ratios". Both fixtures are, from
/// <see cref="ProductivityIndexCalculator"/>'s point of view, the identical operation: sum the
/// numerators, sum the denominators, call <see cref="ProductivityIndexCalculator.Compute"/> exactly
/// once - there is no separate "aggregate" method to test, only the arithmetic difference between
/// doing that and averaging per-scope/per-day ratios instead.
/// </summary>
public class ProductivityAggregationTests
{
    // ---- M-03 ★: cross-scope aggregation: weighted, not unweighted ----

    private static readonly (decimal Emh, decimal Amh)[] M03Scopes =
    [
        (420.00m, 600.00m), // N1/C-STR: PI_s = 0.70
        (120.00m, 100.00m), // N2/C-ARC: PI_s = 1.20
        (80.00m, 40.00m), // N3/C-MEP: PI_s = 2.00
    ];

    [Fact]
    public void M03_Correct_Ratio_Of_Sums_Is_084_Red_Not_The_Naive_130_Green()
    {
        var totalEmh = M03Scopes.Sum(s => s.Emh);
        var totalAmh = M03Scopes.Sum(s => s.Amh);
        Assert.Equal(620.00m, totalEmh);
        Assert.Equal(740.00m, totalAmh);

        var correct = ProductivityIndexCalculator.Compute(
            totalEmh, totalAmh, totalAmh, logEntryCount: 3,
            anyActivityInScope: true, anyBudgetedActivityInScope: true, hasProgressObservationInPeriod: true);

        Assert.Equal(0.84m, correct.Value); // red band ([0.85,0.95) gold cutoff - 0.84 is below it

        // The naive, wrong reading this fixture exists to catch: an unweighted mean of the three
        // per-scope ratios gives 1.30 - a green "better than plan" on the exact same underlying data.
        var perScopeRatios = M03Scopes.Select(s => s.Emh / s.Amh);
        var naiveUnweightedMean = Math.Round(perScopeRatios.Average(), 2, MidpointRounding.AwayFromZero);
        Assert.Equal(1.30m, naiveUnweightedMean);
        Assert.NotEqual(naiveUnweightedMean, correct.Value);

        // Weighted-mean identity (assert): sum(PI_s * AMH_s) / sum(AMH_s) == sum(EMH_s)/sum(AMH_s).
        var weightedMean = M03Scopes.Sum(s => s.Emh / s.Amh * s.Amh) / totalAmh;
        Assert.Equal(Math.Round(weightedMean, 6), Math.Round(totalEmh / totalAmh, 6));
    }

    // ---- M-04 ★: time aggregation, and a stoppage day ----

    private static readonly (decimal ManHours, decimal Emh)[] M04Days =
    [
        (600.00m, 600.00m), // 2026-07-06 (Mon): PI = 1.00
        (600.00m, 540.00m), // 2026-07-07 (Tue): PI = 0.90
        (50.00m, 12.00m), // 2026-07-08 (Wed, weather-eot W-01 full-stoppage day): PI = 0.24
    ];

    [Fact]
    public void M04_Correct_Period_Pi_Is_092_Not_The_Naive_Mean_Of_Daily_Pis_071()
    {
        var totalEmh = M04Days.Sum(d => d.Emh);
        var totalAmh = M04Days.Sum(d => d.ManHours);
        Assert.Equal(1_152.00m, totalEmh);
        Assert.Equal(1_250.00m, totalAmh);

        var correct = ProductivityIndexCalculator.Compute(
            totalEmh, totalAmh, totalAmh, logEntryCount: 3,
            anyActivityInScope: true, anyBudgetedActivityInScope: true, hasProgressObservationInPeriod: true);

        Assert.Equal(0.92m, correct.Value);

        var naiveMeanOfDailyPis = Math.Round(M04Days.Select(d => d.Emh / d.ManHours).Average(), 2, MidpointRounding.AwayFromZero);
        Assert.Equal(0.71m, naiveMeanOfDailyPis);
        Assert.NotEqual(naiveMeanOfDailyPis, correct.Value);
    }

    [Fact]
    public void M04_The_Naive_Errors_Direction_Has_No_Consistent_Sign()
    {
        // M-03: naive OVER-states (1.30 vs correct 0.84). M-04: naive UNDER-states (0.71 vs correct
        // 0.92). "It's roughly right" is not available as a defence for the naive approach.
        var m03Naive = Math.Round(M03Scopes.Select(s => s.Emh / s.Amh).Average(), 2, MidpointRounding.AwayFromZero);
        var m03Correct = Math.Round(M03Scopes.Sum(s => s.Emh) / M03Scopes.Sum(s => s.Amh), 2, MidpointRounding.AwayFromZero);
        Assert.True(m03Naive > m03Correct);

        var m04Naive = Math.Round(M04Days.Select(d => d.Emh / d.ManHours).Average(), 2, MidpointRounding.AwayFromZero);
        var m04Correct = Math.Round(M04Days.Sum(d => d.Emh) / M04Days.Sum(d => d.ManHours), 2, MidpointRounding.AwayFromZero);
        Assert.True(m04Naive < m04Correct);
    }

    // ---- M-10: equipment metrics, and the mixed-denominator error (this fixture belongs here: it
    // is the same "never mix independent sums" family as M-03/M-04, just across resource types
    // rather than scopes/time) ----

    [Fact]
    public void M10_Equipment_Utilisation_And_Availability_Are_Correct_And_Never_Blended_Into_Pi()
    {
        var utilisation = EquipmentMetricsCalculator.ComputeUtilisationPercentage(
            equipmentOperatingHours: 112.00m, equipmentStandbyHours: 16.00m + 8.00m);
        var availability = EquipmentMetricsCalculator.ComputeAvailabilityPercentage(unitsOperating: 14, unitsOnSite: 16);

        Assert.Equal(87.50m, availability);
        Assert.Equal(82.35m, utilisation);

        // PI on M-03's underlying data is unchanged at 0.84 - equipment hours never enter its
        // denominator.
        var totalEmh = M03Scopes.Sum(s => s.Emh);
        var totalAmh = M03Scopes.Sum(s => s.Amh);
        var pi = ProductivityIndexCalculator.Compute(
            totalEmh, totalAmh, totalAmh, logEntryCount: 3,
            anyActivityInScope: true, anyBudgetedActivityInScope: true, hasProgressObservationInPeriod: true);
        Assert.Equal(0.84m, pi.Value);

        // Negative assertion: summing man-hours and equipment-hours gives 620.00 / (740.00 + 136.00)
        // = 0.707762... => 0.71 - the API must never compute or return this.
        const decimal equipmentHours = 112.00m + 24.00m; // 136.00
        var mixedDenominatorWrongValue = Math.Round(totalEmh / (totalAmh + equipmentHours), 2, MidpointRounding.AwayFromZero);
        Assert.Equal(0.71m, mixedDenominatorWrongValue);
        Assert.NotEqual(mixedDenominatorWrongValue, pi.Value);
    }

    [Fact]
    public void M10_Utilisation_And_Availability_Are_Null_Not_Zero_When_Nothing_Was_On_Site()
    {
        Assert.Null(EquipmentMetricsCalculator.ComputeUtilisationPercentage(0m, 0m));
        Assert.Null(EquipmentMetricsCalculator.ComputeAvailabilityPercentage(0, 0));
    }
}
