using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Read-only access to <see cref="ApprovalPolicy"/> rows (S2-BE-05/07). Tenant-scoped
/// automatically via the ambient <see cref="ITenantProvider"/>/EF global query filter (ADR-0002) -
/// callers never pass a TenantId explicitly. This is the "loading" half of policy resolution;
/// the actual routing algorithm is the pure, EF-free <c>IApprovalRoutingService</c>, which
/// receives whatever this reader returns as plain in-memory <see cref="ApprovalPolicy"/> objects.
/// </summary>
public interface IApprovalPolicyReader
{
    /// <summary>All policy versions for the tenant (any <see cref="ApprovalPolicy.ProjectId"/>,
    /// any version) that could plausibly apply to <paramref name="documentType"/> - i.e. active
    /// and within their effective window. <see cref="CMPlus.Application.Approval.IApprovalRoutingService"/>
    /// picks the single applicable one (project override over tenant default) from this set.</summary>
    Task<IReadOnlyList<ApprovalPolicy>> GetCandidatePoliciesAsync(
        ApprovalDocumentType documentType,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    /// <summary>The current tenant-wide default (<see cref="ApprovalPolicy.ProjectId"/> null,
    /// <see cref="ApprovalPolicy.IsActive"/> true) policy for <paramref name="documentType"/>, or
    /// <c>null</c> if the tenant has none configured yet (S2-BE-07's read endpoint).</summary>
    Task<ApprovalPolicy?> GetActiveTenantDefaultPolicyAsync(
        ApprovalDocumentType documentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches one specific policy <b>version</b> by <see cref="CMPlus.Domain.Common.Entity.Id"/>,
    /// regardless of <see cref="ApprovalPolicy.IsActive"/>/<see cref="ApprovalPolicy.EffectiveTo"/>
    /// (S9-BE-05). This is the concrete mechanism that makes version-pinning
    /// (ADR-0008/approval-workflow.md §5.2) actually protect an in-flight document: a
    /// <c>PaymentCertificate</c>/<c>VariationOrder</c> pins <c>ApprovalPolicyId</c> at Submit time,
    /// and re-deriving "what role does chain step N require" while the document is
    /// <c>PendingApproval</c> must always resolve against that exact pinned version - never the
    /// tenant's currently-active one, which a S9-BE-06 policy edit may have since superseded
    /// (deactivated, but never deleted: <see cref="ApprovalPolicy.Deactivate"/> only flips
    /// <see cref="ApprovalPolicy.IsActive"/>/stamps <see cref="ApprovalPolicy.EffectiveTo"/>, so the
    /// row - and this lookup - remains valid for as long as any document still references it).
    /// <see cref="GetActiveTenantDefaultPolicyAsync"/> is deliberately unsuitable for this: it
    /// filters to <c>IsActive</c>, so it would silently start returning the *new* version's rules
    /// for an already-in-flight document the moment an admin edits the policy.
    /// </summary>
    Task<ApprovalPolicy?> GetByIdAsync(
        Guid approvalPolicyId,
        CancellationToken cancellationToken = default);
}
