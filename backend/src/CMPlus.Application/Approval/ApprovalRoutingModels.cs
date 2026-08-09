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
public sealed record ApprovalRoutingRequest(
    ApprovalDocumentType DocumentType,
    Guid? ProjectId,
    decimal Amount,
    DateTimeOffset SubmittedAt,
    IReadOnlyCollection<ApprovalPolicy> CandidatePolicies,
    decimal? ContractValue = null,
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
