using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Append-only ledger of retention/advance movements against certified payments (S9-BE-04,
/// payment-retention.md §4). $R^{cum}$/$D^{cum}$ are always <c>SUM(Amount)</c> over this table -
/// never a recomputation - the same "one source of truth" discipline the Cash Flow query already
/// follows for PV/EV/AC (see <c>IProjectFinanceLedgerReader</c>). Deliberately named
/// <c>ProjectFinanceLedger</c>, not merely "...Ledger", and deliberately distinct from
/// <see cref="ActualCostEntry"/> (ADR-0013): this tracks money retained/recovered against certified
/// payments - money in, at contract price, including margin - and must never feed $AC$/ACWP.
///
/// <para>Same append-only discipline as <see cref="ApprovalAction"/>/<see cref="ActualCostEntry"/>/
/// <see cref="EvmPeriodSnapshot"/>: no mutator method and no public property setter anywhere -
/// verified structurally (not merely by convention) in <c>CMPlus.Domain.Tests</c>. A correction is a
/// new <see cref="FinanceLedgerEntryType.Adjustment"/> row, never an edit of this one.</para>
///
/// <para>Construction is public (unlike <see cref="ActivityProgressLog"/>'s <c>internal</c>
/// constructor restricted to <see cref="Activity.RecordProgress"/>) because this ledger has more
/// than one legitimate poster: a <see cref="PaymentCertificate"/> reaching <c>Certified</c> posts
/// Accrual/Recovery rows (<see cref="PaymentCertificate.CreateCertificationLedgerEntries"/>), while
/// a retention release or a manual correction is posted independently of any one certificate
/// (<see cref="PaymentCertificateId"/> is null in both those cases) - the same shape
/// <see cref="ActualCostEntry"/>'s own public constructor already established for a standalone,
/// multi-source ledger.</para>
/// </summary>
public sealed class ProjectFinanceLedger : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>Required for <see cref="FinanceLedgerEntryType.Accrual"/>/
    /// <see cref="FinanceLedgerEntryType.Recovery"/> (always posted by exactly one certificate
    /// reaching <c>Certified</c>); null for a <see cref="FinanceLedgerEntryType.Release"/>/
    /// <see cref="FinanceLedgerEntryType.Disbursement"/>/<see cref="FinanceLedgerEntryType.Adjustment"/>
    /// row not tied to one specific certificate (payment-retention.md §4's schema).</summary>
    public Guid? PaymentCertificateId { get; private set; }

    public FinanceLedgerCategory Category { get; private set; }

    public FinanceLedgerEntryType EntryType { get; private set; }

    /// <summary>
    /// Signed THB, <c>decimal(18,2)</c>. $R^{cum}$ = <c>SUM(Amount) WHERE Category = Retention</c>
    /// (Accrual positive, Release negative - payment-retention.md §4's own formula, unfiltered by
    /// EntryType). $D^{cum}$ (advance recovered) = <c>SUM(Amount) WHERE Category = Advance AND
    /// EntryType IN (Recovery, Adjustment)</c> - <see cref="FinanceLedgerEntryType.Disbursement"/>
    /// is deliberately excluded from that second sum by <c>IProjectFinanceLedgerReader</c> (a
    /// resolved ambiguity - see its remarks): disbursement records the advance going *out*, already
    /// captured once at project level by <c>Project.AdvanceAmountPaid</c>, and must never inflate
    /// "how much has been recovered so far".
    /// </summary>
    public decimal Amount { get; private set; }

    public DateTimeOffset EffectiveDate { get; private set; }

    public string? Reference { get; private set; }

    public string? Note { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private ProjectFinanceLedger()
    {
    }

    public ProjectFinanceLedger(
        Guid tenantId,
        Guid projectId,
        Guid? paymentCertificateId,
        FinanceLedgerCategory category,
        FinanceLedgerEntryType entryType,
        decimal amount,
        DateTimeOffset effectiveDate,
        string? reference,
        string? note)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("ProjectFinanceLedger.ProjectId is required.");
        }

        if (amount == 0m)
        {
            throw new DomainException(
                "ProjectFinanceLedger.Amount cannot be zero (a no-op ledger entry is noise - do not post one).");
        }

        if (amount != RoundingRules.ToMoney(amount))
        {
            throw new DomainException("ProjectFinanceLedger.Amount cannot have more than 2 decimal places.");
        }

        ValidateCategoryEntryTypeCombination(category, entryType);

        if (paymentCertificateId is null && entryType is FinanceLedgerEntryType.Accrual or FinanceLedgerEntryType.Recovery)
        {
            throw new DomainException($"{entryType} entries must reference the PaymentCertificate that posted them.");
        }

        TenantId = tenantId;
        ProjectId = projectId;
        PaymentCertificateId = paymentCertificateId;
        Category = category;
        EntryType = entryType;
        Amount = amount;
        EffectiveDate = effectiveDate;
        Reference = reference;
        Note = note;
    }

    /// <summary>Retention withheld from a certified payment - always increases the held balance.
    /// Posted when a <see cref="PaymentCertificate"/> reaches <c>Certified</c>
    /// (<see cref="PaymentCertificate.CreateCertificationLedgerEntries"/>).</summary>
    public static ProjectFinanceLedger CreateRetentionAccrual(
        Guid tenantId,
        Guid projectId,
        Guid paymentCertificateId,
        decimal amount,
        DateTimeOffset effectiveDate,
        string? reference = null,
        string? note = null)
    {
        if (amount <= 0m)
        {
            throw new DomainException("A retention accrual must be a positive amount.");
        }

        return new ProjectFinanceLedger(
            tenantId, projectId, paymentCertificateId, FinanceLedgerCategory.Retention,
            FinanceLedgerEntryType.Accrual, amount, effectiveDate, reference, note);
    }

    /// <summary>Retention returned to the contractor at substantial completion / end of the Defects
    /// Liability Period (payment-retention.md §4) - always negative (decreases the held balance).
    /// Not tied to any one certificate.</summary>
    public static ProjectFinanceLedger CreateRetentionRelease(
        Guid tenantId,
        Guid projectId,
        decimal amount,
        DateTimeOffset effectiveDate,
        string reference,
        string? note = null)
    {
        if (amount >= 0m)
        {
            throw new DomainException("A retention release must be a negative amount (it decreases the held balance).");
        }

        return new ProjectFinanceLedger(
            tenantId, projectId, paymentCertificateId: null, FinanceLedgerCategory.Retention,
            FinanceLedgerEntryType.Release, amount, effectiveDate, reference, note);
    }

    /// <summary>Advance recovered (deducted) from a certified payment - always positive (increases
    /// the recovered-to-date balance). Posted when a <see cref="PaymentCertificate"/> reaches
    /// <c>Certified</c>.</summary>
    public static ProjectFinanceLedger CreateAdvanceRecovery(
        Guid tenantId,
        Guid projectId,
        Guid paymentCertificateId,
        decimal amount,
        DateTimeOffset effectiveDate,
        string? reference = null,
        string? note = null)
    {
        if (amount <= 0m)
        {
            throw new DomainException("An advance recovery must be a positive amount.");
        }

        return new ProjectFinanceLedger(
            tenantId, projectId, paymentCertificateId, FinanceLedgerCategory.Advance,
            FinanceLedgerEntryType.Recovery, amount, effectiveDate, reference, note);
    }

    /// <summary>Manual correction to either balance - the only entry type whose sign is
    /// unconstrained, so <paramref name="note"/> explaining the correction is mandatory (mirrors
    /// <see cref="ActualCostEntry"/>'s "note required" discipline for an unusual/manual posting).</summary>
    public static ProjectFinanceLedger CreateAdjustment(
        Guid tenantId,
        Guid projectId,
        Guid? paymentCertificateId,
        FinanceLedgerCategory category,
        decimal amount,
        DateTimeOffset effectiveDate,
        string reference,
        string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new DomainException("A manual ProjectFinanceLedger adjustment requires a Note explaining it.");
        }

        return new ProjectFinanceLedger(
            tenantId, projectId, paymentCertificateId, category, FinanceLedgerEntryType.Adjustment,
            amount, effectiveDate, reference, note);
    }

    private static void ValidateCategoryEntryTypeCombination(FinanceLedgerCategory category, FinanceLedgerEntryType entryType)
    {
        var valid = category switch
        {
            FinanceLedgerCategory.Retention => entryType is FinanceLedgerEntryType.Accrual
                or FinanceLedgerEntryType.Release or FinanceLedgerEntryType.Adjustment,
            FinanceLedgerCategory.Advance => entryType is FinanceLedgerEntryType.Disbursement
                or FinanceLedgerEntryType.Recovery or FinanceLedgerEntryType.Adjustment,
            _ => false,
        };

        if (!valid)
        {
            throw new DomainException($"EntryType {entryType} is not valid for Category {category}.");
        }
    }
}
