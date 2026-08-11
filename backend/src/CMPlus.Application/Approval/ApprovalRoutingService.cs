using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Approval;

/// <summary>
/// Implements the routing algorithm of approval-workflow.md §5.3 exactly:
/// <list type="number">
/// <item>Compute $A^{route}$ - <c>abs(Amount)</c> for a Variation Order, or <c>Amount</c> as-is
///   for a Payment Certificate (the caller already supplies $G_k$, which is non-negative).</item>
/// <item>Select the applicable policy: project-scoped override if one is active/effective for
///   <see cref="ApprovalRoutingRequest.ProjectId"/>, else the tenant-wide default
///   (<see cref="ApprovalPolicy.ProjectId"/> null) - both filtered to active and within
///   <see cref="ApprovalPolicy.EffectiveFrom"/>/<see cref="ApprovalPolicy.EffectiveTo"/> as of
///   <see cref="ApprovalRoutingRequest.SubmittedAt"/>.</item>
/// <item>Select every rule where <c>MinAmount &lt;= A^route &lt; MaxAmount</c> (or MaxAmount is
///   null).</item>
/// <item>Order the selected rules by <c>StepNo</c> to form the chain.</item>
/// <item>Variation Order only: apply cumulative-VO escalation - append
///   <see cref="ApprovalPolicy.CumulativeVoEscalationRole"/> as a final step (if not already
///   present) when <c>(CumulativeApprovedVoAmount + Amount) / EscalationBaselineContractValue * 100</c>
///   exceeds <see cref="ApprovalPolicy.CumulativeVoEscalationPct"/> (ADR-0015: the denominator is the
///   project's <i>baseline</i> contract value, never the current/VO-inclusive one - see
///   <see cref="ApprovalRoutingRequest.EscalationBaselineContractValue"/>'s own remarks). Escalation
///   only ever appends to an already non-empty chain - it never rescues an empty one.</item>
/// <item>Fail closed: an empty resolved chain returns
///   <see cref="Result{T}.Failure"/>(<see cref="ApprovalErrorCodes.PolicyGap"/>) - never an
///   auto-approve.</item>
/// <item>If no policy exists for the tenant/document type at all, fall back to a single
///   mandatory <see cref="UserRole.ProjectDirector"/> step - restrictive, not permissive.</item>
/// </list>
/// Rule-band non-overlap/gap-free validation (approval-workflow.md §5.3 step 7) is enforced by
/// <see cref="ApprovalPolicy"/> itself at construction/versioning time, not here. Re-resolution on
/// resubmission (step 8) needs no special handling - calling <see cref="Resolve"/> again with the
/// revised amount is exactly the re-resolution the algorithm calls for.
/// </summary>
public sealed class ApprovalRoutingService : IApprovalRoutingService
{
    private static readonly IReadOnlyList<ApprovalChainStep> FallbackChain =
        [new ApprovalChainStep(FallbackApprovalChain.StepNo, FallbackApprovalChain.RequiredRole, QuorumCount: 1)];

    public Result<ApprovalChainResolution> Resolve(ApprovalRoutingRequest request)
    {
        var routingAmount = ComputeRoutingAmount(request);

        var policy = SelectPolicy(request.CandidatePolicies, request.ProjectId, request.SubmittedAt);

        if (policy is null)
        {
            return Result<ApprovalChainResolution>.Success(new ApprovalChainResolution(
                routingAmount, Guid.Empty, 0, FallbackChain, EscalationApplied: false, AllowSelfApproval: false));
        }

        return ResolveForPolicy(policy, request, routingAmount);
    }

    /// <summary>H-01 fix - see the interface's own remarks for the full rationale. No
    /// <see cref="SelectPolicy"/> call here, deliberately: <paramref name="pinnedPolicy"/> has already
    /// been chosen by the caller, so there is nothing to select and nothing for an
    /// <c>IsActive</c>/effective-window predicate to reject - the one and only reason the fallback
    /// chain could ever silently substitute for a real, already-in-flight policy.</summary>
    public Result<ApprovalChainResolution> ResolvePinned(ApprovalPolicy pinnedPolicy, ApprovalRoutingRequest request)
    {
        ArgumentNullException.ThrowIfNull(pinnedPolicy);

        return ResolveForPolicy(pinnedPolicy, request, ComputeRoutingAmount(request));
    }

    private static decimal ComputeRoutingAmount(ApprovalRoutingRequest request) =>
        request.DocumentType == ApprovalDocumentType.VariationOrder
            ? Math.Abs(request.Amount)
            : request.Amount;

    /// <summary>The band + cumulative-VO-escalation logic (approval-workflow.md §5.3 steps 3-5),
    /// shared verbatim by <see cref="Resolve"/> (policy chosen by selection) and
    /// <see cref="ResolvePinned"/> (policy chosen by the caller already) - H-01's fix reuses this one
    /// implementation rather than let the two entry points risk drifting apart on how a resolved
    /// policy becomes a chain.</summary>
    private static Result<ApprovalChainResolution> ResolveForPolicy(
        ApprovalPolicy policy, ApprovalRoutingRequest request, decimal routingAmount)
    {
        List<ApprovalChainStep> chain = policy.Rules
            .Where(r => r.MinAmount <= routingAmount && (r.MaxAmount is null || routingAmount < r.MaxAmount))
            .OrderBy(r => r.StepNo)
            .Select(r => new ApprovalChainStep(r.StepNo, r.RequiredRole, r.QuorumCount))
            .ToList();

        if (chain.Count == 0)
        {
            return Result<ApprovalChainResolution>.Failure(ApprovalErrorCodes.PolicyGap);
        }

        var escalationApplied = false;
        if (request.DocumentType == ApprovalDocumentType.VariationOrder
            && policy.CumulativeVoEscalationPct is { } thresholdPct)
        {
            // domain-rules.md §4.6: threshold configured -> the baseline must be known and positive.
            // Fail closed (422 ContractValueNotConfigured) rather than the old ApprovalRoutingService.cs:66
            // behaviour of silently skipping the whole escalation test whenever ContractValue <= 0 -
            // that was a silent bypass of a governance control on a misconfigured project, not a
            // graceful degradation. Checked unconditionally whenever a threshold is configured (not
            // only when the escalation role would otherwise be appended) - domain-rules.md §4.1's own
            // formula computes Phi first, and only then checks whether the role is already present;
            // "the escalation role happens to already be in the banded chain" must not be allowed to
            // paper over "this project's baseline is not usable", which is a data-quality problem in
            // its own right.
            if (request.EscalationBaselineContractValue is not { } baselineContractValue || baselineContractValue <= 0m)
            {
                return Result<ApprovalChainResolution>.Failure(ApprovalErrorCodes.ContractValueNotConfigured);
            }

            if (policy.CumulativeVoEscalationRole is { } escalationRole && !chain.Any(s => s.RequiredRole == escalationRole))
            {
                // ADR-0015: numerator is net-signed (N-1, domain-rules.md §4.2) - request.Amount is the
                // raw signed amount (never abs()'d here), and CumulativeApprovedVoAmount is documented
                // (ApprovalRoutingModels.cs) as the caller's own net-signed running total. Denominator is
                // EscalationBaselineContractValue, never Project.ContractValue - see that parameter's own
                // remarks for why using the current (VO-inclusive) contract value here would silently
                // reintroduce the self-diluting-denominator defect this ADR fixes. Compared at full
                // decimal precision (domain-rules.md §4.6, fixture V-5b) - rounding before the `>`
                // comparison would let a Phi that displays as exactly the threshold wrongly escalate.
                var cumulative = (request.CumulativeApprovedVoAmount ?? 0m) + request.Amount;
                var ratioPct = cumulative / baselineContractValue * 100m;

                if (ratioPct > thresholdPct)
                {
                    escalationApplied = true;
                    chain.Add(new ApprovalChainStep(chain[^1].StepNo + 1, escalationRole, QuorumCount: 1));
                }
            }
        }

        return Result<ApprovalChainResolution>.Success(new ApprovalChainResolution(
            routingAmount, policy.Id, policy.Version, chain, escalationApplied, policy.AllowSelfApproval));
    }

    private static ApprovalPolicy? SelectPolicy(
        IReadOnlyCollection<ApprovalPolicy> candidates, Guid? projectId, DateTimeOffset submittedAt)
    {
        var effective = candidates.Where(p =>
            p.IsActive
            && p.EffectiveFrom <= submittedAt
            && (p.EffectiveTo is null || p.EffectiveTo >= submittedAt))
            .ToList();

        if (projectId is { } id)
        {
            var projectOverride = effective.FirstOrDefault(p => p.ProjectId == id);
            if (projectOverride is not null)
            {
                return projectOverride;
            }
        }

        return effective.FirstOrDefault(p => p.ProjectId is null);
    }
}
