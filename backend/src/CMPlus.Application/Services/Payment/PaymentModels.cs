using CMPlus.Domain.Enums;

namespace CMPlus.Application.Services.Payment;

/// <summary>
/// Input to <see cref="CertificateCalculator.Calculate"/> - every scalar the corrected Net Payment
/// formula needs (domain-decisions.md §2.1, payment-retention.md §1), already resolved by the
/// caller from <c>Project</c>/the prior certificate/<c>IProjectFinanceLedgerReader</c>'s SUM()s. A
/// pure data shape - no I/O, so the calculator itself never touches EF Core (ADR-0001).
/// </summary>
/// <param name="MilestoneValue">$M_m$.</param>
/// <param name="PreviousCumulativeApprovePct">$p^{app}_{k-1}$.</param>
/// <param name="ThisCumulativeApprovePct">$p^{app}_k$.</param>
/// <param name="RetentionRatePercent">$r$ = <c>Project.RetentionRate</c>. Null blocks certificate
/// creation (S9-BE-02 DoD) - never silently substituted.</param>
/// <param name="RetentionCapPercentage">$c$ = <c>Project.RetentionCapPercentage</c>; null = uncapped.</param>
/// <param name="ContractValue">$C$ = <c>Project.ContractValue</c>.</param>
/// <param name="RetentionHeldBefore">$R^{cum}_{k-1}$ - from <c>IProjectFinanceLedgerReader.GetRetentionHeldAsync</c>.</param>
/// <param name="AdvanceRatePercent">$a$ = <c>Project.AdvanceRate</c>. Null blocks certificate
/// creation (S9-BE-02 DoD) - never silently substituted.</param>
/// <param name="AdvanceAmountPaid">$A$ = <c>Project.AdvanceAmountPaid</c>; null defaults to
/// $\frac{a}{100}C$ (payment-retention.md §5).</param>
/// <param name="AdvanceRecoveredBefore">$D^{cum}_{k-1}$ - from <c>IProjectFinanceLedgerReader.GetAdvanceRecoveredAsync</c>.</param>
/// <param name="AdvanceRecoveryMethod"><c>Project.AdvanceRecoveryMethod</c> (S9-BE-03).</param>
/// <param name="AdvanceRecoveryStartPct">$s$ - <see cref="AdvanceRecoveryMethod.ThresholdBanded"/> only.</param>
/// <param name="AdvanceRecoveryRatePct">$\rho$ - <see cref="AdvanceRecoveryMethod.ThresholdBanded"/> only.</param>
/// <param name="AdvanceRecoveryEndPct">$e$ - <see cref="AdvanceRecoveryMethod.ThresholdBanded"/> only; optional.</param>
/// <param name="ProjectCumulativeGrossCertifiedBefore">Project-wide (not milestone-specific)
/// cumulative gross certified value across every other certificate, <i>excluding</i> this period's
/// own $G_k$ - only meaningful for <see cref="AdvanceRecoveryMethod.ThresholdBanded"/>, whose $s$/$e$
/// thresholds are a % of the whole contract, not of one milestone (fixture P6 is stated entirely in
/// project-wide terms). Resolving this sum is out of scope for S9-BE-01..04 (no
/// <c>PaymentCertificate</c>-summing reader exists yet - a follow-up for whichever task builds the
/// "create certificate" handler); callers not using <see cref="AdvanceRecoveryMethod.ThresholdBanded"/>
/// may pass <c>0m</c>.</param>
/// <param name="ManualAdvanceRecoveryAmount">QS-entered $D_k$ - <see cref="AdvanceRecoveryMethod.Manual"/> only.</param>
public sealed record CertificateCalculationInput(
    decimal MilestoneValue,
    decimal PreviousCumulativeApprovePct,
    decimal ThisCumulativeApprovePct,
    decimal? RetentionRatePercent,
    decimal? RetentionCapPercentage,
    decimal ContractValue,
    decimal RetentionHeldBefore,
    decimal? AdvanceRatePercent,
    decimal? AdvanceAmountPaid,
    decimal AdvanceRecoveredBefore,
    AdvanceRecoveryMethod AdvanceRecoveryMethod,
    decimal? AdvanceRecoveryStartPct,
    decimal? AdvanceRecoveryRatePct,
    decimal? AdvanceRecoveryEndPct,
    decimal ProjectCumulativeGrossCertifiedBefore,
    decimal? ManualAdvanceRecoveryAmount);

/// <summary>The four money-shaped outputs of <see cref="CertificateCalculator.Calculate"/>, each
/// already rounded half-away-from-zero at <c>decimal(18,2)</c> - payment-retention.md §1's
/// "round each line separately, then subtract" rule (fixture P7).</summary>
public sealed record CertificateCalculationResult(
    decimal GrossCertifiedAmount,
    decimal RetentionAmount,
    decimal AdvanceRecoveryAmount,
    decimal NetPayment);

/// <summary>
/// Input to <see cref="AdvanceRecoveryCalculator.Compute"/> (S9-BE-03) - deliberately a separate,
/// smaller shape from <see cref="CertificateCalculationInput"/> so fixture P6 (which is stated
/// entirely in its own, project-wide terms with no milestone concept at all) can drive this
/// calculator directly, independent of <see cref="CertificateCalculator"/>.
/// </summary>
/// <param name="PeriodGrossAmount">$G_k$ - <see cref="AdvanceRecoveryMethod.ProRata"/> only.</param>
/// <param name="AdvanceRatePercent">$a$ - <see cref="AdvanceRecoveryMethod.ProRata"/> only.</param>
/// <param name="AdvanceAmountPaid">$A$ - the outstanding-balance cap, every method.</param>
/// <param name="AdvanceRecoveredCumulativeBefore">$D^{cum}_{k-1}$ - the outstanding-balance cap
/// (every method) and the ThresholdBanded target-minus-prior subtraction.</param>
/// <param name="ContractValue">$C$ - <see cref="AdvanceRecoveryMethod.ThresholdBanded"/> only.</param>
/// <param name="ProjectCumulativeGrossCertifiedThisPeriod">$G^{cum}_k$, project-wide, <i>including</i>
/// this period's own $G_k$ - <see cref="AdvanceRecoveryMethod.ThresholdBanded"/> only.</param>
/// <param name="ThresholdStartPct">$s$ - <see cref="AdvanceRecoveryMethod.ThresholdBanded"/> only;
/// null blocks (<see cref="PaymentErrorCodes.AdvanceRecoveryThresholdBandsNotConfigured"/>).</param>
/// <param name="ThresholdRatePct">$\rho$ - <see cref="AdvanceRecoveryMethod.ThresholdBanded"/> only;
/// null blocks the same way as <paramref name="ThresholdStartPct"/>.</param>
/// <param name="ThresholdEndPct">$e$ - <see cref="AdvanceRecoveryMethod.ThresholdBanded"/> only;
/// optional (a null value only disables the "forced full recovery" acceleration - the outstanding-
/// balance cap alone already guarantees $D^{cum}$ never exceeds $A$, so this is not required for
/// correctness, only for matching the contract's stated full-recovery point).</param>
/// <param name="ManualAmount">QS-entered $D_k$ - <see cref="AdvanceRecoveryMethod.Manual"/> only;
/// null defaults to <c>0</c> (a legitimate "nothing recovered this period" outcome, the same as the
/// other two methods naturally produce in early periods - resolved ambiguity, not a block).</param>
public sealed record AdvanceRecoveryInput(
    AdvanceRecoveryMethod Method,
    decimal PeriodGrossAmount,
    decimal? AdvanceRatePercent,
    decimal AdvanceAmountPaid,
    decimal AdvanceRecoveredCumulativeBefore,
    decimal ContractValue,
    decimal ProjectCumulativeGrossCertifiedThisPeriod,
    decimal? ThresholdStartPct,
    decimal? ThresholdRatePct,
    decimal? ThresholdEndPct,
    decimal? ManualAmount);

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the Payment
/// Certificate money math (S9-BE-02/03). Mapping these to HTTP ProblemDetails (including the
/// user-facing "configure this in Project Info" copy the S9-BE-02 DoD calls for) is
/// <c>ResultProblemMapper</c>'s job once a real endpoint exists - S9-BE-05, not built here.</summary>
public static class PaymentErrorCodes
{
    /// <summary><c>Project.RetentionRate</c> is null - blocks certificate creation outright rather
    /// than substituting a default (S9-BE-02 DoD).</summary>
    public const string RetentionRateNotConfigured = "PaymentRetentionRateNotConfigured";

    /// <summary><c>Project.AdvanceRate</c> is null - blocks certificate creation outright rather
    /// than substituting a default (S9-BE-02 DoD).</summary>
    public const string AdvanceRateNotConfigured = "PaymentAdvanceRateNotConfigured";

    /// <summary>$p^{app}_k &lt; p^{app}_{k-1}$ - certified progress must be monotonic
    /// (payment-retention.md §1's "no division anywhere" guard).</summary>
    public const string ApprovePctNotMonotonic = "PaymentApprovePctNotMonotonic";

    /// <summary><c>Project.AdvanceRecoveryMethod</c> is <see cref="AdvanceRecoveryMethod.ThresholdBanded"/>
    /// but <c>AdvanceRecoveryStartPct</c>/<c>RatePct</c> is null - blocks rather than silently
    /// assuming the FIDIC defaults ($s{=}10,\rho{=}25,e{=}90$), which could be wrong for a specific
    /// contract's actual negotiated terms (the same "never substitute a default" philosophy as the
    /// retention/advance rate checks above).</summary>
    public const string AdvanceRecoveryThresholdBandsNotConfigured = "PaymentAdvanceRecoveryThresholdBandsNotConfigured";
}
