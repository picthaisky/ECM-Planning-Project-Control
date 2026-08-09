namespace CMPlus.Domain.Enums;

/// <summary>
/// What kind of movement a <see cref="Entities.ProjectFinanceLedger"/> row records
/// (payment-retention.md §4, S9-BE-04). Backs <c>ProjectFinanceLedger.EntryType</c>.
/// </summary>
public enum FinanceLedgerEntryType
{
    /// <summary>Retention withheld from a certified payment - increases the held balance.</summary>
    Accrual = 1,

    /// <summary>Retention returned to the contractor (substantial completion / end of DLP) -
    /// decreases the held balance.</summary>
    Release = 2,

    /// <summary>The mobilisation advance paid out to the contractor. Never counted toward
    /// $D^{cum}$ (advance recovered) - see <c>IProjectFinanceLedgerReader</c>'s remarks.</summary>
    Disbursement = 3,

    /// <summary>Advance recovered (deducted) from a certified payment - increases $D^{cum}$.</summary>
    Recovery = 4,

    /// <summary>Manual correction to either balance - the only entry type whose sign is
    /// unconstrained; always carries a mandatory explanatory Note.</summary>
    Adjustment = 5,
}
