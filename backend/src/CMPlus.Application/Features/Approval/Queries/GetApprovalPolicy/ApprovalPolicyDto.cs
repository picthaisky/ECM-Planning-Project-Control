using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Approval.Queries.GetApprovalPolicy;

public sealed record ApprovalPolicyRuleDto(
    int StepNo,
    decimal MinAmount,
    decimal? MaxAmount,
    UserRole RequiredRole,
    int QuorumCount);

/// <summary>Shape matches docs/specs/master-plan/design.md §2.2's
/// <c>GET /api/v1/tenants/{tenantId}/approval-policies</c> response.</summary>
public sealed record ApprovalPolicyDto(
    ApprovalDocumentType DocumentType,
    int Version,
    bool IsActive,
    bool AllowSelfApproval,
    decimal? CumulativeVoEscalationPct,
    UserRole? CumulativeVoEscalationRole,
    IReadOnlyList<ApprovalPolicyRuleDto> Rules);
