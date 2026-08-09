namespace CMPlus.Application.Features.CashFlow.Queries.GetCashFlow;

/// <summary>
/// One period bar's PV/EV/AC - both the period delta and the running cumulative, so the frontend
/// never has to re-derive one from the other. <see cref="PeriodStart"/> is <see langword="null"/>
/// for the very first bucket when no lower bound was requested (project inception).
/// <see cref="IsClosed"/> is <see langword="true"/> only when <see cref="PeriodEnd"/> is a frozen
/// <c>EvmPeriodSnapshot</c> (ADR-0009) - the trailing bucket up to the effective data date is always
/// <see langword="false"/> ("live"/still-open), the same distinction the S-Curve already makes
/// between snapshot-sourced history and a live recomputation.
/// </summary>
public sealed record CashFlowPeriodPointDto(
    DateTimeOffset? PeriodStart,
    DateTimeOffset PeriodEnd,
    bool IsClosed,
    decimal PvPeriod,
    decimal EvPeriod,
    decimal AcPeriod,
    decimal PvCumulative,
    decimal EvCumulative,
    decimal AcCumulative);

/// <summary>One period's worth of certified-payment receipts - shaped for forward compatibility with
/// Sprint 9, currently never populated (see <see cref="CashFlowReceiptsDto"/>).</summary>
public sealed record CashFlowReceiptsPeriodPointDto(
    DateTimeOffset? PeriodStart, DateTimeOffset PeriodEnd, decimal Period, decimal Cumulative);

/// <summary>
/// ADR-0013 §5 / actual-cost.md §5: certified-payment receipts are a separate ledger from AC and
/// must never be merged into one "cash flow" number. Modelled as its own typed-absence object
/// (mirrors <c>EacVariantResult</c>'s "every value null with a machine-readable reason" shape)
/// rather than an omitted/empty field with no explanation, because <see cref="IsAvailable"/> = false
/// today is a structural fact (no <c>PaymentCertificate</c>/<c>ProjectFinanceLedger</c> exists until
/// Sprint 9), not a data-quality warning the way an empty AC series would be.
/// </summary>
public sealed record CashFlowReceiptsDto(
    bool IsAvailable,
    decimal? Cumulative,
    IReadOnlyList<CashFlowReceiptsPeriodPointDto> Periods,
    CashFlowReceiptsUnavailableReason? UnavailableReason)
{
    public static CashFlowReceiptsDto NotYetAvailable() =>
        new(false, null, [], CashFlowReceiptsUnavailableReason.PaymentCertificatesNotYetImplemented);
}

/// <summary>
/// The full `GET /api/v1/projects/{projectId}/cash-flow` response (S8-BE-01). Every money figure
/// here is either copied verbatim from <c>EvmComputationService</c>'s output (live) or an
/// <c>EvmPeriodSnapshot</c> row (frozen), or a plain subtraction between two such values - see
/// <c>GetCashFlowQueryHandler</c>'s remarks for the full "no calculation outside the EVM engine"
/// argument. Rounded exactly once, half-away-from-zero, at this boundary (<c>RoundingRules</c>) -
/// same discipline as <c>EvmResponseDto</c>.
/// </summary>
public sealed record CashFlowResponseDto(
    Guid ProjectId,
    DateTimeOffset DataDate,
    decimal Bac,
    decimal PvCumulative,
    decimal EvCumulative,
    decimal AcCumulative,
    int ActualCostEntryCount,
    IReadOnlyList<CashFlowPeriodPointDto> Periods,
    CashFlowReceiptsDto Receipts,
    decimal? NetCashPosition,
    IReadOnlyList<string> Warnings);
