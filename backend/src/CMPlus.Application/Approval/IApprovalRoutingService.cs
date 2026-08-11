using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;

namespace CMPlus.Application.Approval;

/// <summary>
/// The pure, fail-closed amount-tiered approval routing algorithm (ADR-0008, approval-workflow.md
/// §5.3). Deliberately takes only in-memory Domain objects/primitives and returns a
/// <see cref="Result{T}"/> - no EF Core, no I/O, fully unit-testable against the R1-R10 fixtures
/// with no database.
/// </summary>
public interface IApprovalRoutingService
{
    /// <summary>Selects a policy from <see cref="ApprovalRoutingRequest.CandidatePolicies"/> (project
    /// override over tenant default, both filtered to <c>IsActive</c>/effective-window) and resolves
    /// the band/escalation chain against it. This is the entry point for an as-yet-unrouted document
    /// (Submit) - the caller has not chosen a policy yet, and is asking "which one applies, and what
    /// chain does it produce". Never call this to re-check a document that has already pinned a
    /// specific policy version - use <see cref="ResolvePinned"/> instead (sprint-10 security review
    /// H-01).</summary>
    Result<ApprovalChainResolution> Resolve(ApprovalRoutingRequest request);

    /// <summary>
    /// H-01 fix (docs/security/reviews/sprint-10.md): re-resolves the band/escalation logic against a
    /// policy version that the caller has ALREADY selected and pinned onto a document - e.g. the
    /// domain-rules.md §4.7 escalation-bypass guard, which re-checks Phi at the final approving step
    /// against the SAME <c>ApprovalPolicyId</c>/<c>Version</c> that routed the document at Submit
    /// time, fetched by id via <see cref="CMPlus.Application.Abstractions.IApprovalPolicyReader.GetByIdAsync"/>.
    ///
    /// <para>Deliberately skips <see cref="Resolve"/>'s policy-selection step (the
    /// <c>IsActive</c>/<c>EffectiveFrom</c>/<c>EffectiveTo</c> predicate) entirely, because that
    /// predicate answers "which policy applies right now" - a question that does not apply to a
    /// policy the caller has already chosen. Before this method existed, re-checking a pinned policy
    /// through <see cref="Resolve"/> (passing it as the sole candidate) re-applied that predicate
    /// anyway; the moment an ordinary policy edit deactivated the pinned version (every edit
    /// deactivates the previous one - <c>UpdateApprovalPolicyCommandHandler</c>), selection found
    /// nothing and <see cref="Resolve"/> silently substituted the permissive, no-<c>Executive</c>
    /// <see cref="FallbackApprovalChain"/> - a routine administrative act re-opening exactly the
    /// escalation bypass the guard exists to prevent. This method removes that failure category
    /// entirely rather than patching the one call site: there is no selection step here to
    /// mis-resolve, so a deactivated pinned policy can never be silently swapped for the fallback.
    /// </para>
    ///
    /// <para><paramref name="pinnedPolicy"/> must be non-null - by construction, the caller already
    /// has a specific policy in hand (there is no "no policy configured" case to consider here; that
    /// is exactly what <see cref="Resolve"/>'s <see cref="FallbackApprovalChain"/> branch is for).
    /// <see cref="ApprovalRoutingRequest.CandidatePolicies"/> is ignored by this method.</para>
    /// </summary>
    Result<ApprovalChainResolution> ResolvePinned(ApprovalPolicy pinnedPolicy, ApprovalRoutingRequest request);
}
