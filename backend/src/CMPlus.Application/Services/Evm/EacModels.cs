using CMPlus.Domain.Enums;

namespace CMPlus.Application.Services.Evm;

/// <summary>
/// One <see cref="EacVariant"/>'s outcome at a data date: either every value populated
/// (<see cref="Computable"/> = <see langword="true"/>, <see cref="Reason"/> = <see langword="null"/>)
/// or every value <see langword="null"/> with a machine-readable <see cref="Reason"/> - never a bare
/// `0`/`NaN` standing in for "undefined" (the one non-negotiable this whole feature exists to
/// enforce). <see cref="PerformanceFactor"/> is always <see langword="null"/> for
/// <see cref="EacVariant.BottomUpEtc"/> even when computable - that variant has no PF concept
/// (evm-formulas.md's unified-form table lists its PF column as "-").
/// </summary>
public sealed record EacVariantResult(
    EacVariant Variant,
    bool Computable,
    decimal? PerformanceFactor,
    decimal? Etc,
    decimal? Eac,
    decimal? Vac,
    EacNullReason? Reason);

/// <summary>
/// The whole-project outcome of one <see cref="EacCalculator.ComputeAll"/> call: every variant
/// (S7-BE-03 DoD: "ทุก variant ที่คำนวณได้" - always all five, regardless of which one the UI
/// currently has selected), plus <see cref="TcpiEac"/> (measured against <see cref="EacVariant.CpiBased"/>'s
/// own EAC specifically - evm-formulas.md's stated invariant is that this always equals CPI exactly,
/// independent of whichever variant the caller has selected for display) and any data-quality
/// <see cref="Warnings"/> (currently only ever <c>EarnedValueExceedsBudget</c>).
/// </summary>
public sealed record EacCalculationResult(
    IReadOnlyList<EacVariantResult> Variants,
    decimal? TcpiEac,
    IReadOnlyList<string> Warnings)
{
    public EacVariantResult GetVariant(EacVariant variant) =>
        Variants.Single(v => v.Variant == variant);
}

/// <summary>Stable warning codes carried in <see cref="EacCalculationResult.Warnings"/> /
/// design.md §2.1's response `warnings` array.</summary>
public static class EvmWarningCodes
{
    /// <summary>EV &gt; BAC (evm-formulas.md's edge-case table: "progress or weights corrupt"). ETC
    /// is still computed and returned as-is (negative) - this is a data-quality signal, not a
    /// computation failure.</summary>
    public const string EarnedValueExceedsBudget = "EarnedValueExceedsBudget";

    /// <summary>
    /// AC(t) &lt; 0 - an over-reversal/credit note exceeding recorded costs
    /// (actual-cost.md §7.6). CPI/PF and every CPI-driven EAC variant are still computed from this
    /// value as-is here - never clamped, never silently substituted - because
    /// actual-cost.md §13 Q6 leaves adding a dedicated <c>NegativeActualCost</c> null-reason (which
    /// would change the shape of the already-shipped <c>EacNullReason</c>/<c>EvmResponseDto</c>
    /// contract) as an explicit open call for system-architect, not something this change makes
    /// unilaterally. This warning is the interim data-quality signal: any caller seeing it should
    /// treat <c>Cpi</c>/<c>Spi</c>-driven figures in the same response with caution until that
    /// decision is made.
    /// </summary>
    public const string ActualCostIsNegative = "ActualCostIsNegative";

    /// <summary>
    /// domain-rules.md §5.7's ⚠ rule: <see cref="CMPlus.Domain.Entities.Project.EacManualEtcStaleSince"/>
    /// is set - an approved Variation Order has moved <c>BAC</c> since <c>EacManualEtc</c> was last
    /// (re-)entered. <see cref="EacVariant.BottomUpEtc"/>'s $EAC$ does not track the VO (a bottom-up
    /// estimate is a professional judgement, never an arithmetic series - it is never auto-adjusted by
    /// $A$) while $VAC$ silently improves by the full VO amount, purely because the input went stale -
    /// "a project forecast 10,000,000.00 over budget now reports exactly on budget" after a
    /// +10,000,000.00 VO with no real improvement behind it. Cleared only by
    /// <see cref="CMPlus.Domain.Entities.Project.SetEacManualEtc"/> (a QS re-entering the figure).
    /// </summary>
    public const string ManualEtcPredatesBacChange = "ManualEtcPredatesBacChange";
}
