namespace CMPlus.Domain.Enums;

/// <summary>
/// Which running balance a <see cref="Entities.ProjectFinanceLedger"/> row affects
/// (payment-retention.md §4, S9-BE-04). Backs <c>ProjectFinanceLedger.Category</c>.
/// </summary>
public enum FinanceLedgerCategory
{
    Retention = 1,
    Advance = 2,
}
