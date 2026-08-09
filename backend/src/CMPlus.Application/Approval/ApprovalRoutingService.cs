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
///   present) when <c>(CumulativeApprovedVoAmount + Amount) / ContractValue * 100</c> exceeds
///   <see cref="ApprovalPolicy.CumulativeVoEscalationPct"/>. Escalation only ever appends to an
///   already non-empty chain - it never rescues an empty one.</item>
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
        var routingAmount = request.DocumentType == ApprovalDocumentType.VariationOrder
            ? Math.Abs(request.Amount)
            : request.Amount;

        var policy = SelectPolicy(request.CandidatePolicies, request.ProjectId, request.SubmittedAt);

        if (policy is null)
        {
            return Result<ApprovalChainResolution>.Success(new ApprovalChainResolution(
                routingAmount, Guid.Empty, 0, FallbackChain, EscalationApplied: false, AllowSelfApproval: false));
        }

        List<ApprovalChainStep> chain = policy.Rules
            .Where(r => r.MinAmount <= routingAmount && (r.MaxAmount is null || routingAmount < r.MaxAmount))
            .OrderBy(r => r.StepNo)
            .Select(r => new ApprovalChainStep(r.StepNo, r.RequiredRole, r.QuorumCount))
            .ToList();

        var escalationApplied = false;
        if (chain.Count > 0
            && request.DocumentType == ApprovalDocumentType.VariationOrder
            && policy.CumulativeVoEscalationPct is { } thresholdPct
            && policy.CumulativeVoEscalationRole is { } escalationRole
            && request.ContractValue is { } contractValue && contractValue > 0
            && !chain.Any(s => s.RequiredRole == escalationRole))
        {
            var cumulative = (request.CumulativeApprovedVoAmount ?? 0m) + request.Amount;
            var ratioPct = cumulative / contractValue * 100m;

            if (ratioPct > thresholdPct)
            {
                escalationApplied = true;
                chain.Add(new ApprovalChainStep(chain[^1].StepNo + 1, escalationRole, QuorumCount: 1));
            }
        }

        if (chain.Count == 0)
        {
            return Result<ApprovalChainResolution>.Failure(ApprovalErrorCodes.PolicyGap);
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
