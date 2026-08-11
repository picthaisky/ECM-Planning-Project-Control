namespace CMPlus.Application.Features.CashFlow;

/// <summary>Stable data-quality warning codes for <c>CashFlowResponseDto.Warnings</c>, carried
/// alongside whatever <c>EacCalculationResult.Warnings</c> already contributes (never replacing
/// them - <c>GetCashFlowQueryHandler</c> appends to that list, it does not own a separate field).</summary>
public static class CashFlowWarningCodes
{
    /// <summary>
    /// The effective data date coincides exactly with an already-closed <c>EvmPeriodSnapshot</c>,
    /// but the live recomputation at that same date (what every top-level cumulative field in this
    /// response reflects, to stay consistent with `GetEvmQuery`'s own always-live headline) no
    /// longer agrees with the frozen snapshot. This is the expected, legitimate consequence of a
    /// backdated <c>ActualCostEntry</c>/<c>ActivityProgressLog</c> correction landing after that
    /// period closed (ADR-0009, ADR-0013 §6.4's "periodRestated" recommendation) - not a bug, but it
    /// must be surfaced, never left as two silently disagreeing numbers between the top-level
    /// cumulative fields and <c>Periods[]</c>'s own last closed bucket (actual-cost.md §6.4: "two
    /// numbers disagreeing across two screens with no explanation destroys user trust").
    /// </summary>
    public const string PeriodRestated = "CashFlowPeriodRestated";
}
