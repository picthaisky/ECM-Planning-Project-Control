using CMPlus.Application.Services.Payment;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Services.Payment;

/// <summary>
/// S9-BE-02 DoD: passes fixtures P1, P2, P3, P3b, P4, P5, P7 (payment-retention.md §6 /
/// backend-developer's own sanity check per CLAUDE.md, transcribed verbatim from
/// <c>backend/tests/CMPlus.Application.Tests/Fixtures/payment-retention-fixtures.json</c>, which
/// Phase 0 already extracted from the knowledge base). P6 (ThresholdBanded) belongs to
/// <see cref="AdvanceRecoveryCalculator"/> and is covered by <c>AdvanceRecoveryCalculatorTests</c>
/// instead, since it is stated in project-wide terms with no milestone concept at all.
/// </summary>
public class CertificateCalculatorTests
{
    private static CertificateCalculationInput BaseInput(
        decimal milestoneValue,
        decimal previousPct,
        decimal thisPct,
        decimal? retentionRate = 5.00m,
        decimal? retentionCapPct = null,
        decimal contractValue = 485_000_000.00m,
        decimal retentionHeldBefore = 0m,
        decimal? advanceRate = 10.00m,
        decimal? advanceAmountPaid = null,
        decimal advanceRecoveredBefore = 0m) =>
        new(
            milestoneValue, previousPct, thisPct,
            retentionRate, retentionCapPct, contractValue, retentionHeldBefore,
            advanceRate, advanceAmountPaid, advanceRecoveredBefore,
            AdvanceRecoveryMethod.ProRata, null, null, null,
            ProjectCumulativeGrossCertifiedBefore: 0m, ManualAdvanceRecoveryAmount: null);

    [Fact]
    public void P1_Normal_Period_Thai_Config_No_Cap()
    {
        var input = BaseInput(
            milestoneValue: 21_600_000.00m, previousPct: 0.00m, thisPct: 100.00m,
            retentionRate: 5.00m, retentionCapPct: null,
            contractValue: 485_000_000.00m, retentionHeldBefore: 11_900_000.00m,
            advanceRate: 10.00m, advanceAmountPaid: 48_500_000.00m, advanceRecoveredBefore: 23_800_000.00m);

        var result = CertificateCalculator.Calculate(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(21_600_000.00m, result.Value.GrossCertifiedAmount);
        Assert.Equal(1_080_000.00m, result.Value.RetentionAmount);
        Assert.Equal(2_160_000.00m, result.Value.AdvanceRecoveryAmount);
        Assert.Equal(18_360_000.00m, result.Value.NetPayment);
    }

    [Fact]
    public void P2_Partial_Certification_Deducts_On_Period_Gross_Not_Cumulative()
    {
        var input = BaseInput(
            milestoneValue: 10_000_000.00m, previousPct: 40.00m, thisPct: 65.00m,
            retentionRate: 5.00m, retentionCapPct: null,
            advanceRate: 10.00m, advanceAmountPaid: 100_000_000.00m); // headroom/outstanding non-binding

        var result = CertificateCalculator.Calculate(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(2_500_000.00m, result.Value.GrossCertifiedAmount);
        Assert.Equal(125_000.00m, result.Value.RetentionAmount);
        Assert.Equal(250_000.00m, result.Value.AdvanceRecoveryAmount);
        Assert.Equal(2_125_000.00m, result.Value.NetPayment);
    }

    [Fact]
    public void P3_Fidic_Retention_Cap_Bites()
    {
        var input = BaseInput(
            milestoneValue: 8_000_000.00m, previousPct: 0.00m, thisPct: 100.00m,
            retentionRate: 10.00m, retentionCapPct: 5.00m, contractValue: 100_000_000.00m,
            retentionHeldBefore: 4_500_000.00m,
            advanceRate: 10.00m, advanceAmountPaid: 10_000_000.00m, advanceRecoveredBefore: 4_500_000.00m);

        var result = CertificateCalculator.Calculate(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(8_000_000.00m, result.Value.GrossCertifiedAmount);
        // Uncapped would be 800,000.00; headroom is only 500,000.00 (Rmax 5,000,000 - held 4,500,000).
        Assert.Equal(500_000.00m, result.Value.RetentionAmount);
        Assert.Equal(800_000.00m, result.Value.AdvanceRecoveryAmount);
        Assert.Equal(6_700_000.00m, result.Value.NetPayment);
        // Without cap logic this would wrongly be 6,400,000.00 - a 300,000.00 underpayment.
        Assert.NotEqual(6_400_000.00m, result.Value.NetPayment);
    }

    [Fact]
    public void P3b_Next_Period_Cap_Fully_Consumed()
    {
        var input = BaseInput(
            milestoneValue: 5_000_000.00m, previousPct: 0.00m, thisPct: 100.00m,
            retentionRate: 10.00m, retentionCapPct: 5.00m, contractValue: 100_000_000.00m,
            retentionHeldBefore: 5_000_000.00m, // cap (5,000,000.00) already fully consumed by P3
            advanceRate: 10.00m, advanceAmountPaid: 10_000_000.00m, advanceRecoveredBefore: 5_300_000.00m);

        var result = CertificateCalculator.Calculate(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(0.00m, result.Value.RetentionAmount);
        Assert.Equal(500_000.00m, result.Value.AdvanceRecoveryAmount);
        Assert.Equal(4_500_000.00m, result.Value.NetPayment);
    }

    [Fact]
    public void P4_Advance_Nearly_Exhausted()
    {
        var input = BaseInput(
            milestoneValue: 1_000_000.00m, previousPct: 0.00m, thisPct: 100.00m,
            retentionRate: 5.00m, retentionCapPct: null,
            advanceRate: 10.00m, advanceAmountPaid: 10_000_000.00m, advanceRecoveredBefore: 9_960_000.00m);

        var result = CertificateCalculator.Calculate(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(50_000.00m, result.Value.RetentionAmount);
        // Uncapped advance would be 100,000.00 but only 40,000.00 is outstanding.
        Assert.Equal(40_000.00m, result.Value.AdvanceRecoveryAmount);
        Assert.Equal(910_000.00m, result.Value.NetPayment);
    }

    [Fact]
    public void P5_Nothing_Certified_This_Period_Everything_Is_Zero_No_Division_Anywhere()
    {
        var input = BaseInput(milestoneValue: 21_600_000.00m, previousPct: 40.00m, thisPct: 40.00m);

        var result = CertificateCalculator.Calculate(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(0.00m, result.Value.GrossCertifiedAmount);
        Assert.Equal(0.00m, result.Value.RetentionAmount);
        Assert.Equal(0.00m, result.Value.AdvanceRecoveryAmount);
        Assert.Equal(0.00m, result.Value.NetPayment);
    }

    [Fact]
    public void P7_Rounding_Determinism_The_Bankers_Rounding_Trap()
    {
        // Gk = 1,000,000.10 directly - constructed via a milestone/pct pair that reproduces exactly
        // that period-gross value: M = 1,000,000.10, 0% -> 100%.
        var input = BaseInput(
            milestoneValue: 1_000_000.10m, previousPct: 0.00m, thisPct: 100.00m,
            retentionRate: 5.00m, retentionCapPct: null,
            advanceRate: 10.00m, advanceAmountPaid: 100_000_000.00m); // outstanding non-binding

        var result = CertificateCalculator.Calculate(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(1_000_000.10m, result.Value.GrossCertifiedAmount);
        // 1,000,000.10 * 5% = 50,000.005 -> 50,000.01 half-away-from-zero (NOT 50,000.00 banker's).
        Assert.Equal(50_000.01m, result.Value.RetentionAmount);
        Assert.Equal(100_000.01m, result.Value.AdvanceRecoveryAmount);
        // 850,000.08, never the banker's-rounding-wrong 850,000.09.
        Assert.Equal(850_000.08m, result.Value.NetPayment);
        Assert.NotEqual(850_000.09m, result.Value.NetPayment);
    }

    [Fact]
    public void Calculate_Blocks_Certificate_Creation_When_RetentionRate_Is_Null()
    {
        var input = BaseInput(1_000_000m, 0m, 50m, retentionRate: null);

        var result = CertificateCalculator.Calculate(input);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCodes.RetentionRateNotConfigured, result.Error);
    }

    [Fact]
    public void Calculate_Blocks_Certificate_Creation_When_AdvanceRate_Is_Null()
    {
        var input = BaseInput(1_000_000m, 0m, 50m, advanceRate: null);

        var result = CertificateCalculator.Calculate(input);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCodes.AdvanceRateNotConfigured, result.Error);
    }

    [Fact]
    public void Calculate_Rejects_A_Regressing_ApprovePct_Monotonicity_Guard()
    {
        var input = BaseInput(1_000_000m, previousPct: 65m, thisPct: 40m);

        var result = CertificateCalculator.Calculate(input);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCodes.ApprovePctNotMonotonic, result.Error);
    }

    [Fact]
    public void Calculate_Defaults_AdvanceAmountPaid_To_Rate_Times_ContractValue_When_Not_Supplied()
    {
        // A defaults to a/100 * C = 10% * 485,000,000.00 = 48,500,000.00 - same as P1's explicit A,
        // so omitting it here must reproduce P1's exact result.
        var input = BaseInput(
            milestoneValue: 21_600_000.00m, previousPct: 0.00m, thisPct: 100.00m,
            retentionRate: 5.00m, retentionCapPct: null,
            contractValue: 485_000_000.00m, retentionHeldBefore: 11_900_000.00m,
            advanceRate: 10.00m, advanceAmountPaid: null, advanceRecoveredBefore: 23_800_000.00m);

        var result = CertificateCalculator.Calculate(input);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(2_160_000.00m, result.Value.AdvanceRecoveryAmount);
        Assert.Equal(18_360_000.00m, result.Value.NetPayment);
    }
}
