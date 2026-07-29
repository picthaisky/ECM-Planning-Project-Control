using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// <see cref="IApprovalPolicyReader"/> implementation. Relies entirely on the ambient tenant
/// query filter (ADR-0002) - never filters by TenantId explicitly. Loads full aggregates
/// (policy + rules) with <c>AsNoTracking</c>/<c>AsSplitQuery</c>-free eager loading, since a
/// tenant's rule set is always small (single/low-digit rows per policy version).
/// </summary>
public sealed class ApprovalPolicyReader(CmPlusDbContext dbContext) : IApprovalPolicyReader
{
    public async Task<IReadOnlyList<ApprovalPolicy>> GetCandidatePoliciesAsync(
        ApprovalDocumentType documentType, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        return await dbContext.ApprovalPolicies
            .AsNoTracking()
            .Include(p => p.Rules)
            .Where(p => p.DocumentType == documentType && p.IsActive
                        && p.EffectiveFrom <= asOf
                        && (p.EffectiveTo == null || p.EffectiveTo >= asOf))
            .ToListAsync(cancellationToken);
    }

    public async Task<ApprovalPolicy?> GetActiveTenantDefaultPolicyAsync(
        ApprovalDocumentType documentType, CancellationToken cancellationToken = default)
    {
        return await dbContext.ApprovalPolicies
            .AsNoTracking()
            .Include(p => p.Rules)
            .Where(p => p.DocumentType == documentType && p.ProjectId == null && p.IsActive)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
