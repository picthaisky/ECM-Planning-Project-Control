namespace CMPlus.Application.Features.CashFlow;

/// <summary>
/// Machine-readable reason <c>CashFlowReceiptsDto.Cumulative</c>/<c>Periods</c> carry no data - the
/// same "typed absence, never a bare null/0 with no explanation" idiom as
/// <c>CMPlus.Domain.Enums.EacNullReason</c>. Lives in the Application feature folder rather than
/// <c>CMPlus.Domain.Enums</c> because, unlike <c>EacNullReason</c>, nothing in the Domain layer
/// (no entity/value object) ever references it - it exists purely to describe one response DTO's
/// shape, so promoting it into Domain would be layering creep (ADR-0001).
/// </summary>
public enum CashFlowReceiptsUnavailableReason
{
    /// <summary>
    /// No <c>PaymentCertificate</c>/<c>ProjectFinanceLedger</c> exists anywhere in this codebase yet
    /// - that is Sprint 9 (docs/10 §7). This is not "zero certificates issued so far" (a real,
    /// verifiable fact a query could confirm); it is "there is no ledger to query at all" - the same
    /// distinction actual-cost.md draws between "no cost entries" (a real 0.00) and "the reader
    /// itself doesn't exist yet" (the pre-ADR-0013 placeholder this sprint just replaced for AC).
    /// Model receipts the same honest way rather than repeating that mistake.
    /// </summary>
    PaymentCertificatesNotYetImplemented = 1,
}
