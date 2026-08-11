using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Approval;

/// <summary>Well-known <see cref="CMPlus.Domain.Common.Result"/> error codes the WebApi layer
/// maps to specific ProblemDetails responses (S2-BE-03/05).</summary>
public static class ApprovalErrorCodes
{
    /// <summary>The resolved chain is empty - submission must be blocked, never auto-approved
    /// (approval-workflow.md §5.3 step 6). Maps to HTTP 422.</summary>
    public const string PolicyGap = "ApprovalPolicyGap";

    /// <summary>
    /// S10-BE-02 (domain-rules.md §4.6): the pinned policy has a cumulative-VO-escalation threshold
    /// configured (<c>CumulativeVoEscalationPct</c> is not null) but
    /// <see cref="ApprovalRoutingRequest.EscalationBaselineContractValue"/> is missing or
    /// <![CDATA[<=]]> 0 - i.e. the project's baseline contract value is not usable as a denominator.
    /// Submission is blocked. <b>Never divide, and never silently skip the escalation test</b> - the
    /// exact defect <c>ApprovalRoutingService.cs:66</c> shipped with (skipping escalation whenever
    /// <c>ContractValue &lt;= 0</c>, a silent bypass of a governance control on a misconfigured
    /// project). Maps to HTTP 422.
    /// </summary>
    public const string ContractValueNotConfigured = "ContractValueNotConfigured";
}

/// <summary>One rung of a resolved approval chain.</summary>
public sealed record ApprovalChainStep(int StepNo, UserRole RequiredRole, int QuorumCount);

/// <summary>
/// The full result of resolving a document submission against the approval policy engine.
/// <see cref="ApprovalPolicyId"/>/<see cref="ApprovalPolicyVersion"/> are <c>Guid.Empty</c>/<c>0</c>
/// for the "no policy configured at all" fallback chain (approval-workflow.md §5.3 step 6) - there
/// is no real policy row to pin in that case, and <see cref="AllowSelfApproval"/> is
/// <see langword="false"/> (restrictive, not permissive - the same default the fallback role itself
/// uses). The caller snapshots <see cref="Steps"/>/<see cref="AllowSelfApproval"/> onto the document
/// at Submit time (security review sprint-09.md H-01 fix) rather than re-resolving them later.
/// </summary>
public sealed record ApprovalChainResolution(
    decimal RoutingAmount,
    Guid ApprovalPolicyId,
    int ApprovalPolicyVersion,
    IReadOnlyList<ApprovalChainStep> Steps,
    bool EscalationApplied,
    bool AllowSelfApproval);

/// <summary>
/// Input to <see cref="IApprovalRoutingService.Resolve"/>. <see cref="Amount"/> carries the raw,
/// signed Variation Order amount (Add positive / Deduct negative) or the Payment Certificate's
/// gross certified value $G_k$ (already non-negative) - the service itself derives $A^{route}$
/// (approval-workflow.md §5.1), the caller must not pre-apply <c>abs()</c>.
/// <see cref="CandidatePolicies"/> is every policy version the tenant has for this
/// <see cref="DocumentType"/> (any <see cref="ApprovalPolicy.ProjectId"/>, any version) - loaded
/// by <see cref="CMPlus.Application.Abstractions.IApprovalPolicyReader"/>; this service performs
/// the effective-date/active/project-override selection itself, purely in memory.
/// </summary>
/// <param name="EscalationBaselineContractValue">
/// ADR-0015 (Variation Order only; ignored for a Payment Certificate). The denominator of the
/// cumulative-VO-escalation ratio $\Phi = (\Sigma^{VO} + Amount) / \text{this} \times 100$. <b>Must
/// be the project's baseline contract value</b> - <c>Project.EscalationBaselineContractValue</c>
/// (<c>OriginalContractValue ?? ContractValue</c>), fixed at signature and moved only by a formal
/// contract amendment - <b>never</b> <c>Project.ContractValue</c> (the current, VO-inclusive sum).
/// Renamed from the shipped <c>ContractValue</c> specifically to make that distinction impossible to
/// miss at the call site: every approved VO raises <c>Project.ContractValue</c>, so wiring THAT field
/// here makes the escalation trigger recede exactly as the contract drifts furthest from its
/// original scope - a live, self-diluting-denominator defect in the original Sprint 2 code, not a
/// hypothetical one. See domain-rules.md §4.3's worked counterfactual (10.14% vs 9.27% on the same
/// R4 data, depending on which of the two fields is passed here).
/// </param>
/// <param name="CumulativeApprovedVoAmount">
/// $\Sigma^{VO}$: the running total of <b>net-signed</b> (additions less deductions, N-1 -
/// domain-rules.md §4.2) <c>Approved</c> VO amounts for the project within the current reset window
/// (all of them, until a <c>ContractAmendment</c> reset is implemented). <b>Not</b> the count of VOs
/// and <b>not</b> a gross/absolute sum - passing <c>Sum(Math.Abs(vo.Amount))</c> here silently
/// switches the numerator from N-1 to N-2, a different, materially larger, ratio
/// (domain-rules.md §4.2's V-4 fixture: 9.73% vs 13.03% on identical data). The VO currently being
/// submitted is NOT included here - it is added separately via <see cref="Amount"/> inside
/// <see cref="ApprovalRoutingService.Resolve"/>.
/// </param>
public sealed record ApprovalRoutingRequest(
    ApprovalDocumentType DocumentType,
    Guid? ProjectId,
    decimal Amount,
    DateTimeOffset SubmittedAt,
    IReadOnlyCollection<ApprovalPolicy> CandidatePolicies,
    decimal? EscalationBaselineContractValue = null,
    decimal? CumulativeApprovedVoAmount = null);

/// <summary>
/// The single hard-coded step used when a tenant has no policy configured at all for a document
/// type (approval-workflow.md §5.3 step 6: "restrictive, not permissive"). Extracted as a shared
/// constant (S9-BE-05) so <see cref="ApprovalRoutingService"/> (which returns it as the resolved
/// chain at Submit time) and the Payment Certificate approval command handlers (which must
/// re-derive "what role does step N require" for an in-flight document that has no real
/// <see cref="ApprovalPolicyRule"/> row to query, i.e. <c>ApprovalPolicyId == Guid.Empty</c>) can
/// never drift apart on what the fallback chain actually is.
/// </summary>
public static class FallbackApprovalChain
{
    public const int StepNo = 1;

    public const UserRole RequiredRole = UserRole.ProjectDirector;
}
