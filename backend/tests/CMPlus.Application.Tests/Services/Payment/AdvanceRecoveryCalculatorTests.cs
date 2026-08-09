using CMPlus.Application.Services.Payment;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Services.Payment;

/// <summary>
/// S9-BE-03 DoD (closes backlog D5 - no TODO/placeholder for any of the three methods):
/// <see cref="AdvanceRecoveryMethod.ProRata"/> (default), <see cref="AdvanceRecoveryMethod.ThresholdBanded"/>
/// (fixture P6, all four periods), <see cref="AdvanceRecoveryMethod.Manual"/> (still capped by the
/// outstanding balance).
/// </summary>
public class AdvanceRecoveryCalculatorTests
{
    // ---- ProRata ----

    [Fact]
    public void ProRata_Deducts_The_Configured_Rate_Of_The_Period_Gross()
    {
        var input = new AdvanceRecoveryInput(
            AdvanceRecoveryMethod.ProRata, PeriodGrossAmount: 21_600_000.00m, AdvanceRatePercent: 10.00m,
            AdvanceAmountPaid: 48_500_000.00m, AdvanceRecoveredCumulativeBefore: 23_800_000.00m,
            ContractValue: 485_000_000.00m, ProjectCumulativeGrossCertifiedThisPeriod: 0m,
            ThresholdStartPct: null, ThresholdRatePct: null, ThresholdEndPct: null, ManualAmount: null);

        var result = AdvanceRecoveryCalculator.Compute(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(2_160_000.00m, result.Value);
    }

    [Fact]
    public void ProRata_Is_Capped_By_The_Outstanding_Balance_Fixture_P4_Style()
    {
        var input = new AdvanceRecoveryInput(
            AdvanceRecoveryMethod.ProRata, PeriodGrossAmount: 1_000_000.00m, AdvanceRatePercent: 10.00m,
            AdvanceAmountPaid: 10_000_000.00m, AdvanceRecoveredCumulativeBefore: 9_960_000.00m,
            ContractValue: 100_000_000.00m, ProjectCumulativeGrossCertifiedThisPeriod: 0m,
            ThresholdStartPct: null, ThresholdRatePct: null, ThresholdEndPct: null, ManualAmount: null);

        var result = AdvanceRecoveryCalculator.Compute(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        // Uncapped would be 100,000.00; only 40,000.00 is outstanding.
        Assert.Equal(40_000.00m, result.Value);
    }

    // ---- ThresholdBanded: fixture P6, all four periods ----

    private static AdvanceRecoveryInput ThresholdBandedInput(
        decimal projectCumulativeGrossThisPeriod, decimal advanceRecoveredCumulativeBefore) =>
        new(
            AdvanceRecoveryMethod.ThresholdBanded, PeriodGrossAmount: 0m, AdvanceRatePercent: null,
            AdvanceAmountPaid: 10_000_000.00m, AdvanceRecoveredCumulativeBefore: advanceRecoveredCumulativeBefore,
            ContractValue: 100_000_000.00m, ProjectCumulativeGrossCertifiedThisPeriod: projectCumulativeGrossThisPeriod,
            ThresholdStartPct: 10m, ThresholdRatePct: 25m, ThresholdEndPct: 90m, ManualAmount: null);

    [Fact]
    public void ThresholdBanded_P6_Certificate_1_Below_Start_Threshold_Recovers_Nothing()
    {
        var result = AdvanceRecoveryCalculator.Compute(ThresholdBandedInput(8_000_000.00m, 0m));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(0.00m, result.Value);
    }

    [Fact]
    public void ThresholdBanded_P6_Certificate_2_Recovers_25_Percent_Of_The_Excess_Over_The_Start_Threshold()
    {
        // Excess over 10%*100M = 5,000,000.00; target cumulative = 25% * 5,000,000.00 = 1,250,000.00.
        var result = AdvanceRecoveryCalculator.Compute(ThresholdBandedInput(15_000_000.00m, 0m));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(1_250_000.00m, result.Value);
    }

    [Fact]
    public void ThresholdBanded_P6_Certificate_3_Recovers_The_Delta_To_The_New_Target_Cumulative()
    {
        // Excess = 15,000,000.00; target cumulative = 25% * 15,000,000.00 = 3,750,000.00;
        // Dk = target - D^cum_before(1,250,000.00) = 2,500,000.00.
        var result = AdvanceRecoveryCalculator.Compute(ThresholdBandedInput(25_000_000.00m, 1_250_000.00m));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(2_500_000.00m, result.Value);
    }

    [Fact]
    public void ThresholdBanded_P6_Later_Certificate_At_50M_Reaches_Exactly_Full_Recovery()
    {
        // Excess = 40,000,000.00; target cumulative = 25% * 40,000,000.00 = 10,000,000.00 = A exactly
        // (the fixture states only that full recovery is reached here, not a per-certificate Dk -
        // this independently derives it from the same formula the other three periods already
        // proved correct, and confirms it lands exactly on the outstanding-balance cap).
        var result = AdvanceRecoveryCalculator.Compute(ThresholdBandedInput(50_000_000.00m, 3_750_000.00m));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(6_250_000.00m, result.Value); // 10,000,000.00 target - 3,750,000.00 prior
    }

    [Fact]
    public void ThresholdBanded_Forces_Full_Recovery_Once_Cumulative_Reaches_The_End_Threshold()
    {
        // e = 50% of C(100,000,000) = 50,000,000.00; cumulative this period (60,000,000.00) has
        // passed it, so the whole outstanding balance (A - D^cum_before) recovers this period,
        // regardless of what the ordinary rho-of-excess formula alone would have given
        // (25% * (60,000,000 - 10,000,000) = 12,500,000.00, which would over-recover past A).
        var input = new AdvanceRecoveryInput(
            AdvanceRecoveryMethod.ThresholdBanded, PeriodGrossAmount: 0m, AdvanceRatePercent: null,
            AdvanceAmountPaid: 10_000_000.00m, AdvanceRecoveredCumulativeBefore: 3_000_000.00m,
            ContractValue: 100_000_000.00m, ProjectCumulativeGrossCertifiedThisPeriod: 60_000_000.00m,
            ThresholdStartPct: 10m, ThresholdRatePct: 25m, ThresholdEndPct: 50m, ManualAmount: null);

        var result = AdvanceRecoveryCalculator.Compute(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(7_000_000.00m, result.Value); // 10,000,000.00 - 3,000,000.00 outstanding
    }

    [Fact]
    public void ThresholdBanded_Blocks_When_Start_Or_Rate_Percent_Is_Not_Configured()
    {
        var missingStart = ThresholdBandedInput(15_000_000.00m, 0m) with { ThresholdStartPct = null };
        var missingRate = ThresholdBandedInput(15_000_000.00m, 0m) with { ThresholdRatePct = null };

        var resultMissingStart = AdvanceRecoveryCalculator.Compute(missingStart);
        var resultMissingRate = AdvanceRecoveryCalculator.Compute(missingRate);

        Assert.True(resultMissingStart.IsFailure);
        Assert.Equal(PaymentErrorCodes.AdvanceRecoveryThresholdBandsNotConfigured, resultMissingStart.Error);
        Assert.True(resultMissingRate.IsFailure);
        Assert.Equal(PaymentErrorCodes.AdvanceRecoveryThresholdBandsNotConfigured, resultMissingRate.Error);
    }

    [Fact]
    public void ThresholdBanded_Does_Not_Require_The_End_Percent_It_Is_Optional()
    {
        var input = ThresholdBandedInput(15_000_000.00m, 0m) with { ThresholdEndPct = null };

        var result = AdvanceRecoveryCalculator.Compute(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(1_250_000.00m, result.Value);
    }

    // ---- Manual ----

    [Fact]
    public void Manual_Uses_The_QS_Entered_Amount_As_Is_When_Within_The_Outstanding_Balance()
    {
        var input = new AdvanceRecoveryInput(
            AdvanceRecoveryMethod.Manual, PeriodGrossAmount: 0m, AdvanceRatePercent: null,
            AdvanceAmountPaid: 10_000_000.00m, AdvanceRecoveredCumulativeBefore: 2_000_000.00m,
            ContractValue: 100_000_000.00m, ProjectCumulativeGrossCertifiedThisPeriod: 0m,
            ThresholdStartPct: null, ThresholdRatePct: null, ThresholdEndPct: null, ManualAmount: 500_000.00m);

        var result = AdvanceRecoveryCalculator.Compute(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(500_000.00m, result.Value);
    }

    [Fact]
    public void Manual_Is_Still_Capped_By_The_Outstanding_Balance()
    {
        var input = new AdvanceRecoveryInput(
            AdvanceRecoveryMethod.Manual, PeriodGrossAmount: 0m, AdvanceRatePercent: null,
            AdvanceAmountPaid: 10_000_000.00m, AdvanceRecoveredCumulativeBefore: 9_800_000.00m,
            ContractValue: 100_000_000.00m, ProjectCumulativeGrossCertifiedThisPeriod: 0m,
            ThresholdStartPct: null, ThresholdRatePct: null, ThresholdEndPct: null,
            ManualAmount: 1_000_000.00m); // QS tries to recover more than the 200,000.00 outstanding

        var result = AdvanceRecoveryCalculator.Compute(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(200_000.00m, result.Value);
    }

    [Fact]
    public void Manual_Defaults_To_Zero_When_Not_Supplied()
    {
        var input = new AdvanceRecoveryInput(
            AdvanceRecoveryMethod.Manual, PeriodGrossAmount: 0m, AdvanceRatePercent: null,
            AdvanceAmountPaid: 10_000_000.00m, AdvanceRecoveredCumulativeBefore: 0m,
            ContractValue: 100_000_000.00m, ProjectCumulativeGrossCertifiedThisPeriod: 0m,
            ThresholdStartPct: null, ThresholdRatePct: null, ThresholdEndPct: null, ManualAmount: null);

        var result = AdvanceRecoveryCalculator.Compute(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(0.00m, result.Value);
    }

    [Fact]
    public void Manual_Never_Goes_Negative_Even_If_A_Negative_Amount_Is_Entered_By_Mistake()
    {
        var input = new AdvanceRecoveryInput(
            AdvanceRecoveryMethod.Manual, PeriodGrossAmount: 0m, AdvanceRatePercent: null,
            AdvanceAmountPaid: 10_000_000.00m, AdvanceRecoveredCumulativeBefore: 0m,
            ContractValue: 100_000_000.00m, ProjectCumulativeGrossCertifiedThisPeriod: 0m,
            ThresholdStartPct: null, ThresholdRatePct: null, ThresholdEndPct: null, ManualAmount: -500_000.00m);

        var result = AdvanceRecoveryCalculator.Compute(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(0.00m, result.Value);
    }
}
