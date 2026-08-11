using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.WebApi.Controllers.Approval;

/// <summary>Request body for <c>PUT /api/v1/tenants/{tenantId}/approval-policies/{documentType}</c>
/// (S9-BE-06, design.md §2.2).</summary>
public sealed record UpdateApprovalPolicyRuleRequest(
    int StepNo, decimal MinAmount, decimal? MaxAmount, UserRole RequiredRole, int QuorumCount = 1)
{
    public ApprovalPolicyRuleInput ToDomainInput() => new(StepNo, MinAmount, MaxAmount, RequiredRole, RequiredUserId: null, QuorumCount);
}

public sealed record UpdateApprovalPolicyRequest(
    bool AllowSelfApproval,
    decimal? CumulativeVoEscalationPct,
    UserRole? CumulativeVoEscalationRole,
    IReadOnlyList<UpdateApprovalPolicyRuleRequest> Rules);
