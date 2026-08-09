using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S9-BE-06: <see cref="IApprovalPolicyRepository"/> against <see cref="CmPlusDbContext"/> -
/// the write-side counterpart to the read-only <see cref="ApprovalPolicyReader"/>.</summary>
public sealed class ApprovalPolicyRepository(CmPlusDbContext dbContext) : IApprovalPolicyRepository
{
    public Task<ApprovalPolicy?> FindActiveTenantDefaultAsync(
        ApprovalDocumentType documentType, CancellationToken cancellationToken = default)
    {
        return dbContext.ApprovalPolicies
            .Include(p => p.Rules)
            .Where(p => p.DocumentType == documentType && p.ProjectId == null && p.IsActive)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void AddVersion(ApprovalPolicy policy) => dbContext.ApprovalPolicies.Add(policy);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
