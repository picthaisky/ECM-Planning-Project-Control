using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Approval.Queries.GetApprovalActionHistory;

/// <summary>
/// Wire shape for one row of the append-only <see cref="ApprovalAction"/> ledger (design.md §2.3:
/// `GET /api/v1/{…}/{id}/approval-actions`; S9 read-side gap closure, finding L-04) - transcribed
/// field-for-field from the entity. Document-type-agnostic, exactly like the entity itself and
/// <see cref="CMPlus.Application.Abstractions.IApprovalActionRepository"/> - Sprint 10's
/// VariationOrder history reuses this DTO (and the query/handler below) unchanged, per ADR-0008's
/// staging note that Sprint 10 "carries no rework" from Sprint 9.
/// </summary>
public sealed record ApprovalActionDto(
    Guid Id,
    ApprovalDocumentType DocumentType,
    Guid DocumentId,
    int RevisionNo,
    int StepNo,
    Guid ActorUserId,
    UserRole ActorRoleAtTime,
    ApprovalActionType Action,
    string? Comment,
    DateTimeOffset ActedAt,
    Guid ApprovalPolicyId,
    int ApprovalPolicyVersion)
{
    public static ApprovalActionDto From(ApprovalAction action) => new(
        action.Id,
        action.DocumentType,
        action.DocumentId,
        action.RevisionNo,
        action.StepNo,
        action.ActorUserId,
        action.ActorRoleAtTime,
        action.Action,
        action.Comment,
        action.ActedAt,
        action.ApprovalPolicyId,
        action.ApprovalPolicyVersion);
}
