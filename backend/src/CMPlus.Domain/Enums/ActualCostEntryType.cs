namespace CMPlus.Domain.Enums;

/// <summary>
/// What kind of ledger fact one <see cref="Entities.ActualCostEntry"/> row represents
/// (.claude/knowledge/domain/actual-cost.md §2.1/§6.5, ADR-0013). The sign on
/// <see cref="Entities.ActualCostEntry.Amount"/> already carries the arithmetic - every entry
/// type is simply summed to compute AC(t) (actual-cost.md §7.1); this value exists for reporting/
/// reconciliation, not to select a different formula.
/// </summary>
public enum ActualCostEntryType
{
    /// <summary>An invoiced/confirmed cost (e.g. a subcontractor invoice, a payroll run).</summary>
    Actual = 1,

    /// <summary>Month-end estimate for work received/consumed but not yet invoiced
    /// (ตั้งค้างจ่าย ณ วันปิดงวด) - reversed when the real invoice lands (actual-cost.md §2.1).</summary>
    Accrual = 2,

    /// <summary>Reverses a prior <see cref="Accrual"/> once the real invoice is posted - paired via
    /// <see cref="Entities.ActualCostEntry.ReversesEntryId"/>, carries the same original
    /// <see cref="Entities.ActualCostEntry.IncurredDate"/> as the accrual it reverses.</summary>
    AccrualReversal = 3,

    /// <summary>A correction/reconciliation entry against the accounting system's own trial balance
    /// (actual-cost.md §11) - never an in-place edit of the row being corrected.</summary>
    Adjustment = 4,
}
