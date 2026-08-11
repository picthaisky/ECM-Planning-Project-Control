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

    /// <summary>See <see cref="IApprovalPolicyRepository.SaveChangesAsync"/>'s remarks for the
    /// concurrent-request race this guards against - the same shape, and the same
    /// <c>UniqueIndexViolationClassifier</c>, <see cref="BaselineRepository.TryActivateAsync"/> uses.
    /// Deliberately no transaction wrapper here (unlike that method): this is always exactly one
    /// <c>SaveChangesAsync</c> call flushing one batch (<c>current.Deactivate()</c> + the new
    /// <c>Added</c> version, staged by the caller before calling this), so there is nothing to split
    /// across two round trips the way Baseline's activate/deactivate pair needed - a single
    /// <c>SaveChangesAsync</c> is already atomic on its own.</summary>
    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException ex) when (UniqueIndexViolationClassifier.IsUniqueConstraintViolation(ex))
        {
            return false;
        }
    }
}
