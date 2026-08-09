namespace CMPlus.Application.Abstractions;

/// <summary>
/// Resolves $R^{cum}$/$D^{cum}$ (payment-retention.md §4, S9-BE-04) - always <c>SUM(Amount)</c> over
/// <c>ProjectFinanceLedger</c>, never a recomputation from <c>PaymentCertificate</c> rows (S9-BE-04
/// DoD: "R^cum/D^cum ต้องมาจาก SUM() ของ ledger ไม่ใช่การคำนวณซ้ำ") - the same "one source of truth"
/// discipline <c>IActualCostReader</c>/the Cash Flow query already follow for AC/PV/EV.
///
/// <para>No "as of a date" parameter, unlike <c>IActualCostReader.GetActualCostAsOfAsync</c>:
/// certificates are certified strictly in sequence (never backdated against an already-certified
/// milestone - payment-retention.md never describes an out-of-order certification), so "the ledger's
/// current running balance" is always exactly what the <i>next</i> certificate's
/// $R^{cum}_{k-1}$/$D^{cum}_{k-1}$ needs.</para>
/// </summary>
public interface IProjectFinanceLedgerReader
{
    /// <summary>$R^{cum}$ = <c>SUM(Amount) WHERE Category = Retention</c>, across every EntryType -
    /// Accrual/Release/Adjustment all legitimately move the held balance (payment-retention.md §4's
    /// own formula is stated with no EntryType filter).</summary>
    Task<decimal> GetRetentionHeldAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>$D^{cum}$ = <c>SUM(Amount) WHERE Category = Advance AND EntryType IN (Recovery,
    /// Adjustment)</c>. <c>Disbursement</c> is deliberately excluded (a resolved ambiguity -
    /// payment-retention.md §4 states the Retention sum explicitly but leaves the Advance one
    /// implicit): Disbursement records the advance going <i>out</i> - already captured once, at
    /// project level, by <c>Project.AdvanceAmountPaid</c> - and must never inflate "how much has been
    /// recovered so far".</summary>
    Task<decimal> GetAdvanceRecoveredAsync(Guid projectId, CancellationToken cancellationToken = default);
}
