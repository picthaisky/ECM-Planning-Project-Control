using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Append-only Actual Cost (AC/ACWP) ledger row - the data source that was missing entirely as of
/// Sprint 7 (see the S7 <c>IActualCostReader</c> placeholder remarks). Ruled and shaped by
/// .claude/knowledge/domain/actual-cost.md §9, accepted by ADR-0013. <c>AC(t) = SUM(Amount) WHERE
/// IncurredDate &lt;= t</c>, tenant- and project-scoped (see <c>IActualCostReader</c>).
///
/// <para>Deliberately <b>not</b> named <c>...Ledger</c> to stay unconfusable with
/// <c>ProjectFinanceLedger</c>: certified payments (money in, priced at contract rates, includes
/// margin) and actual cost (money out, priced at resource cost, no margin) are two separate
/// ledgers that must never be conflated (actual-cost.md §5) - substituting one for the other flips
/// the sign of <c>CV</c> (fixture AC-7).</para>
///
/// <para>Same append-only discipline as <see cref="ApprovalAction"/>/<see cref="EvmPeriodSnapshot"/>:
/// no mutator method and no public property setter anywhere - verified structurally (not merely by
/// convention) in <c>CMPlus.Domain.Tests</c>. Corrections are a new row
/// (<see cref="ActualCostEntryType.AccrualReversal"/>/<see cref="ActualCostEntryType.Adjustment"/>)
/// linked via <see cref="ReversesEntryId"/>, never an edit of this one (actual-cost.md §6.5/§10.1).
/// </para>
///
/// <para>Bitemporal (actual-cost.md §6, mirrors ADR-0009's treatment of <see cref="ActivityProgressLog"/>):
/// <see cref="IncurredDate"/> is valid time and drives <c>AC(t)</c>; <see cref="PostedAt"/> is
/// transaction time, audit-only. A backdated entry legitimately restates a past period's live EVM
/// read; an already-closed <see cref="EvmPeriodSnapshot"/> stays frozen regardless.</para>
/// </summary>
public sealed class ActualCostEntry : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>Control account (actual-cost.md §3) - the WBS level at which budget/EV/AC all
    /// meet. Null = project-level/unattributed (typical for preliminaries and freshly-imported,
    /// unmapped rows). A parent's AC must never be pro-rated down to children (actual-cost.md §3).</summary>
    public Guid? WbsNodeId { get; private set; }

    /// <summary>Only populated when genuinely known (e.g. a plant-hire ticket for a specific
    /// pour) - enables activity-level CPI where the data supports it (actual-cost.md §3).</summary>
    public Guid? ActivityId { get; private set; }

    public CostCategory CostCategory { get; private set; }

    public ActualCostEntryType EntryType { get; private set; }

    public ActualCostSource Source { get; private set; }

    /// <summary>
    /// Signed THB, <c>decimal(18,2)</c> - the value of work/goods received: net of recoverable
    /// input VAT, gross of withholding tax and any subcontractor retention withheld, gross of any
    /// early-payment discount taken (unless it is a genuine price reduction), excluding head-office
    /// overhead allocation (actual-cost.md §2.2). Never exactly zero (§7.6 - a zero entry, including
    /// a zero reversal, is noise) and never carries more than 2 decimal places for a manually-posted
    /// row (an import row is pre-rounded before this constructor is ever called - §7.6).
    /// <see langword="AC(t) &lt; 0"/> (an over-reversal/credit note exceeding costs) is a legitimate,
    /// computed value here - it is never clamped.
    /// </summary>
    public decimal Amount { get; private set; }

    /// <summary>Valid time - the date the cost *belongs to*; drives <c>AC(t)</c>. Compared
    /// inclusive <c>&lt;=</c>, normalised to 00:00 in the project's timezone, the same convention
    /// <see cref="ActivityProgressLog.PeriodEndDate"/> uses (actual-cost.md §7.1).</summary>
    public DateTimeOffset IncurredDate { get; private set; }

    /// <summary>Transaction time - when this row was created in CM+. Drives audit/"as-known-at"
    /// only; never used to compute <c>AC(t)</c> (actual-cost.md §6.1).</summary>
    public DateTimeOffset PostedAt { get; private set; }

    public Guid PostedByUserId { get; private set; }

    /// <summary>Self-FK pairing an <see cref="ActualCostEntryType.AccrualReversal"/>/
    /// <see cref="ActualCostEntryType.Adjustment"/> row with the entry it corrects
    /// (actual-cost.md §6.5/§9).</summary>
    public Guid? ReversesEntryId { get; private set; }

    /// <summary>Invoice / PO / DO / payroll batch number.</summary>
    public string? DocumentReference { get; private set; }

    /// <summary>Source system's job-cost code - the Sprint 9 import path's mapping key
    /// (<c>CostCodeMapping</c>, not built yet).</summary>
    public string? CostCode { get; private set; }

    public string? VendorName { get; private set; }

    /// <summary>Mandatory (enforced by the Application-layer command handler, which is the only
    /// layer that can see other rows/snapshots) when posting into an already-closed
    /// <see cref="EvmPeriodSnapshot"/> period (actual-cost.md §6.6).</summary>
    public string? Note { get; private set; }

    /// <summary>Traceability for an imported row (<c>FileImportJob</c>, Sprint 3) - always null for
    /// a manually-posted entry (this sprint's only write path).</summary>
    public Guid? FileImportJobId { get; private set; }

    /// <summary>Cheap-now optional field (actual-cost.md §9): when set, enables a genuine cash-out
    /// series later with no schema break, and fixes the S8 "Net Cash Position" label issue (§5)
    /// without a new table.</summary>
    public DateTimeOffset? PaidDate { get; private set; }

    /// <summary><c>decimal(18,4)</c> - unit-rate analysis, feeds a future <c>BottomUpEtc</c>
    /// re-estimate. Never negative when supplied.</summary>
    public decimal? Quantity { get; private set; }

    public string? UnitOfMeasure { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private ActualCostEntry()
    {
    }

    public ActualCostEntry(
        Guid tenantId,
        Guid projectId,
        Guid? wbsNodeId,
        Guid? activityId,
        CostCategory costCategory,
        ActualCostEntryType entryType,
        ActualCostSource source,
        decimal amount,
        DateTimeOffset incurredDate,
        DateTimeOffset postedAt,
        Guid postedByUserId,
        Guid? reversesEntryId,
        string? documentReference,
        string? costCode,
        string? vendorName,
        string? note,
        Guid? fileImportJobId,
        DateTimeOffset? paidDate,
        decimal? quantity,
        string? unitOfMeasure)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("ActualCostEntry.ProjectId is required.");
        }

        if (postedByUserId == Guid.Empty)
        {
            throw new DomainException("ActualCostEntry.PostedByUserId is required.");
        }

        if (amount == 0m)
        {
            throw new DomainException("ActualCostEntry.Amount cannot be zero (a zero cost entry, including a zero reversal, is noise).");
        }

        if (amount != RoundingRules.ToMoney(amount))
        {
            throw new DomainException("ActualCostEntry.Amount cannot have more than 2 decimal places.");
        }

        if (quantity is < 0m)
        {
            throw new DomainException("ActualCostEntry.Quantity cannot be negative.");
        }

        TenantId = tenantId;
        ProjectId = projectId;
        WbsNodeId = wbsNodeId;
        ActivityId = activityId;
        CostCategory = costCategory;
        EntryType = entryType;
        Source = source;
        Amount = amount;
        IncurredDate = incurredDate;
        PostedAt = postedAt;
        PostedByUserId = postedByUserId;
        ReversesEntryId = reversesEntryId;
        DocumentReference = documentReference;
        CostCode = costCode;
        VendorName = vendorName;
        Note = note;
        FileImportJobId = fileImportJobId;
        PaidDate = paidDate;
        Quantity = quantity;
        UnitOfMeasure = unitOfMeasure;
    }
}
