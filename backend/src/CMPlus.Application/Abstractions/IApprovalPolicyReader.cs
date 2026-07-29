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
}
