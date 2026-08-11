using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Services.Payment;

/// <summary>
/// S9-BE-03 (closes backlog open question D5 - "advance-recovery mechanics undefined" - resolved
/// 2026-07-27, domain-decisions.md §2.3): pure Application-layer engine implementing all three
/// <see cref="AdvanceRecoveryMethod"/> values, no TODO/placeholder for any of them. No dependency on
/// EF Core/MediatR (ADR-0001) - unit-testable with nothing but plain decimals, exactly like
/// <see cref="CMPlus.Application.Services.Evm.EacCalculator"/>.
///
/// <para><b>Composition with the universal outstanding-balance cap:</b> each method below computes
/// only its own distinguishing "raw target" formula; <see cref="Compute"/> then applies
/// $\max(0,\min(\text{raw}, A - D^{cum}_{k-1}))$ uniformly across all three methods before rounding
/// once. This is mathematically identical to embedding the cap inside each method's own formula
/// (<c>min</c> is associative, and the raw target is never negative enough for early-vs-late
/// clamping to matter here) but keeps every method's code focused on what actually varies between
/// them - verified against fixture P6's all four periods and against <c>CertificateCalculator</c>'s
/// P1-P7 fixtures (which exercise <see cref="AdvanceRecoveryMethod.ProRata"/> through this same
/// path).</para>
///
/// <para><b>The "never push $N_k$ negative" guard is deliberately NOT applied here</b> - that
/// depends on retention ($G_k - R_k$), a concept this calculator has no visibility into by design
/// (fixture P6 is stated with no retention at all). <c>CertificateCalculator</c> applies that
/// additional, final clamp on top of this method's own result.</para>
/// </summary>
public static class AdvanceRecoveryCalculator
{
    public static Result<decimal> Compute(AdvanceRecoveryInput input)
    {
        var rawResult = input.Method switch
        {
            AdvanceRecoveryMethod.ProRata => Result<decimal>.Success(ComputeProRataRaw(input)),
            AdvanceRecoveryMethod.ThresholdBanded => ComputeThresholdBandedRaw(input),
            AdvanceRecoveryMethod.Manual => Result<decimal>.Success(input.ManualAmount ?? 0m),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input.Method, "Unknown AdvanceRecoveryMethod."),
        };

        if (rawResult.IsFailure)
        {
            return rawResult;
        }

        var outstanding = input.AdvanceAmountPaid - input.AdvanceRecoveredCumulativeBefore;
        var capped = Math.Max(0m, Math.Min(rawResult.Value, outstanding));
        return Result<decimal>.Success(RoundingRules.ToMoney(capped));
    }

    /// <summary>Thai practice (default): $D_k = \frac{a}{100}G_k$ (payment-retention.md §3).</summary>
    private static decimal ComputeProRataRaw(AdvanceRecoveryInput input) =>
        (input.AdvanceRatePercent ?? 0m) / 100m * input.PeriodGrossAmount;

    /// <summary>
    /// FIDIC 14.2: no recovery below start threshold $s$, amortises at rate $\rho$ of the excess
    /// over $sC$, forced full recovery once cumulative reaches $e\%$ of $C$ (payment-retention.md
    /// §3). Verified against fixture P6's certificates 1-3 (0.00 / 1,250,000.00 / 2,500,000.00).
    /// </summary>
    private static Result<decimal> ComputeThresholdBandedRaw(AdvanceRecoveryInput input)
    {
        if (input.ThresholdStartPct is not { } startPct || input.ThresholdRatePct is not { } ratePct)
        {
            return Result<decimal>.Failure(PaymentErrorCodes.AdvanceRecoveryThresholdBandsNotConfigured);
        }

        if (input.ThresholdEndPct is { } endPct
            && input.ProjectCumulativeGrossCertifiedThisPeriod >= endPct / 100m * input.ContractValue)
        {
            // Forced full recovery: recover everything still outstanding this period.
            return Result<decimal>.Success(input.AdvanceAmountPaid - input.AdvanceRecoveredCumulativeBefore);
        }

        var excess = Math.Max(0m, input.ProjectCumulativeGrossCertifiedThisPeriod - startPct / 100m * input.ContractValue);
        var targetCumulativeRecovered = ratePct / 100m * excess;
        return Result<decimal>.Success(targetCumulativeRecovered - input.AdvanceRecoveredCumulativeBefore);
    }
}
