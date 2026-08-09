using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S9-BE-05: <see cref="IApprovalActionRepository"/> against <see cref="CmPlusDbContext"/>.
/// Document-type-agnostic, ready for Sprint 10's <c>VariationOrder</c> handlers to reuse
/// unchanged.</summary>
public sealed class ApprovalActionRepository(CmPlusDbContext dbContext) : IApprovalActionRepository
{
    public void Add(ApprovalAction action) => dbContext.ApprovalActions.Add(action);

    public async Task<IReadOnlyList<ApprovalAction>> GetHistoryAsync(
        ApprovalDocumentType documentType, Guid documentId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ApprovalActions
            .AsNoTracking()
            .Where(a => a.DocumentType == documentType && a.DocumentId == documentId)
            .OrderBy(a => a.ActedAt)
            .ToListAsync(cancellationToken);
    }
}
