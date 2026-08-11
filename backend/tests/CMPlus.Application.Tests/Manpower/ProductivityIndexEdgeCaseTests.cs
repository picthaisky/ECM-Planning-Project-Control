using CMPlus.Application.Services.Manpower;

namespace CMPlus.Application.Tests.Manpower;

/// <summary>
/// docs/specs/manpower-equipment/domain-rules.md §5.7, fixture set M-06(a-i) - the exhaustive
/// degenerate-case table. Every case is deterministic; none throws (§5.7's own closing rule: division
/// is guarded by the numerator's existence, never by try/catch - no NaN, no Infinity, no exception,
/// ever). M-06(i) (a partially-reported day) is exercised in <c>ProductivityIndexQueryHandlerTests</c>
/// instead - it needs a query-level "expected vs reported scopes" comparison this pure engine does
/// not have inputs for.
/// </summary>
public class ProductivityIndexEdgeCaseTests
{
    // (a) EMH > 0, AMH = 0, no log rows at all.
    [Fact]
    public void M06a_Progress_With_No_Log_Rows_Is_NotReported_With_The_ProgressWithoutManHours_Warning()
    {
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: 150.00m, actualManHoursInScope: 0m, actualManHoursTotal: 0m, logEntryCount: 0,
            anyActivityInScope: true, anyBudgetedActivityInScope: true, hasProgressObservationInPeriod: true);

        Assert.Null(result.Value);
        Assert.Equal(PiNullReason.NotReported, result.Reason);
        Assert.Equal(0, result.LogEntryCount);
        Assert.Contains(PiDataQualityWarning.ProgressWithoutManHours, result.Warnings);
    }

    // (b) EMH > 0, AMH = 0, but 3 rows exist (all WorkerCount=0/ManHours=0.00) - distinct Thai copy
    // from (a), which is why LogEntryCount travels on the result at all.
    [Fact]
    public void M06b_Rows_Exist_But_All_Report_Zero_Is_NoActualManHours_Distinct_From_NotReported()
    {
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: 150.00m, actualManHoursInScope: 0m, actualManHoursTotal: 0m, logEntryCount: 3,
            anyActivityInScope: true, anyBudgetedActivityInScope: true, hasProgressObservationInPeriod: true);

        Assert.Null(result.Value);
        Assert.Equal(PiNullReason.NoActualManHours, result.Reason);
        Assert.Equal(3, result.LogEntryCount);
        Assert.NotEqual(PiNullReason.NotReported, result.Reason);
    }

    // (c) EMH = 0, AMH > 0 - a DEFINED 0.00, not null. Mirrors evm-formulas' EV=0 ∧ AC>0 ⟹ CPI=0.
    [Fact]
    public void M06c_Zero_Earned_With_Positive_Actual_Hours_Is_A_Defined_000_Not_Null()
    {
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: 0m, actualManHoursInScope: 160.00m, actualManHoursTotal: 160.00m, logEntryCount: 1,
            anyActivityInScope: true, anyBudgetedActivityInScope: true, hasProgressObservationInPeriod: true);

        Assert.Equal(0.00m, result.Value);
        Assert.Null(result.Reason);
    }

    // (d) EMH = 0, AMH = 0, no rows.
    [Fact]
    public void M06d_Zero_Earned_Zero_Actual_No_Rows_Is_NotReported()
    {
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: 0m, actualManHoursInScope: 0m, actualManHoursTotal: 0m, logEntryCount: 0,
            anyActivityInScope: true, anyBudgetedActivityInScope: true, hasProgressObservationInPeriod: true);

        Assert.Null(result.Value);
        Assert.Equal(PiNullReason.NotReported, result.Reason);
        // Unlike (a), EMH is not positive here - no ProgressWithoutManHours warning.
        Assert.DoesNotContain(PiDataQualityWarning.ProgressWithoutManHours, result.Warnings);
    }

    // (e) Every in-scope BMH is NULL - the DoD's "-" case.
    [Fact]
    public void M06e_No_Budgeted_Activity_In_Scope_Is_NoBudgetManHours()
    {
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: 0m, actualManHoursInScope: 120.00m, actualManHoursTotal: 120.00m, logEntryCount: 1,
            anyActivityInScope: true, anyBudgetedActivityInScope: false, hasProgressObservationInPeriod: true);

        Assert.Null(result.Value);
        Assert.Equal(PiNullReason.NoBudgetManHours, result.Reason);
    }

    // (f) BMH = 0.00 EXPLICITLY, AMH > 0 - in scope; numerator 0 ⟹ PI = 0.00 + UnbudgetedLabourHours.
    [Fact]
    public void M06f_Explicit_Zero_Budget_With_Matched_Hours_Is_A_Defined_000_With_A_Warning_Not_Excluded()
    {
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: 0m, actualManHoursInScope: 40.00m, actualManHoursTotal: 40.00m, logEntryCount: 1,
            anyActivityInScope: true, anyBudgetedActivityInScope: true, hasProgressObservationInPeriod: true,
            hasExplicitZeroBudgetWithMatchedHours: true);

        Assert.Equal(0.00m, result.Value);
        Assert.Null(result.Reason);
        Assert.Contains(PiDataQualityWarning.UnbudgetedLabourHours, result.Warnings);
    }

    // (g) Hours logged against a node with no matching activity at all.
    [Fact]
    public void M06g_Hours_Against_A_Scope_With_No_Activity_At_All_Is_NoMatchingBudgetedScope()
    {
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: 0m, actualManHoursInScope: 0m, actualManHoursTotal: 60.00m, logEntryCount: 1,
            anyActivityInScope: false, anyBudgetedActivityInScope: false, hasProgressObservationInPeriod: true);

        Assert.Null(result.Value);
        Assert.Equal(PiNullReason.NoMatchingBudgetedScope, result.Reason);
        // The hours are excluded from PI's numerator/denominator but still show up as excluded, i.e.
        // still plotted on the histogram (§4.3: "never silently dropped").
        Assert.Equal(60.00m, result.ExcludedManHours);
    }

    // (h) Progress corrected downward: ΔP = -1.00%, AMH = 200.00 ⟹ EMH = -120.00, PI = -0.60.
    // Do not clamp.
    [Fact]
    public void M06h_A_Downward_Correction_Yields_A_Negative_Pi_And_Is_Never_Clamped()
    {
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: -120.00m, actualManHoursInScope: 200.00m, actualManHoursTotal: 200.00m, logEntryCount: 1,
            anyActivityInScope: true, anyBudgetedActivityInScope: true, hasProgressObservationInPeriod: true);

        Assert.Equal(-0.60m, result.Value);
        Assert.Null(result.Reason);
        Assert.Contains(PiDataQualityWarning.NegativeEarnedHours, result.Warnings);
    }

    // (i) Cross-tenant WbsNodeId => 404 (ADR-0002), never 422 - exercised at the repository/handler
    // level (CMPlus.Integration.Tests.Manpower), not this pure engine, which has no concept of tenant
    // identity at all. See ProductivityIndexQueryHandlerTests/tenant-isolation coverage for M-14/M-06i.

    // ---- The division guard itself: never NaN/Infinity/an exception, regardless of inputs ----

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(500, 0, 0)]
    [InlineData(-500, 0, 0)]
    public void Division_Is_Guarded_By_The_Numerators_Existence_Never_By_TryCatch(int emh, int amhInScope, int amhTotal)
    {
        var result = ProductivityIndexCalculator.Compute(
            earnedManHours: emh, actualManHoursInScope: amhInScope, actualManHoursTotal: amhTotal, logEntryCount: 1,
            anyActivityInScope: true, anyBudgetedActivityInScope: true, hasProgressObservationInPeriod: true);

        // Every one of these zero-denominator inputs must resolve to a well-formed null result, never
        // throw, never NaN, never Infinity.
        Assert.Null(result.Value);
        Assert.NotNull(result.Reason);
    }
}
