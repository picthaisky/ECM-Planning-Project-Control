using CMPlus.Application.Services.Manpower;

namespace CMPlus.Application.Tests.Manpower;

/// <summary>
/// docs/specs/manpower-equipment/domain-rules.md §5.6, fixture M-05 ★ - "no planned value => '-',
/// never 0, never a silent fallback". Unmatched/unbudgeted hours are excluded from BOTH the numerator
/// and the denominator, never treated as "earned zero" (0.57, the ADR-0013 null-vs-zero failure) or
/// "assumed on plan" (0.86, inventing earned hours) - only the ratio of the genuinely matched scope
/// is defensible.
/// </summary>
public class ProductivityScopeMatchingTests
{
    // N1/C-STR: BudgetManHours 12,000.00, ManHours 300.00, ΔP 2.00% ⟹ EMH 240.00 (matched, BMH not null).
    // N3/C-MEP: BudgetManHours NULL, ManHours 120.00, ΔP 2.00% ⟹ excluded entirely (unknown, not 0).
    [Fact]
    public void M05_Project_Rollup_Excludes_The_Unbudgeted_Scope_From_Both_Numerator_And_Denominator()
    {
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: 240.00m, // only N1 contributes - N3 has no BudgetManHours to earn against
            actualManHoursInScope: 300.00m, // only N1's matched hours
            actualManHoursTotal: 420.00m, // N1 + N3's unmatched hours
            logEntryCount: 2,
            anyActivityInScope: true,
            anyBudgetedActivityInScope: true, // N1 alone is enough for the rollup to be non-null
            hasProgressObservationInPeriod: true);

        Assert.Equal(0.80m, result.Value);
        Assert.Null(result.Reason);
        Assert.Equal(300.00m, result.ActualManHoursInScope);
        Assert.Equal(420.00m, result.ActualManHoursTotal);
        Assert.Equal(120.00m, result.ExcludedManHours);
        Assert.Equal(71.43m, result.CoveragePercentage); // 300/420 = 71.4285... away-from-zero
    }

    [Fact]
    public void M05_C_MEP_Alone_Is_Null_NoBudgetManHours_Not_Zero()
    {
        // Queried at the C-MEP category/scope alone (its only activity has BudgetManHours = NULL).
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: 0m,
            actualManHoursInScope: 0m,
            actualManHoursTotal: 120.00m,
            logEntryCount: 1,
            anyActivityInScope: true,
            anyBudgetedActivityInScope: false,
            hasProgressObservationInPeriod: true);

        Assert.Null(result.Value);
        Assert.Equal(PiNullReason.NoBudgetManHours, result.Reason);
    }

    [Fact]
    public void M05_Negative_Assertions_The_Two_Plausible_Wrong_Readings_Are_Never_Produced()
    {
        // "unknown ⟹ EMH = 0": charges N3's 120h against a budget that does not exist - the ADR-0013
        // null-vs-zero failure. 240/420 = 0.5714... => 0.57.
        var unknownAsZero = Math.Round(240.00m / 420.00m, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(0.57m, unknownAsZero);

        // "unknown ⟹ assume on plan": invents 120 earned hours for N3. (240+120)/420 = 0.8571... => 0.86.
        var unknownAsOnPlan = Math.Round((240.00m + 120.00m) / 420.00m, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(0.86m, unknownAsOnPlan);

        var correct = ProductivityIndexCalculator.Compute(
            240.00m, 300.00m, 420.00m, logEntryCount: 2,
            anyActivityInScope: true, anyBudgetedActivityInScope: true, hasProgressObservationInPeriod: true);

        Assert.NotEqual(unknownAsZero, correct.Value);
        Assert.NotEqual(unknownAsOnPlan, correct.Value);
        Assert.Equal(0.80m, correct.Value);
    }
}
