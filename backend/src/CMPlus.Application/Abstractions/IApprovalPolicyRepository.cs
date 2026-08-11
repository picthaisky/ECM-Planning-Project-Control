using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Write-side persistence boundary for the Tenant Admin approval-policy editor (S9-BE-06),
/// deliberately separate from the read-only <see cref="IApprovalPolicyReader"/> (S2-BE-05/07) the
/// same way <c>IProjectRepository</c> (write) is separate from <c>IProjectReader</c> (read) - this
/// interface returns a <b>tracked</b> entity because <see cref="ApprovalPolicy.Deactivate"/> is a
/// mutation that must be persisted, which <see cref="IApprovalPolicyReader"/>'s <c>AsNoTracking</c>
/// reads are unsuitable for.
/// </summary>
public interface IApprovalPolicyRepository
{
    /// <summary>Tracked (not <c>AsNoTracking</c>). The tenant-wide (<see cref="ApprovalPolicy.ProjectId"/>
    /// <see langword="null"/>) currently-active policy for <paramref name="documentType"/>, or
    /// <see langword="null"/> if the tenant has none yet (the very first
    /// <c>PUT .../approval-policies/{documentType}</c> for a document type creates
    /// <see cref="ApprovalPolicy.CreateInitialVersion"/> rather than a next version). Tenant-scoped
    /// by the global EF query filter (ADR-0002).</summary>
    Task<ApprovalPolicy?> FindActiveTenantDefaultAsync(
        ApprovalDocumentType documentType, CancellationToken cancellationToken = default);

    /// <summary>Stages a brand-new <see cref="ApprovalPolicy"/> row (either
    /// <see cref="ApprovalPolicy.CreateInitialVersion"/> or the result of
    /// <see cref="ApprovalPolicy.CreateNextVersion"/>) - never an edit of an existing row
    /// (approval-workflow.md §5.2 "version-pin, never mutate").</summary>
    void AddVersion(ApprovalPolicy policy);

    /// <summary>
    /// Persists the staged new version and, when <see cref="FindActiveTenantDefaultAsync"/> returned
    /// a previous version that was subsequently <see cref="ApprovalPolicy.Deactivate"/>d, that
    /// deactivation - both in the same atomic <c>SaveChanges</c> call, so a reader can never
    /// observe two simultaneously-active versions of the same policy (design.md §3's unique
    /// filtered index on <c>(TenantId, ProjectId, DocumentType) WHERE IsActive = 1</c> would reject
    /// exactly that if it were somehow attempted).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
