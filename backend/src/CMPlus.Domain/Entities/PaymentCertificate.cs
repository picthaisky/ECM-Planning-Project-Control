using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Interim Payment Certificate (IPC) aggregate - the 7-state machine of approval-workflow.md §3/§4
/// (S9-BE-01): <c>NotDue</c> -&gt; <c>Draft</c> -&gt; <c>PendingApproval</c> -&gt; <c>Certified</c> -&gt;
/// <c>Paid</c>, plus <c>Rejected</c>/<c>Cancelled</c>, plus the <c>ReturnForRevision</c> loop back to
/// <c>Draft</c>. Money math (period-gross/retention/advance-recovery) is computed externally by
/// <c>CertificateCalculator</c> (S9-BE-02) and handed to <see cref="SetPeriodClaim"/> - this
/// aggregate owns the workflow shape and the freeze/immutability rules, not the formula.
///
/// <para><b>Money-field freeze (approval-workflow.md §4 rule 4, conventions.md "submitted payment
/// certificates are immutable"):</b> <see cref="SetPeriodClaim"/> is the <i>only</i> method that can
/// change <see cref="ApprovePct"/>/<see cref="GrossCertifiedAmount"/>/<see cref="RetentionAmount"/>/
/// <see cref="AdvanceRecoveryAmount"/>/<see cref="NetPayment"/>, and it refuses to run unless
/// <see cref="Status"/> is <see cref="PaymentCertificateStatus.NotDue"/> or
/// <see cref="PaymentCertificateStatus.Draft"/> (<see cref="EnsureMoneyFieldsEditable"/>). Because
/// every other transition method only ever <i>reads</i> money fields (e.g. to build ledger entries)
/// and never assigns them, "frozen from PendingApproval onward" and "Certified is immutable forever"
/// are true by construction - there is no code path, anywhere in this type, that can reach these
/// fields once <see cref="Status"/> has left <c>Draft</c>/<c>NotDue</c>. Corrections after
/// <c>Certified</c> are a new certificate, never an edit.</para>
///
/// <para><b>Concurrency:</b> <see cref="RowVersion"/> is mapped as a SQL Server <c>rowversion</c>
/// optimistic-concurrency token (design.md §3, <c>PaymentCertificateConfiguration</c>). Two
/// simultaneous approvers loading the same row and both calling <see cref="Approve"/> will both
/// succeed in memory, but the second <c>SaveChanges</c> throws <c>DbUpdateConcurrencyException</c> -
/// which the (not-yet-built, S9-BE-05) command handler maps to <c>409 Conflict</c> - never a
/// double-advance through the chain.</para>
///
/// <para><b>Approval chain (resolved scope boundary, revised by the security review sprint-09.md
/// H-01 fix):</b> this aggregate snapshots the fully-resolved per-step role/quorum chain onto itself
/// at <see cref="Submit"/> time (<see cref="ApprovalSteps"/>) - it no longer merely stores
/// <see cref="CurrentStepNo"/>/<see cref="TotalSteps"/> and leaves the caller to re-derive "what does
/// step N require" from <c>ApprovalPolicyRule</c> later, which is exactly what let an out-of-band
/// rule sharing a <c>StepNo</c> across a different amount band authorize the wrong role (H-01).
/// <see cref="Approve"/>/<see cref="Reject"/> still re-check the role/self-approval/quorum guards
/// handed to them as arguments, as defense-in-depth (the same idiom <see cref="ApprovalAction"/>'s
/// own comment-mandatory check uses) - the caller resolves <c>expectedStepRole</c>/<c>quorumSatisfied</c>
/// from <see cref="ApprovalSteps"/> plus the append-only <c>ApprovalAction</c> history, both of which
/// live outside this aggregate. Guards this type cannot check at all (e.g. <c>ReturnForRevision</c>'s
/// "actor holds *any* pending step's role", which needs the full chain) are left entirely to the
/// caller rather than half-implemented here.</para>
/// </summary>
public sealed class PaymentCertificate : Entity, ITenantOwned
{
    private readonly List<PaymentCertificateApprovalStep> _approvalSteps = [];

    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>Sequential period/installment number (docs/9 §4) - e.g. "งวดที่ 3".</summary>
    public int MilestoneNo { get; private set; }

    public string? Description { get; private set; }

    /// <summary>$M_m$ - the milestone value this certificate is claimed against (payment-retention.md §1).</summary>
    public decimal MilestoneValue { get; private set; }

    /// <summary>$p^{app}_{k-1}$ - the prior certificate's cumulative certified % for this same
    /// milestone (payment-retention.md §5's <c>PreviousCumulativeApprovePct</c>), snapshotted here
    /// rather than re-derived by querying a prior row every time.</summary>
    public decimal PreviousCumulativeApprovePct { get; private set; }

    /// <summary>$p^{app}_k$ - cumulative certified % for this milestone, this certificate.</summary>
    public decimal ApprovePct { get; private set; }

    /// <summary>Contractor-claimed % (docs/9 §4) - distinct from <see cref="ApprovePct"/>, the
    /// QS-certified figure the money math actually runs on. Optional/reporting only.</summary>
    public decimal? ClaimPct { get; private set; }

    /// <summary>QS-assessed physical progress (docs/9 §4) - reporting/audit only, not a money-math input.</summary>
    public decimal? ActualProgressPct { get; private set; }

    /// <summary>$G_k$ - period gross certified value (payment-retention.md §1). Also the approval
    /// routing amount (approval-workflow.md §5.1).</summary>
    public decimal GrossCertifiedAmount { get; private set; }

    /// <summary>$R_k$ - period retention withheld.</summary>
    public decimal RetentionAmount { get; private set; }

    /// <summary>$D_k$ - period advance recovery deducted.</summary>
    public decimal AdvanceRecoveryAmount { get; private set; }

    /// <summary>$N_k = G_k - R_k - D_k$ - net certified amount this period (pre-tax; VAT/WHT is an
    /// explicitly deferred open question, domain-decisions.md §2.7 - never invented here).</summary>
    public decimal NetPayment { get; private set; }

    public PaymentCertificateStatus Status { get; private set; }

    /// <summary>1-based; bumped only by <see cref="ReturnForRevision"/> (approval-workflow.md §4).</summary>
    public int RevisionNo { get; private set; }

    /// <summary>1-based position in the resolved approval chain; 0 before the first
    /// <see cref="Submit"/> and after the chain snapshot is voided
    /// (<see cref="ReturnForRevision"/>/<see cref="Withdraw"/>).</summary>
    public int CurrentStepNo { get; private set; }

    /// <summary>Snapshot of the resolved chain's length at <see cref="Submit"/> time; 0 when no
    /// chain is currently attached.</summary>
    public int TotalSteps { get; private set; }

    /// <summary>Version-pinned policy that routed this revision (approval-workflow.md §5.2) - null
    /// while no chain is attached.</summary>
    public Guid? ApprovalPolicyId { get; private set; }

    public int? ApprovalPolicyVersion { get; private set; }

    /// <summary>Snapshotted from the pinned policy's <c>AllowSelfApproval</c> at <see cref="Submit"/>
    /// time (security review sprint-09.md H-01 fix) - not itself amount-tiered, so re-reading it from
    /// the same immutable, version-pinned policy row was never the bug H-01 describes, but
    /// snapshotting it here too keeps Approve fully self-contained against this document's own data,
    /// with no decision-time dependency on the policy store at all. <see langword="false"/> while no
    /// chain is attached (mirrors the fail-closed/restrictive default of the no-policy-configured
    /// fallback chain, approval-workflow.md §5.3 step 6).</summary>
    public bool AllowSelfApproval { get; private set; }

    /// <summary>The resolved approval chain for the current (or most recently voided) revision,
    /// snapshotted at <see cref="Submit"/> time - see <see cref="PaymentCertificateApprovalStep"/>'s
    /// own remarks for why this exists. Never re-derived from <see cref="ApprovalPolicyRule"/> at
    /// decision time.</summary>
    public IReadOnlyList<PaymentCertificateApprovalStep> ApprovalSteps => _approvalSteps.AsReadOnly();

    public Guid CreatedByUserId { get; private set; }

    public Guid? SubmittedByUserId { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? CertifiedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public string? PaymentReference { get; private set; }

    /// <summary>SQL Server <c>rowversion</c> optimistic-concurrency token (design.md §3) - see this
    /// type's remarks. Populated by EF Core (<c>PaymentCertificateConfiguration.IsRowVersion()</c>),
    /// never assigned by application code.</summary>
    public byte[] RowVersion { get; private set; } = [];

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private PaymentCertificate()
    {
    }

    public PaymentCertificate(
        Guid tenantId,
        Guid projectId,
        int milestoneNo,
        string? description,
        decimal milestoneValue,
        decimal previousCumulativeApprovePct,
        Guid createdByUserId,
        PaymentCertificateStatus initialStatus = PaymentCertificateStatus.Draft)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("PaymentCertificate.ProjectId is required.");
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new DomainException("PaymentCertificate.CreatedByUserId is required.");
        }

        if (initialStatus is not (PaymentCertificateStatus.NotDue or PaymentCertificateStatus.Draft))
        {
            throw new DomainException("A PaymentCertificate can only be created as NotDue or Draft.");
        }

        TenantId = tenantId;
        ProjectId = projectId;
        MilestoneNo = milestoneNo;
        Description = description;
        MilestoneValue = MoneyGuard.EnsureNonNegative(milestoneValue, nameof(MilestoneValue));
        PreviousCumulativeApprovePct = PercentageGuard.Clamp(previousCumulativeApprovePct);
        CreatedByUserId = createdByUserId;
        Status = initialStatus;
        RevisionNo = 1;
    }

    /// <summary><c>[NotDue] --period reached--&gt; [Draft]</c> (approval-workflow.md §3's diagram).
    /// No guard is stated for this transition in §4's guarded table - "period reached" is treated as
    /// an unconditional, caller-driven event (e.g. a scheduled milestone date arriving).</summary>
    public void MarkPeriodReached()
    {
        if (Status != PaymentCertificateStatus.NotDue)
        {
            throw new DomainException($"Cannot mark period reached from status {Status}; must be NotDue.");
        }

        Status = PaymentCertificateStatus.Draft;
    }

    /// <summary>
    /// Sets/recomputes this period's claim and its already-calculated money outputs (from
    /// <c>CertificateCalculator</c>, S9-BE-02) - the only method that can touch any money-shaped
    /// field, and only while <see cref="Status"/> is <see cref="PaymentCertificateStatus.NotDue"/> or
    /// <see cref="PaymentCertificateStatus.Draft"/> (see this type's remarks on the freeze rule).
    /// Re-asserts monotonicity (<c>ApprovePct &gt;= PreviousCumulativeApprovePct</c>,
    /// payment-retention.md §1) as defense-in-depth alongside <c>CertificateCalculator</c>'s own
    /// check.
    /// </summary>
    public void SetPeriodClaim(
        decimal approvePct,
        decimal? claimPct,
        decimal? actualProgressPct,
        decimal grossCertifiedAmount,
        decimal retentionAmount,
        decimal advanceRecoveryAmount,
        decimal netPayment)
    {
        EnsureMoneyFieldsEditable();

        var clampedApprovePct = PercentageGuard.Clamp(approvePct);
        if (clampedApprovePct < PreviousCumulativeApprovePct)
        {
            throw new DomainException(
                $"ApprovePct ({clampedApprovePct:0.00}) cannot be less than PreviousCumulativeApprovePct " +
                $"({PreviousCumulativeApprovePct:0.00}) - certified progress is monotonic (payment-retention.md §1).");
        }

        ApprovePct = clampedApprovePct;
        ClaimPct = PercentageGuard.Clamp(claimPct);
        ActualProgressPct = PercentageGuard.Clamp(actualProgressPct);
        GrossCertifiedAmount = MoneyGuard.EnsureNonNegative(grossCertifiedAmount, nameof(GrossCertifiedAmount));
        RetentionAmount = MoneyGuard.EnsureNonNegative(retentionAmount, nameof(RetentionAmount));
        AdvanceRecoveryAmount = MoneyGuard.EnsureNonNegative(advanceRecoveryAmount, nameof(AdvanceRecoveryAmount));
        NetPayment = MoneyGuard.EnsureNonNegative(netPayment, nameof(NetPayment));
    }

    /// <summary><c>[Draft] --Submit--&gt; [PendingApproval(step 1)]</c> (approval-workflow.md §4).
    /// Freezes money fields (via the <see cref="Status"/> change alone - see
    /// <see cref="EnsureMoneyFieldsEditable"/>) and snapshots the resolved chain - exactly as the
    /// caller's routing engine resolved it - plus the policy version pin (security review
    /// sprint-09.md H-01 fix: <paramref name="steps"/> replaces the old bare <c>totalSteps</c> count,
    /// so the required role/quorum for every step live on this document from the moment it is
    /// submitted, never re-derived from the pinned policy's full rule set later). An empty
    /// <paramref name="steps"/> mirrors approval-workflow.md §5.3 step 6 ("an empty chain blocks
    /// submission") as defense-in-depth - the routing service itself is expected to already have
    /// failed with <c>ApprovalPolicyGap</c> before ever calling this method with zero steps.</summary>
    public void Submit(
        IReadOnlyList<PaymentCertificateApprovalStepInput> steps,
        Guid approvalPolicyId,
        int approvalPolicyVersion,
        bool allowSelfApproval,
        Guid submittedByUserId,
        DateTimeOffset submittedAt)
    {
        if (Status != PaymentCertificateStatus.Draft)
        {
            throw new DomainException($"Cannot submit from status {Status}; must be Draft.");
        }

        if (steps.Count < 1)
        {
            throw new DomainException("Cannot submit with an empty approval chain (ApprovalPolicyGap).");
        }

        if (submittedByUserId == Guid.Empty)
        {
            throw new DomainException("PaymentCertificate.SubmittedByUserId is required.");
        }

        Status = PaymentCertificateStatus.PendingApproval;
        CurrentStepNo = 1;
        TotalSteps = steps.Count;
        ApprovalPolicyId = approvalPolicyId;
        ApprovalPolicyVersion = approvalPolicyVersion;
        AllowSelfApproval = allowSelfApproval;
        SubmittedByUserId = submittedByUserId;
        SubmittedAt = submittedAt;

        foreach (var step in steps)
        {
            _approvalSteps.Add(new PaymentCertificateApprovalStep(
                TenantId, Id, RevisionNo, step.StepNo, step.RequiredRole, step.QuorumCount));
        }
    }

    /// <summary><c>[PendingApproval] --Approve--&gt;</c> same state (<see cref="CurrentStepNo"/> + 1)
    /// or <c>[Certified]</c> once the chain is exhausted (approval-workflow.md §4) - but only once
    /// <paramref name="quorumSatisfied"/> is <see langword="true"/> (security review sprint-09.md
    /// H-02 fix). <paramref name="actorRole"/>/<paramref name="expectedStepRole"/> and the
    /// self-approval check are defense-in-depth (see this type's remarks) - the caller is
    /// responsible for resolving <paramref name="expectedStepRole"/> from
    /// <see cref="ApprovalSteps"/> and for counting distinct prior approvers of
    /// <see cref="CurrentStepNo"/> (from the append-only <c>ApprovalAction</c> ledger, which this
    /// aggregate does not itself hold) to decide <paramref name="quorumSatisfied"/>.</summary>
    /// <param name="quorumSatisfied">Whether this vote, combined with whatever distinct approvers
    /// already voted for <see cref="CurrentStepNo"/> on this <see cref="RevisionNo"/>, reaches the
    /// step's <c>QuorumCount</c>. When <see langword="false"/>, the vote is accepted (the guard
    /// checks above still run, and the caller is expected to still record an <c>ApprovalAction</c>
    /// row for it) but the certificate's state does not change - it stays <c>PendingApproval</c> at
    /// the same <see cref="CurrentStepNo"/>, awaiting more distinct approvers. Defaults to
    /// <see langword="true"/> (quorum of one, the overwhelmingly common case and every step's
    /// default <c>QuorumCount</c>) so existing single-signature call sites are unaffected.</param>
    public void Approve(
        Guid actorUserId,
        UserRole actorRole,
        UserRole expectedStepRole,
        bool allowSelfApproval,
        DateTimeOffset actedAt,
        bool quorumSatisfied = true)
    {
        if (Status != PaymentCertificateStatus.PendingApproval)
        {
            throw new DomainException($"Cannot approve from status {Status}; must be PendingApproval.");
        }

        if (!allowSelfApproval && (actorUserId == CreatedByUserId || actorUserId == SubmittedByUserId))
        {
            throw new DomainException("SelfApprovalNotPermitted: the creator/submitter may not approve their own certificate.");
        }

        if (actorRole != expectedStepRole)
        {
            throw new DomainException(
                $"Actor role {actorRole} does not hold the required role {expectedStepRole} for step {CurrentStepNo}.");
        }

        if (!quorumSatisfied)
        {
            // Vote recorded (by the caller, as an ApprovalAction) but this step's QuorumCount has not
            // yet been reached by enough distinct approvers (approval-workflow.md §6.2) - no state
            // change, still PendingApproval at the same CurrentStepNo.
            return;
        }

        if (CurrentStepNo >= TotalSteps)
        {
            Status = PaymentCertificateStatus.Certified;
            CertifiedAt = actedAt;
        }
        else
        {
            CurrentStepNo += 1;
        }
    }

    /// <summary><c>[PendingApproval] --ReturnForRevision--&gt; [Draft]</c>: bumps
    /// <see cref="RevisionNo"/>, voids every approval collected this revision (the chain snapshot
    /// itself), and unfreezes money fields (approval-workflow.md §4/§6.3 - "no partial carry-over").
    /// The comment-mandatory guard lives on <see cref="ApprovalAction"/>'s own constructor, not
    /// here - this method has no comment parameter to (redundantly, and possibly inconsistently)
    /// re-validate.</summary>
    public void ReturnForRevision()
    {
        if (Status != PaymentCertificateStatus.PendingApproval)
        {
            throw new DomainException($"Cannot return for revision from status {Status}; must be PendingApproval.");
        }

        RevisionNo += 1;
        VoidChainSnapshot();
        Status = PaymentCertificateStatus.Draft;
    }

    /// <summary><c>[PendingApproval] --Reject--&gt; [Rejected]</c> (terminal): only the final step's
    /// approver may reject (approval-workflow.md §4/§6.1) - intermediate approvers may only
    /// <see cref="ReturnForRevision"/>.</summary>
    public void Reject(UserRole actorRole, UserRole expectedFinalStepRole)
    {
        if (Status != PaymentCertificateStatus.PendingApproval)
        {
            throw new DomainException($"Cannot reject from status {Status}; must be PendingApproval.");
        }

        if (CurrentStepNo < TotalSteps)
        {
            throw new DomainException("Only the final step's approver may reject; intermediate approvers may only ReturnForRevision.");
        }

        if (actorRole != expectedFinalStepRole)
        {
            throw new DomainException(
                $"Actor role {actorRole} does not hold the required role {expectedFinalStepRole} to reject at the final step.");
        }

        Status = PaymentCertificateStatus.Rejected;
    }

    /// <summary><c>[PendingApproval] --Withdraw--&gt; [Draft]</c>: only the submitter, and only
    /// before any step has cleared (approval-workflow.md §4). Voids the chain snapshot but does
    /// <i>not</i> bump <see cref="RevisionNo"/> - only <see cref="ReturnForRevision"/> does.</summary>
    public void Withdraw(Guid actorUserId)
    {
        if (Status != PaymentCertificateStatus.PendingApproval)
        {
            throw new DomainException($"Cannot withdraw from status {Status}; must be PendingApproval.");
        }

        if (actorUserId != SubmittedByUserId)
        {
            throw new DomainException("Only the submitter may withdraw this certificate.");
        }

        if (CurrentStepNo > 1)
        {
            throw new DomainException("Cannot withdraw once at least one approval step has cleared.");
        }

        VoidChainSnapshot();
        Status = PaymentCertificateStatus.Draft;
    }

    /// <summary><c>[Draft] --Cancel--&gt; [Cancelled]</c> (terminal): actor must be the creator or a
    /// PM (approval-workflow.md §4).</summary>
    public void Cancel(Guid actorUserId, UserRole actorRole)
    {
        if (Status != PaymentCertificateStatus.Draft)
        {
            throw new DomainException($"Cannot cancel from status {Status}; must be Draft.");
        }

        if (actorUserId != CreatedByUserId && actorRole != UserRole.PM)
        {
            throw new DomainException("Only the creator or a PM may cancel this certificate.");
        }

        Status = PaymentCertificateStatus.Cancelled;
    }

    /// <summary><c>[Certified] --RecordPayment--&gt; [Paid]</c>: records the actual disbursement
    /// (approval-workflow.md §4). "Paid" amounts are receipts, never AC (ADR-0013) - this method
    /// never touches any Actual Cost type, by construction.</summary>
    public void RecordPayment(string paymentReference, DateTimeOffset paidAt)
    {
        if (Status != PaymentCertificateStatus.Certified)
        {
            throw new DomainException($"Cannot record payment from status {Status}; must be Certified.");
        }

        if (string.IsNullOrWhiteSpace(paymentReference))
        {
            throw new DomainException("A payment reference is required to record payment.");
        }

        Status = PaymentCertificateStatus.Paid;
        PaymentReference = paymentReference;
        PaidAt = paidAt;
    }

    /// <summary>
    /// S9-BE-04: builds (but does not persist) the <see cref="ProjectFinanceLedger"/> rows this
    /// certificate posts on reaching <see cref="PaymentCertificateStatus.Certified"/> - retention
    /// accrual and advance recovery (approval-workflow.md §7 / payment-retention.md §4). A zero
    /// amount posts no row at all (a no-op period is the absence of an entry, not a 0.00 row - the
    /// same "never post noise" discipline <see cref="ActualCostEntry"/> already established, and
    /// exactly what fixture P6's first certificate exercises: $D_k = 0.00 \Rightarrow$ no Recovery
    /// row). Persisting the returned rows (adding them to the DbContext and calling
    /// <c>SaveChanges</c>) is the caller's job - S9-BE-05, not built here.
    /// </summary>
    public IReadOnlyList<ProjectFinanceLedger> CreateCertificationLedgerEntries()
    {
        if (Status != PaymentCertificateStatus.Certified || CertifiedAt is not { } certifiedAt)
        {
            throw new DomainException("Ledger entries can only be built once the certificate is Certified.");
        }

        var entries = new List<ProjectFinanceLedger>();

        if (RetentionAmount != 0m)
        {
            entries.Add(ProjectFinanceLedger.CreateRetentionAccrual(
                TenantId, ProjectId, Id, RetentionAmount, certifiedAt, reference: MilestoneNo.ToString()));
        }

        if (AdvanceRecoveryAmount != 0m)
        {
            entries.Add(ProjectFinanceLedger.CreateAdvanceRecovery(
                TenantId, ProjectId, Id, AdvanceRecoveryAmount, certifiedAt, reference: MilestoneNo.ToString()));
        }

        return entries;
    }

    private void EnsureMoneyFieldsEditable()
    {
        if (Status is not (PaymentCertificateStatus.NotDue or PaymentCertificateStatus.Draft))
        {
            throw new DomainException(
                $"Money fields are frozen once a certificate leaves Draft (current status: {Status}) - " +
                "corrections after Certified are a new certificate, never an edit (conventions.md).");
        }
    }

    private void VoidChainSnapshot()
    {
        CurrentStepNo = 0;
        TotalSteps = 0;
        ApprovalPolicyId = null;
        ApprovalPolicyVersion = null;
        AllowSelfApproval = false;
        SubmittedByUserId = null;
        SubmittedAt = null;
        _approvalSteps.Clear();
    }
}
