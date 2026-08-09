using CMPlus.Domain.Common;

namespace CMPlus.Application.Services.Payment;

/// <summary>
/// S9-BE-02: pure Application-layer engine for the corrected Net Payment formula
/// (domain-decisions.md §2.1, payment-retention.md §1). No dependency on EF Core/MediatR/
/// FluentValidation (ADR-0001) - consumes only already-resolved scalars (from
/// <c>Project</c>/the prior certificate/<c>IProjectFinanceLedgerReader</c>'s SUM()s), so it is
/// unit-testable with nothing but plain decimals, exactly like
/// <see cref="CMPlus.Application.Services.Evm.EacCalculator"/>.
///
/// <para><b>Deducts on the period gross, never the cumulative</b> (fixture P2): $G_k = G^{cum}_k -
/// G^{cum}_{k-1}$, and every deduction below runs against $G_k$, never $G^{cum}_k$.</para>
///
/// <para><b>Rounding (fixture P7, the banker's-rounding trap):</b> each "line" is rounded once, via
/// <see cref="RoundingRules.ToMoney"/> (<c>MidpointRounding.AwayFromZero</c>), at the point it
/// becomes that line - never at full precision followed by a single rounding of $N_k$. The two
/// cumulative gross values $G^{cum}_{k-1}$/$G^{cum}_k$ are themselves treated as lines (each is a
/// real monetary "value certified to date" figure a printed certificate could show) and rounded
/// independently before subtracting to get $G_k$ - a resolved ambiguity: payment-retention.md's own
/// worked fixtures never exercise a case where this differs numerically from rounding $G_k$ itself
/// once, but rounding the two cumulative lines independently is the reading consistent with "round
/// each line separately" applied literally to every monetary quantity the formula names, and it
/// composes exactly: two already-2dp values subtracted in <see cref="decimal"/> arithmetic can never
/// reintroduce a third decimal place, so $G_k$ needs no further rounding of its own.</para>
///
/// <para><b>Retention cap</b> (fixtures P3/P3b): $R^{max} = \frac{c}{100}C$, headroom $= R^{max} -
/// R^{cum}_{k-1}$; a null <see cref="CertificateCalculationInput.RetentionCapPercentage"/> means
/// uncapped (Thai standard) - modelled as skipping the cap comparison entirely, never as a literal
/// "infinite" value participating in arithmetic.</para>
///
/// <para><b>Advance recovery</b> (S9-BE-03): delegates to <see cref="AdvanceRecoveryCalculator"/> for
/// the method-specific raw target (already capped by the outstanding balance), then applies one more,
/// universal clamp - $\min(\text{candidate}, G_k - R_k)$ - the "third term of $D_k$" note in
/// payment-retention.md §1 that guarantees $N_k \ge 0$ regardless of which method is configured.</para>
///
/// <para><b>Null retention/advance rate blocks creation</b> (S9-BE-02 DoD): checked first, before any
/// arithmetic, so a missing <c>Project.RetentionRate</c>/<c>AdvanceRate</c> never silently computes a
/// certificate at an assumed rate.</para>
/// </summary>
public static class CertificateCalculator
{
    public static Result<CertificateCalculationResult> Calculate(CertificateCalculationInput input)
    {
        if (input.RetentionRatePercent is null)
        {
            return Result<CertificateCalculationResult>.Failure(PaymentErrorCodes.RetentionRateNotConfigured);
        }

        if (input.AdvanceRatePercent is null)
        {
            return Result<CertificateCalculationResult>.Failure(PaymentErrorCodes.AdvanceRateNotConfigured);
        }

        if (input.ThisCumulativeApprovePct < input.PreviousCumulativeApprovePct)
        {
            return Result<CertificateCalculationResult>.Failure(PaymentErrorCodes.ApprovePctNotMonotonic);
        }

        // Gk = Gcum_k - Gcum_(k-1) - each cumulative rounded independently before subtracting (see
        // this type's remarks); the difference of two already-2dp decimals needs no further rounding.
        var grossCumulativeBefore = RoundingRules.ToMoney(input.MilestoneValue * input.PreviousCumulativeApprovePct / 100m);
        var grossCumulativeThis = RoundingRules.ToMoney(input.MilestoneValue * input.ThisCumulativeApprovePct / 100m);
        var periodGross = grossCumulativeThis - grossCumulativeBefore;

        var retentionAmount = ComputeRetention(input, periodGross);

        var advanceAmountPaid = input.AdvanceAmountPaid
            ?? RoundingRules.ToMoney(input.AdvanceRatePercent.Value / 100m * input.ContractValue);

        var advanceRecoveryResult = AdvanceRecoveryCalculator.Compute(new AdvanceRecoveryInput(
            input.AdvanceRecoveryMethod,
            periodGross,
            input.AdvanceRatePercent,
            advanceAmountPaid,
            input.AdvanceRecoveredBefore,
            input.ContractValue,
            input.ProjectCumulativeGrossCertifiedBefore + periodGross,
            input.AdvanceRecoveryStartPct,
            input.AdvanceRecoveryRatePct,
            input.AdvanceRecoveryEndPct,
            input.ManualAdvanceRecoveryAmount));

        if (advanceRecoveryResult.IsFailure)
        {
            return Result<CertificateCalculationResult>.Failure(advanceRecoveryResult.Error);
        }

        // The universal "never push Nk negative" guard (payment-retention.md §1's notes) - applies
        // uniformly regardless of AdvanceRecoveryMethod, layered on top of that method's own
        // outstanding-balance cap (already applied inside AdvanceRecoveryCalculator.Compute).
        var advanceRecoveryAmount = RoundingRules.ToMoney(
            Math.Max(0m, Math.Min(advanceRecoveryResult.Value, periodGross - retentionAmount)));

        var netPayment = periodGross - retentionAmount - advanceRecoveryAmount;

        return Result<CertificateCalculationResult>.Success(
            new CertificateCalculationResult(periodGross, retentionAmount, advanceRecoveryAmount, netPayment));
    }

    private static decimal ComputeRetention(CertificateCalculationInput input, decimal periodGross)
    {
        var rawRetention = input.RetentionRatePercent!.Value / 100m * periodGross;

        if (input.RetentionCapPercentage is not { } capPct)
        {
            // Uncapped (Thai standard, payment-retention.md §2): no headroom comparison at all -
            // never modelled as a literal "infinite" value participating in arithmetic.
            return RoundingRules.ToMoney(Math.Max(0m, rawRetention));
        }

        var retentionMax = RoundingRules.ToMoney(capPct / 100m * input.ContractValue);
        var headroom = retentionMax - input.RetentionHeldBefore;
        return RoundingRules.ToMoney(Math.Max(0m, Math.Min(rawRetention, headroom)));
    }
}
