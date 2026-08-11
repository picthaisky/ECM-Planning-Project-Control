namespace CMPlus.Application.Features.VariationOrder;

/// <summary>
/// Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the S10-BE-01/02/03 Variation
/// Order commands - mirrors <c>CMPlus.Application.Features.Payment.PaymentApprovalErrorCodes</c>'s
/// shape exactly, including reusing <c>CMPlus.Application.Approval.ApprovalErrorCodes.PolicyGap</c>/
/// <c>ContractValueNotConfigured</c> as-is (both already mapped by <c>ResultProblemMapper</c>) rather
/// than duplicating them.
/// </summary>
public static class VariationOrderErrorCodes
{
    /// <summary>No <see cref="CMPlus.Domain.Entities.VariationOrder"/> with this id exists in the
    /// caller's tenant (or at all) - the global EF query filter (ADR-0002) makes "wrong tenant" and
    /// "does not exist" indistinguishable.</summary>
    public const string NotFound = "VariationOrderNotFound";

    public const string ProjectNotFound = "VariationOrderProjectNotFound";

    /// <summary>L-01 (domain-rules.md §7.5): "actor identity must never be fabricated" - the caller
    /// has no resolvable user id (<c>ICurrentUserContext.UserId</c> is null). Structurally
    /// unreachable behind <c>[Authorize]</c> in production; kept as a fail-closed guard rather than
    /// ever stamping <c>Guid.Empty</c> onto an actor field. Maps to 401.</summary>
    public const string ActorRequired = "VariationOrderActorRequired";

    /// <summary><see cref="CMPlus.Domain.Entities.VariationOrder.VoNumber"/> is already used by
    /// another variation order in this project (domain-rules.md §7.1: unique per
    /// <c>(TenantId, ProjectId)</c>).</summary>
    public const string DuplicateVoNumber = "VariationOrderDuplicateVoNumber";

    /// <summary>One or more <c>ActivityId</c> values in the scope payload do not belong to this
    /// project (or do not exist).</summary>
    public const string UnknownActivity = "VariationOrderUnknownActivity";

    /// <summary>domain-rules.md §5.2: the scope payload's net budget delta does not equal
    /// <see cref="CMPlus.Domain.Entities.VariationOrder.Amount"/> exactly. Maps to 422.</summary>
    public const string ScopeBudgetMismatch = "VoScopeBudgetMismatch";

    /// <summary>domain-rules.md §7.3: a variation with zero Amount, zero TimeImpactDays and an empty
    /// scope payload carries no effect at all. Maps to 422.</summary>
    public const string EmptyVariation = "EmptyVariation";

    /// <summary>The document's current <c>Status</c> does not allow the requested transition. Maps to
    /// <c>409 document-immutable</c>, matching <c>PaymentApprovalErrorCodes.InvalidStatusForTransition</c>'s
    /// mapping.</summary>
    public const string InvalidStatusForTransition = "VariationOrderInvalidStatusForTransition";

    /// <summary>Reused 403 shape - see <c>PaymentApprovalErrorCodes.NotAuthorizedForApprovalStep</c>'s
    /// identical remarks; never a static role escape hatch, always resolved from the document's own
    /// version-pinned chain.</summary>
    public const string NotAuthorizedForApprovalStep = "VariationOrderNotAuthorizedForApprovalStep";

    public const string SelfApprovalNotPermitted = "VariationOrderSelfApprovalNotPermitted";

    /// <summary>ADR-0016 / domain-rules.md §8.3: an actor who already voted (Approve OR Reject) on
    /// this revision may not vote again, on any step.</summary>
    public const string DuplicateChainVoter = "VariationOrderDuplicateChainVoter";

    public const string ConcurrencyConflict = "VariationOrderConcurrencyConflict";

    /// <summary>security review sprint-09.md M-03 shape: a corrupt/legacy chain snapshot degrades to
    /// a clear 409 instead of an unhandled 500.</summary>
    public const string CorruptApprovalChain = "VariationOrderCorruptApprovalChain";

    /// <summary>domain-rules.md §2.3 Withdraw guard: the submitter, but at least one distinct vote has
    /// already been cast this revision (relevant when the first step's <c>QuorumCount</c> &gt; 1, where
    /// <c>CurrentStepNo</c> alone does not prove "zero votes cast"). Maps to 409, same bucket as
    /// <see cref="InvalidStatusForTransition"/>.</summary>
    public const string WithdrawAfterVoteCast = "VariationOrderWithdrawAfterVoteCast";

    /// <summary>domain-rules.md §5.1's Guard: a Deduct VO large enough to drive
    /// <c>Project.BAC</c>/<c>Project.ContractValue</c> below zero must fail the approval, not clamp
    /// silently. Maps to 422.</summary>
    public const string WouldMakeContractValueNegative = "VoWouldMakeContractValueNegative";

    /// <summary>domain-rules.md §5.2 "Omission of already-executed work": a negative
    /// <c>BudgetCostDelta</c> targets an activity with <c>ProgressPct &gt; 0</c> - blocked at approval
    /// (warned only at Draft, per the same source). Maps to 422.</summary>
    public const string OmitsExecutedScope = "VoOmitsExecutedScope";

    /// <summary>A scope item's delta would drive that individual activity's own
    /// <c>Activity.BudgetCost</c> below zero (a stricter per-line consequence of
    /// <c>Activity.BudgetCost</c> being <c>EnsureNonNegative</c>, domain-rules.md §5.2). Maps to 422.
    /// </summary>
    public const string ScopeActivityBudgetWouldBeNegative = "VoScopeActivityBudgetWouldBeNegative";

    /// <summary>
    /// domain-rules.md §4.7 - the escalation-bypass guard. Re-checked as a guard on the final
    /// <c>Approve</c> that would exhaust the chain: if the cumulative-VO ratio has crossed the
    /// configured threshold since submission and the escalation role is not already present in this
    /// document's snapshotted chain, the approval is blocked (fail closed) rather than silently
    /// approved with no Executive signature anywhere. The remedy is <c>ReturnForRevision</c> then
    /// resubmit, which re-resolves the chain and picks up the escalation role - this guard never
    /// appends the missing step to the already-pinned chain itself (the H-01/N-01 bug class). Maps to
    /// 409.
    /// </summary>
    public const string EscalationThresholdCrossedSinceSubmission = "VoEscalationThresholdCrossedSinceSubmission";
}
