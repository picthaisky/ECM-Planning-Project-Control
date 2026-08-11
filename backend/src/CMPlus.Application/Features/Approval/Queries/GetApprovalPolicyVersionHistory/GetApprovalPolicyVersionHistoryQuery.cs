using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Approval.Queries.GetApprovalPolicyVersionHistory;

/// <summary>
/// S15-BE-01: <c>GET /api/v1/tenants/{tenantId}/approval-policies/{documentType}/history</c> -
/// "ประวัติเวอร์ชันดูจาก AuditLog + ApprovalPolicy.Version โดยไม่เพิ่มที่เก็บใหม่" (version history read
/// from AuditLog + ApprovalPolicy.Version, no new storage). Scoped to the tenant-wide default policy
/// only, mirroring <c>GetApprovalPolicyQuery</c>'s own scope (project-scoped override is
/// schema-present but not surfaced anywhere in this codebase's write path yet, per ADR-0008's
/// consequences) - tenant scoping happens entirely via the ambient <c>ITenantProvider</c>/EF global
/// query filter (ADR-0002).
/// </summary>
public sealed record GetApprovalPolicyVersionHistoryQuery(ApprovalDocumentType DocumentType)
    : IRequest<Result<IReadOnlyList<ApprovalPolicyVersionHistoryEntryDto>>>;
