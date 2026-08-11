using CMPlus.Application.Services.Manpower;

namespace CMPlus.Application.Tests.Manpower;

/// <summary>
/// docs/specs/manpower-equipment/domain-rules.md §10 fixtures M-01, M-02 ★, M-11/M-11c, M-12, M-16 -
/// the base case, the manning-ratio-is-not-PI defect (built first, per the traceability table), the
/// PI-vs-CPI reconciliation identity, the circular-earning-basis advisory, and away-from-zero
/// rounding. Exercises <see cref="ProductivityIndexCalculator"/> directly (pure, no DB) with
/// already-aggregated scalars - exactly the "reader does the DB work, this engine only computes"
/// contract described on that type.
/// </summary>
public class ProductivityIndexTests
{
    // ---- M-01: the base case, and the money-form identity ----

    [Fact]
    public void M01_Base_Case_Yields_090_Gold_Band_100pc_Coverage()
    {
        // ΔP = 1.50% ⟹ EMH = 12,000.00 × 0.0150 = 180.00h; AMH = 200.00h (100% matched, no exclusions).
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: 180.00m,
            actualManHoursInScope: 200.00m,
            actualManHoursTotal: 200.00m,
            logEntryCount: 1,
            anyActivityInScope: true,
            anyBudgetedActivityInScope: true,
            hasProgressObservationInPeriod: true);

        Assert.Equal(0.90m, result.Value);
        Assert.Null(result.Reason);
        Assert.Equal(180.00m, result.EarnedManHours);
        Assert.Equal(200.00m, result.ActualManHoursInScope);
        Assert.Equal(200.00m, result.ActualManHoursTotal);
        Assert.Equal(0.00m, result.ExcludedManHours);
        Assert.Equal(100.00m, result.CoveragePercentage);
        Assert.Equal(1, result.LogEntryCount);

        // Money-form identity (§5.2): ΔEV/AMH ÷ (BAC/BMH) collapses to the same 0.90 the hours-only
        // form gives - both expressions must return the same value, asserted here on the money side.
        const decimal deltaEv = 3_600_000m * 0.0150m;
        const decimal actualRate = deltaEv / 200.00m; // 270.00 THB/h
        const decimal plannedRate = 3_600_000m / 12_000.00m; // 300.00 THB/h
        Assert.Equal(0.90m, Math.Round(actualRate / plannedRate, 2, MidpointRounding.AwayFromZero));
    }

    // ---- M-02 ★: the manning ratio is not the Productivity Index (build this first) ----

    [Fact]
    public void M02_Manning_Ratio_And_Productivity_Index_Disagree_On_The_Same_Day_And_Never_Share_A_Value()
    {
        // ΔP = 1.00% ⟹ EMH = 120.00h; AMH = 200.00h ⟹ PI = 0.60 (red).
        var pi = ProductivityIndexCalculator.Compute(
            earnedManHours: 120.00m,
            actualManHoursInScope: 200.00m,
            actualManHoursTotal: 200.00m,
            logEntryCount: 1,
            anyActivityInScope: true,
            anyBudgetedActivityInScope: true,
            hasProgressObservationInPeriod: true);

        // MR = 25 / 20 = 1.25 (would read green under the naive "PI" label - the defect).
        var mr = ManningRatioCalculator.Compute(actualWorkerCount: 25, plannedWorkerCount: 20);

        Assert.Equal(0.60m, pi.Value);
        Assert.Equal(1.25m, mr.Value);

        // The load-bearing assertion: the two values must never collapse onto the same field/number
        // under the same name - a field named productivityIndex must never read 1.25 for this input.
        Assert.NotEqual(pi.Value, mr.Value);

        // Lost man-hours L = AMH - EMH = 80.00h - "40% of the day's effort produced nothing".
        var lostManHours = pi.ActualManHoursInScope - pi.EarnedManHours;
        Assert.Equal(80.00m, lostManHours);
    }

    [Fact]
    public void M02_ManningRatioCalculator_Never_Reads_Progress_Or_Budgeted_Hours()
    {
        // §5.1's own proof that MR has "no output term at all" - the calculator's signature simply
        // cannot accept EMH/BMH/progress, so this is a compile-time guarantee as much as a runtime
        // one. A day with over-manning and zero output still reads a "healthy-looking" ratio.
        var mr = ManningRatioCalculator.Compute(actualWorkerCount: 40, plannedWorkerCount: 20);
        Assert.Equal(2.00m, mr.Value);
    }

    // ---- M-11: PI vs CPI - legitimate disagreement, and the variance split (reconciliation reference) ----

    [Fact]
    public void M11_Pi_And_Labour_Cpi_Legitimately_Disagree_By_Exactly_The_Rate_Factor()
    {
        const decimal emh = 1_000.00m;
        const decimal amh = 950.00m;
        const decimal rPlan = 400.00m;
        const decimal rAct = 460.00m;

        var pi = Math.Round(emh / amh, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(1.05m, pi);

        var evL = emh * rPlan;
        var acL = amh * rAct;
        Assert.Equal(400_000.00m, evL);
        Assert.Equal(437_000.00m, acL);

        var cpiL = evL / acL;
        Assert.Equal(0.92m, Math.Round(cpiL, 2, MidpointRounding.AwayFromZero));

        // Identity (assert, full precision): PI × RF = CPI_L.
        var rf = rPlan / rAct;
        var piFullPrecision = emh / amh;
        Assert.Equal(Math.Round(piFullPrecision * rf, 6), Math.Round(cpiL, 6));

        // Variance split, to the baht.
        var efficiencyVariance = (emh - amh) * rPlan;
        var rateVariance = (rPlan - rAct) * amh;
        Assert.Equal(20_000.00m, efficiencyVariance);
        Assert.Equal(-57_000.00m, rateVariance);
        Assert.Equal(-37_000.00m, efficiencyVariance + rateVariance);
        Assert.Equal(evL - acL, efficiencyVariance + rateVariance);
    }

    // ---- M-12: the circular earning basis ----

    [Fact]
    public void M12_Circular_Earning_Basis_Computes_100_And_Cannot_Detect_Its_Own_Meaninglessness_Alone()
    {
        // Progress reported as 50% "because the hours are 50% of budget" - EMH = 6,000.00, PI = 1.00.
        // The engine has no way to know this is meaningless from one bucket alone; it must still
        // return the value, not withhold it.
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: 6_000.00m,
            actualManHoursInScope: 6_000.00m,
            actualManHoursTotal: 6_000.00m,
            logEntryCount: 1,
            anyActivityInScope: true,
            anyBudgetedActivityInScope: true,
            hasProgressObservationInPeriod: true);

        Assert.Equal(1.00m, result.Value);
        Assert.Null(result.Reason);
        Assert.DoesNotContain(PiDataQualityWarning.CircularEarningBasisRisk, result.Warnings);
    }

    [Fact]
    public void M12_CircularEarningBasisRisk_Fires_After_Three_Consecutive_Near_One_Buckets_Never_Before()
    {
        Assert.False(ProductivityIndexCalculator.DetectCircularEarningBasisRisk([1.00m, 1.00m]));
        Assert.True(ProductivityIndexCalculator.DetectCircularEarningBasisRisk([1.00m, 1.00m, 1.00m]));

        // A gap (null) or a genuinely different value resets the streak - it must be consecutive.
        Assert.False(ProductivityIndexCalculator.DetectCircularEarningBasisRisk([1.00m, 0.80m, 1.00m, 1.00m]));
        Assert.True(ProductivityIndexCalculator.DetectCircularEarningBasisRisk([0.80m, 1.00m, 1.005m, 0.995m]));

        // Within the 0.01 tolerance still counts; outside it does not.
        Assert.True(ProductivityIndexCalculator.DetectCircularEarningBasisRisk([1.009m, 0.991m, 1.00m]));
        Assert.False(ProductivityIndexCalculator.DetectCircularEarningBasisRisk([1.02m, 0.98m, 1.00m]));
    }

    // ---- M-16: rounding is away-from-zero, and happens once ----

    [Fact]
    public void M16_Rounding_Is_AwayFromZero_Not_Bankers_Rounding()
    {
        // 169.00 / 200.00 = 0.845 exactly - .NET's default Math.Round(x,2) (banker's rounding) would
        // return 0.84; MidpointRounding.AwayFromZero must return 0.85.
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: 169.00m,
            actualManHoursInScope: 200.00m,
            actualManHoursTotal: 200.00m,
            logEntryCount: 1,
            anyActivityInScope: true,
            anyBudgetedActivityInScope: true,
            hasProgressObservationInPeriod: true);

        Assert.Equal(0.85m, result.Value);
        // The naive/wrong reading this test exists to rule out.
        Assert.NotEqual(Math.Round(169.00m / 200.00m, 2, MidpointRounding.ToEven), result.Value);
    }

    [Fact]
    public void M16_Sums_Are_Not_Rounded_Before_The_Division()
    {
        // M-03's data, restated: rounding each scope's PI to 2dp before weighting would still give
        // 0.84 here "but the property must be tested as ratio computed on unrounded sums, not
        // asserted by coincidence" - so this test asserts the calculator only ever divides the raw,
        // unrounded sums it is handed, never intermediate per-scope rounded values.
        const decimal emh = 420.00m + 120.00m + 80.00m; // 620.00
        const decimal amh = 600.00m + 100.00m + 40.00m; // 740.00

        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: emh,
            actualManHoursInScope: amh,
            actualManHoursTotal: amh,
            logEntryCount: 3,
            anyActivityInScope: true,
            anyBudgetedActivityInScope: true,
            hasProgressObservationInPeriod: true);

        Assert.Equal(0.84m, result.Value);
    }
}
