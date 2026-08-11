using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S11-BE-03: <see cref="IIssueLogRepository"/> against <see cref="CmPlusDbContext"/>.</summary>
public sealed class IssueLogRepository(CmPlusDbContext dbContext) : IIssueLogRepository
{
    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, cancellationToken);

    public Task<IssueLog?> FindAsync(Guid issueId, CancellationToken cancellationToken = default) =>
        dbContext.IssueLogs.FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

    public async Task<IReadOnlyList<IssueLog>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.IssueLogs
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId)
            .ToListAsync(cancellationToken);

    public void Add(IssueLog issue) => dbContext.IssueLogs.Add(issue);

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
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
    }
}
