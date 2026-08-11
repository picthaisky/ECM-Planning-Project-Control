using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Variation Order (VO/CO) aggregate - the five-state machine of domain-rules.md §2:
/// <c>Draft</c> -&gt; <c>PendingApproval</c> -&gt; <c>Approved</c>/<c>Rejected</c>, <c>Draft</c> -&gt;
/// <c>Cancelled</c>, <c>PendingApproval</c> -&gt; <c>Draft</c> via <see cref="ReturnForRevision"/>
/// (<see cref="RevisionNo"/>+1) or <see cref="Withdraw"/> (unchanged). S10-BE-01.
///
/// <para><b>Follows the <see cref="PaymentCertificate"/> (Sprint 9) shape exactly</b> - snapshotted
/// chain (<see cref="ApprovalSteps"/>, H-01 fix), <see cref="RowVersion"/> optimistic concurrency,
/// <see cref="LastVoteAt"/> (N-03 parity), quorum-bound <see cref="Reject"/> with
/// <c>rejectQuorumSatisfied</c> (ADR-0016) - except in the five places domain-rules.md §2.2
/// enumerates: no <c>NotDue</c>; no <c>Paid</c> (a VO never posts a <see cref="ProjectFinanceLedger"/>
/// row, by construction - there is no code path in this type that could build one); <c>Approved</c>
/// fires an irreversible external effect instead of merely posting a ledger row (see
/// <see cref="RecordApprovalEffects"/>); supporting evidence may be appended while
/// <c>PendingApproval</c> (not modelled by this type - attachments are a separate,
/// append-only-by-construction concern, out of this sprint's scope); and <see cref="Reject"/> is
/// quorum-bound identically to <see cref="Approve"/> from day one (ADR-0016 - Sprint 9 had to be
/// retrofitted for this, Sprint 10 ships it correctly the first time).</para>
///
/// <para><b>Money/scope freeze (domain-rules.md §2.4), enforced by construction exactly like
/// <see cref="PaymentCertificate"/>'s <c>EnsureMoneyFieldsEditable</c>:</b> <see cref="SetVariationContent"/>
/// is the <i>only</i> method that can change <see cref="Amount"/>/<see cref="Description"/>/
/// <see cref="Justification"/>/<see cref="TimeImpactDays"/>/<see cref="ScopeItems"/>, and it refuses
/// unless <see cref="Status"/> is <see cref="VariationOrderStatus.Draft"/>
/// (<see cref="EnsureContentEditable"/>). <see cref="Type"/> is a computed property with no backing
/// column at all, so it can never independently disagree with <see cref="Amount"/>'s sign
/// (domain-rules.md §1) - not even a persistence-layer bug could desynchronise the two, because there
/// is only ever one stored value to begin with.</para>
///
/// <para><b>BAC/ContractValue effects live outside this aggregate, deliberately.</b>
/// <see cref="Approve"/> only ever flips <see cref="Status"/>/<see cref="CurrentStepNo"/> (identical
/// shape to <see cref="PaymentCertificate.Approve"/>) - it does not, and structurally cannot, reach
/// into <see cref="Project.BAC"/>/<see cref="Project.ContractValue"/> (a different aggregate,
/// ADR-0001 layering: an aggregate never reaches across another aggregate's boundary). The command
/// handler (S10-BE-03, <c>ApproveVariationOrderCommandHandler</c>) calls
/// <see cref="Project.ApplyVariationOrderApproval"/> separately, in the same unit of work, once
/// <see cref="Status"/> has actually reached <see cref="VariationOrderStatus.Approved"/>, then calls
/// <see cref="RecordApprovalEffects"/> to stamp the immutable before/after figures onto this document
/// for its own audit trail - the same two-step shape
/// <c>ApprovePaymentCertificateCommandHandler</c> already uses for
/// <see cref="PaymentCertificate.CreateCertificationLedgerEntries"/>.</para>
/// </summary>
public sealed class VariationOrder : Entity, ITenantOwned
{
    private readonly List<VariationOrderApprovalStep> _approvalSteps = [];
    private readonly List<VariationOrderScopeItem> _scopeItems = [];

    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>Unique per <c>(TenantId, ProjectId)</c> (domain-rules.md §7.1), assigned at creation,
    /// immutable in every state - a returned-for-revision VO keeps its number; <see cref="RevisionNo"/>
    /// distinguishes revisions.</summary>
    public string VoNumber { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? Justification { get; private set; }

    /// <summary>$A$ - signed <c>decimal(18,2)</c>. <c>Add</c> ⟹ positive, <c>Deduct</c> ⟹ negative,
    /// zero permitted (a time-only/equal-value-swap variation, domain-rules.md §7.3).</summary>
    public decimal Amount { get; private set; }

    /// <summary>Derived from <see cref="Amount"/>'s sign - never independently settable, no backing
    /// column (see this type's own remarks). Zero is grouped with <c>Add</c>: it omits nothing.</summary>
    public VariationOrderType Type => Amount < 0m ? VariationOrderType.Deduct : VariationOrderType.Add;

    public int TimeImpactDays { get; private set; }

    public VariationOrderStatus Status { get; private set; }

    /// <summary>1-based; bumped only by <see cref="ReturnForRevision"/> (domain-rules.md §1).</summary>
    public int RevisionNo { get; private set; }

    /// <summary>1-based position in the resolved approval chain; 0 before the first
    /// <see cref="Submit"/> and after the chain snapshot is voided.</summary>
    public int CurrentStepNo { get; private set; }

    /// <summary>Snapshot of the resolved chain's length at <see cref="Submit"/> time; 0 when no
    /// chain is currently attached.</summary>
    public int TotalSteps { get; private set; }

    /// <summary>Version-pinned policy that routed this revision (ADR-0008) - null while no chain is
    /// attached. <c>Guid.Empty</c> for the "no policy configured at all" fallback chain
    /// (approval-workflow.md §5.3 step 6), mirroring <see cref="ApprovalRoutingService"/>'s own
    /// convention.</summary>
    public Guid? ApprovalPolicyId { get; private set; }

    public int? ApprovalPolicyVersion { get; private set; }

    /// <summary>Snapshotted from the pinned policy's <c>AllowSelfApproval</c> at <see cref="Submit"/>
    /// time (H-01 fix pattern) - <see langword="false"/> while no chain is attached.</summary>
    public bool AllowSelfApproval { get; private set; }

    /// <summary>Stamped by every accepted <see cref="Approve"/>/<see cref="Reject"/> vote -
    /// advancing/terminating or not (N-03 fix parity, ADR-0016 §8.2). Exists purely so a
    /// non-advancing quorum vote still dirties this aggregate for EF Core's change tracker, giving
    /// <see cref="RowVersion"/> something to disagree about when two voters race to be "first" on the
    /// same step.</summary>
    public DateTimeOffset? LastVoteAt { get; private set; }

    /// <summary>The resolved approval chain for the current (or most recently voided) revision,
    /// snapshotted at <see cref="Submit"/> time. Never re-derived from <see cref="ApprovalPolicyRule"/>
    /// at decision time (H-01 fix) - the only way the synthesised cumulative-VO-escalation rung
    /// (domain-rules.md §4.1, no real <see cref="ApprovalPolicyRule"/> row behind it) is
    /// representable at all.</summary>
    public IReadOnlyList<VariationOrderApprovalStep> ApprovalSteps => _approvalSteps.AsReadOnly();

    /// <summary>The scope payload - see <see cref="VariationOrderScopeItem"/>'s own remarks on the
    /// Sprint 10 existing-activity-only scope. The sum of every item's <c>BudgetCostDelta</c> always
    /// equals <see cref="Amount"/> exactly (domain-rules.md §5.2), enforced continuously by
    /// <see cref="SetVariationContent"/> being the only writer of either.</summary>
    public IReadOnlyList<VariationOrderScopeItem> ScopeItems => _scopeItems.AsReadOnly();

    public Guid CreatedByUserId { get; private set; }

    public Guid? SubmittedByUserId { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>$t_A$ - the rebaseline boundary (domain-rules.md §1/§5.5). Stamped once, by
    /// <see cref="Approve"/>, the instant <see cref="Status"/> reaches
    /// <see cref="VariationOrderStatus.Approved"/>; never touched again.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>$BAC$ immediately before this approval - written once, by
    /// <see cref="RecordApprovalEffects"/>, immediately after <see cref="Approve"/> reaches
    /// <see cref="VariationOrderStatus.Approved"/> (domain-rules.md §2.4 freeze matrix). Null until
    /// then, forever non-null afterward (an <see cref="Approved"/> VO always has effects recorded -
    /// see that method's idempotency guard).</summary>
    public decimal? BacBefore { get; private set; }

    public decimal? BacAfter { get; private set; }

    public decimal? ContractValueBefore { get; private set; }

    public decimal? ContractValueAfter { get; private set; }

    /// <summary>$\Phi$ at the moment of final approval (domain-rules.md §4.7), for audit/reconstruction
    /// - null if no cumulative-VO-escalation threshold was configured for the pinned policy.</summary>
    public decimal? CumulativeVoPctAtApproval { get; private set; }

    /// <summary>$C^{esc}$ actually used for the §4.7 re-check at final approval - for audit/
    /// reconstruction. Null if no threshold was configured.</summary>
    public decimal? EscalationBasisContractValue { get; private set; }

    /// <summary>SQL Server <c>rowversion</c> optimistic-concurrency token, identical role to
    /// <see cref="PaymentCertificate.RowVersion"/> - two simultaneous approvers race here, the second
    /// gets 409, never a double-advance.</summary>
    public byte[] RowVersion { get; private set; } = [];

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private VariationOrder()
    {
    }

    public VariationOrder(
        Guid tenantId,
        Guid projectId,
        string voNumber,
        Guid createdByUserId,
        decimal amount,
        string? description,
        string? justification,
        int timeImpactDays,
        IReadOnlyList<VariationOrderScopeItemInput> scopeItems)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("VariationOrder.ProjectId is required.");
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new DomainException("VariationOrder.CreatedByUserId is required.");
        }

        TenantId = tenantId;
        ProjectId = projectId;
        VoNumber = ValidateVoNumber(voNumber);
        CreatedByUserId = createdByUserId;
        Status = VariationOrderStatus.Draft;
        RevisionNo = 1;

        ApplyContent(amount, description, justification, timeImpactDays, scopeItems);
    }

    /// <summary>
    /// The single writer of <see cref="Amount"/>/<see cref="Type"/>(derived)/<see cref="Description"/>/
    /// <see cref="Justification"/>/<see cref="TimeImpactDays"/>/<see cref="ScopeItems"/> - refuses
    /// unless <see cref="Status"/> is <see cref="VariationOrderStatus.Draft"/> (domain-rules.md §2.4:
    /// "reproduce [PaymentCertificate's] shape ... with no other writer anywhere in the type"). Used
    /// both for the initial <see cref="Draft"/> content (via the constructor) and to re-price a
    /// returned-for-revision document before resubmission (fixture V-10).
    /// </summary>
    public void SetVariationContent(
        decimal amount,
        string? description,
        string? justification,
        int timeImpactDays,
        IReadOnlyList<VariationOrderScopeItemInput> scopeItems)
    {
        EnsureContentEditable();
        ApplyContent(amount, description, justification, timeImpactDays, scopeItems);
    }

    /// <summary><c>[Draft] --Submit--&gt; [PendingApproval(step 1)]</c> (domain-rules.md §2.3).
    /// Snapshots the resolved chain - exactly as the routing engine produced it for THIS document's
    /// routing amount - plus the policy version pin (H-01 fix pattern), onto the document. An empty
    /// <paramref name="steps"/> mirrors approval-workflow.md §5.3 step 6 as defense-in-depth - the
    /// routing service itself is expected to already have failed with <c>ApprovalPolicyGap</c> before
    /// ever calling this method with zero steps. Guards (a)/(b) of domain-rules.md §2.3's transition
    /// table - required-field validity and $\Delta B_{scope}=A$ - are enforced continuously by
    /// <see cref="SetVariationContent"/> being the sole content writer, so nothing further to check
    /// here; guards (c)/(d) - chain non-empty, baseline configured - are the caller's routing-service
    /// responsibility (an empty/failed routing result must never reach this method at all).</summary>
    public void Submit(
        IReadOnlyList<VariationOrderApprovalStepInput> steps,
        Guid approvalPolicyId,
        int approvalPolicyVersion,
        bool allowSelfApproval,
        Guid submittedByUserId,
        DateTimeOffset submittedAt)
    {
        if (Status != VariationOrderStatus.Draft)
        {
            throw new DomainException($"Cannot submit from status {Status}; must be Draft.");
        }

        if (steps.Count < 1)
        {
            throw new DomainException("Cannot submit with an empty approval chain (ApprovalPolicyGap).");
        }

        if (submittedByUserId == Guid.Empty)
        {
            throw new DomainException("VariationOrder.SubmittedByUserId is required.");
        }

        Status = VariationOrderStatus.PendingApproval;
        CurrentStepNo = 1;
        TotalSteps = steps.Count;
        ApprovalPolicyId = approvalPolicyId;
        ApprovalPolicyVersion = approvalPolicyVersion;
        AllowSelfApproval = allowSelfApproval;
        SubmittedByUserId = submittedByUserId;
        SubmittedAt = submittedAt;

        foreach (var step in steps)
        {
            _approvalSteps.Add(new VariationOrderApprovalStep(
                TenantId, Id, RevisionNo, step.StepNo, step.RequiredRole, step.QuorumCount));
        }
    }

    /// <summary><c>[PendingApproval] --Approve--&gt;</c> same state (<see cref="CurrentStepNo"/> + 1)
    /// or <c>[Approved]</c> (terminal) once the chain is exhausted - identical shape to
    /// <see cref="PaymentCertificate.Approve"/>, see that method's own remarks for the full guard
    /// rationale. Deliberately does <b>not</b> touch <see cref="Project.BAC"/>/
    /// <see cref="Project.ContractValue"/> or record this document's own before/after figures - see
    /// this type's class remarks and <see cref="RecordApprovalEffects"/>. The caller is responsible
    /// for the domain-rules.md §4.7 escalation-bypass re-check <i>before</i> calling this method when
    /// it would be the final, chain-exhausting vote - see <see cref="WouldFinalize"/>.</summary>
    /// <param name="quorumSatisfied">See <see cref="PaymentCertificate.Approve"/>'s identical
    /// parameter.</param>
    public void Approve(
        Guid actorUserId,
        UserRole actorRole,
        UserRole expectedStepRole,
        bool allowSelfApproval,
        DateTimeOffset actedAt,
        bool quorumSatisfied = true)
    {
        if (Status != VariationOrderStatus.PendingApproval)
        {
            throw new DomainException($"Cannot approve from status {Status}; must be PendingApproval.");
        }

        if (!allowSelfApproval && (actorUserId == CreatedByUserId || actorUserId == SubmittedByUserId))
        {
            throw new DomainException("SelfApprovalNotPermitted: the creator/submitter may not approve their own variation order.");
        }

        if (actorRole != expectedStepRole)
        {
            throw new DomainException(
                $"Actor role {actorRole} does not hold the required role {expectedStepRole} for step {CurrentStepNo}.");
        }

        // N-03 parity - see PaymentCertificate.Approve's identical remark.
        LastVoteAt = actedAt;

        if (!quorumSatisfied)
        {
            return;
        }

        if (CurrentStepNo >= TotalSteps)
        {
            Status = VariationOrderStatus.Approved;
            ApprovedAt = actedAt;
        }
        else
        {
            CurrentStepNo += 1;
        }
    }

    /// <summary>
    /// True when a vote satisfying <paramref name="quorumSatisfied"/> would exhaust the chain, i.e.
    /// the call site is about to make <see cref="Approve"/> reach
    /// <see cref="VariationOrderStatus.Approved"/>. The caller (S10-BE-03's
    /// <c>ApproveVariationOrderCommandHandler</c>) uses this <i>before</i> calling <see cref="Approve"/>
    /// to decide whether the domain-rules.md §4.7 escalation-bypass re-check applies - "immediately
    /// before the transition to Approved", never on an intermediate vote.
    /// </summary>
    public bool WouldFinalize(bool quorumSatisfied) => quorumSatisfied && CurrentStepNo >= TotalSteps;

    /// <summary>
    /// Stamps the immutable before/after money figures (domain-rules.md §2.4 freeze matrix) once
    /// <see cref="Approve"/> has actually moved <see cref="Status"/> to
    /// <see cref="VariationOrderStatus.Approved"/> - called by the same command handler, in the same
    /// unit of work, immediately after it has also called <see cref="Project.ApplyVariationOrderApproval"/>
    /// on the separate <see cref="Project"/> aggregate (ADR-0001: this method never reaches for
    /// <see cref="Project"/> itself). Idempotency guard (<see cref="BacBefore"/> already set) mirrors
    /// <see cref="PaymentCertificate.CreateCertificationLedgerEntries"/>'s own "only once" discipline -
    /// belt-and-braces against a caller bug that might otherwise double-stamp on a retried request.
    /// </summary>
    public void RecordApprovalEffects(
        decimal bacBefore,
        decimal bacAfter,
        decimal contractValueBefore,
        decimal contractValueAfter,
        decimal? cumulativeVoPctAtApproval,
        decimal? escalationBasisContractValue)
    {
        if (Status != VariationOrderStatus.Approved || ApprovedAt is null)
        {
            throw new DomainException("Approval effects can only be recorded once the variation order has reached Approved.");
        }

        if (BacBefore is not null)
        {
            throw new DomainException("Approval effects have already been recorded for this variation order.");
        }

        BacBefore = MoneyGuard.EnsureNonNegative(bacBefore, nameof(bacBefore));
        BacAfter = MoneyGuard.EnsureNonNegative(bacAfter, nameof(bacAfter));
        ContractValueBefore = MoneyGuard.EnsureNonNegative(contractValueBefore, nameof(contractValueBefore));
        ContractValueAfter = MoneyGuard.EnsureNonNegative(contractValueAfter, nameof(contractValueAfter));
        CumulativeVoPctAtApproval = cumulativeVoPctAtApproval;
        EscalationBasisContractValue = escalationBasisContractValue;
    }

    /// <summary><c>[PendingApproval] --Reject--&gt; [Rejected]</c> (terminal): only the final step's
    /// approver may reject; quorum-bound identically to <see cref="Approve"/> from day one (ADR-0016 -
    /// ships correctly the first time for the VO, unlike Sprint 9's IPC which needed retrofitting).
    /// See <see cref="PaymentCertificate.Reject"/> for the full guard rationale, reproduced verbatim
    /// here.</summary>
    /// <param name="rejectQuorumSatisfied">See <see cref="PaymentCertificate.Reject"/>'s identical
    /// parameter.</param>
    public void Reject(UserRole actorRole, UserRole expectedFinalStepRole, DateTimeOffset actedAt, bool rejectQuorumSatisfied = true)
    {
        if (Status != VariationOrderStatus.PendingApproval)
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

        LastVoteAt = actedAt;

        if (!rejectQuorumSatisfied)
        {
            return;
        }

        Status = VariationOrderStatus.Rejected;
    }

    /// <summary><c>[PendingApproval] --ReturnForRevision--&gt; [Draft]</c>: bumps
    /// <see cref="RevisionNo"/>, voids every approval collected this revision (the chain snapshot
    /// itself), and unfreezes money/scope fields (domain-rules.md §2.3 - "void every vote collected on
    /// this revision"). Comment-mandatory is enforced by the caller (VO-specific: the shared
    /// <see cref="ApprovalAction"/> constructor already mandates it for
    /// <see cref="ApprovalActionType.ReturnForRevision"/> regardless of document type).</summary>
    public void ReturnForRevision()
    {
        if (Status != VariationOrderStatus.PendingApproval)
        {
            throw new DomainException($"Cannot return for revision from status {Status}; must be PendingApproval.");
        }

        RevisionNo += 1;
        VoidChainSnapshot();
        Status = VariationOrderStatus.Draft;
    }

    /// <summary><c>[PendingApproval] --Withdraw--&gt; [Draft]</c>: only the submitter, and only before
    /// any step has cleared. <see cref="RevisionNo"/> is unchanged (unlike <see cref="ReturnForRevision"/>).
    /// This aggregate-level guard (<see cref="CurrentStepNo"/> &lt;= 1) is necessary but not always
    /// sufficient on its own - domain-rules.md §2.3 additionally requires "zero votes cast on this
    /// revision", which matters when the first step's <c>QuorumCount</c> &gt; 1 (one vote can be cast
    /// without advancing <see cref="CurrentStepNo"/> at all); the caller enforces that half from the
    /// append-only <see cref="ApprovalAction"/> history, the same way it already resolves every other
    /// vote-counting guard external to this aggregate.</summary>
    public void Withdraw(Guid actorUserId)
    {
        if (Status != VariationOrderStatus.PendingApproval)
        {
            throw new DomainException($"Cannot withdraw from status {Status}; must be PendingApproval.");
        }

        if (actorUserId != SubmittedByUserId)
        {
            throw new DomainException("Only the submitter may withdraw this variation order.");
        }

        if (CurrentStepNo > 1)
        {
            throw new DomainException("Cannot withdraw once at least one approval step has cleared.");
        }

        VoidChainSnapshot();
        Status = VariationOrderStatus.Draft;
    }

    /// <summary><c>[Draft] --Cancel--&gt; [Cancelled]</c> (terminal): actor must be the creator or a
    /// PM. Comment-mandatory (VO-specific - "a cancelled instruction is itself evidence",
    /// domain-rules.md §2.3) is enforced by the caller's command validator, not here - mirrors how
    /// <see cref="PaymentCertificate.Cancel"/> itself has no comment parameter to
    /// (redundantly/inconsistently) re-validate.</summary>
    public void Cancel(Guid actorUserId, UserRole actorRole)
    {
        if (Status != VariationOrderStatus.Draft)
        {
            throw new DomainException($"Cannot cancel from status {Status}; must be Draft.");
        }

        if (actorUserId != CreatedByUserId && actorRole != UserRole.PM)
        {
            throw new DomainException("Only the creator or a PM may cancel this variation order.");
        }

        Status = VariationOrderStatus.Cancelled;
    }

    private void ApplyContent(
        decimal amount,
        string? description,
        string? justification,
        int timeImpactDays,
        IReadOnlyList<VariationOrderScopeItemInput> scopeItems)
    {
        if (amount != RoundingRules.ToMoney(amount))
        {
            throw new DomainException("VariationOrder.Amount cannot have more than 2 decimal places.");
        }

        var scopeTotal = scopeItems.Sum(i => i.BudgetCostDelta);
        if (scopeTotal != amount)
        {
            throw new DomainException(
                $"VoScopeBudgetMismatch: the scope payload's net budget delta ({scopeTotal:0.00}) must equal " +
                $"VariationOrder.Amount ({amount:0.00}) exactly (domain-rules.md §5.2).");
        }

        if (amount == 0m && timeImpactDays == 0 && scopeItems.Count == 0)
        {
            throw new DomainException(
                "EmptyVariation: a variation order with zero Amount, zero TimeImpactDays and an empty scope payload carries no effect.");
        }

        Amount = amount;
        Description = description;
        Justification = justification;
        TimeImpactDays = timeImpactDays;

        _scopeItems.Clear();
        foreach (var item in scopeItems)
        {
            _scopeItems.Add(new VariationOrderScopeItem(TenantId, Id, item.ActivityId, item.BudgetCostDelta, item.Note));
        }
    }

    private void EnsureContentEditable()
    {
        if (Status != VariationOrderStatus.Draft)
        {
            throw new DomainException(
                $"Money/scope fields are frozen once a variation order leaves Draft (current status: {Status}) - " +
                "a returned-for-revision document (back in Draft) may be re-priced; nothing else may.");
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
        LastVoteAt = null;
        _approvalSteps.Clear();
    }

    private static string ValidateVoNumber(string voNumber) =>
        string.IsNullOrWhiteSpace(voNumber)
            ? throw new DomainException("VariationOrder.VoNumber is required.")
            : voNumber.Trim();
}
