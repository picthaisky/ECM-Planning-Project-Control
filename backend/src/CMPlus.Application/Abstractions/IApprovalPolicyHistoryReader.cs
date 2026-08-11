using CMPlus.Application.Features.Approval.Queries.GetApprovalPolicyVersionHistory;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Read-only access to a tenant-wide <c>ApprovalPolicy</c>'s full version timeline (S15-BE-01),
/// composed from the existing <c>ApprovalPolicy</c> and <c>AuditLog</c> tables only - the DoD is
/// explicit that no new storage is added for this feature. Deliberately separate from
/// <see cref="IApprovalPolicyReader"/> (which only ever exposes the single currently-applicable
/// policy/policies, never the full history) the same way every other "…Reader" in this codebase is
/// scoped to one read shape.
/// </summary>
public interface IApprovalPolicyHistoryReader
{
    /// <summary>Every version ever created for the tenant-wide (<c>ProjectId</c> null) default policy
    /// of <paramref name="documentType"/>, oldest first, including deactivated (superseded) versions -
    /// <c>ApprovalPolicy.Deactivate</c> never deletes a row (approval-workflow.md §5.2 "version-pin,
    /// never mutate"), so every version that ever existed is still readable. Returns an empty list
    /// (never a failure) when the tenant has not configured this document type yet - tenant-scoped by
    /// the global EF query filter (ADR-0002), so a wrong-tenant request also observes an empty list,
    /// never another tenant's history.</summary>
    Task<IReadOnlyList<ApprovalPolicyVersionHistoryEntryDto>> GetVersionHistoryAsync(
        ApprovalDocumentType documentType, CancellationToken cancellationToken = default);
}
