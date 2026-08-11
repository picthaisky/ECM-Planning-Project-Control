using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S4-BE-02: <see cref="IProjectRepository"/> against <see cref="CmPlusDbContext"/>.</summary>
public sealed class ProjectRepository(CmPlusDbContext dbContext) : IProjectRepository
{
    public Task<Project?> FindAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

    /// <summary>Catches only <see cref="DbUpdateConcurrencyException"/> - a constraint violation or
    /// any other <c>DbUpdateException</c> must still surface rather than be silently reported as a
    /// concurrency conflict (the same discipline `PaymentCertificateRepository` follows).</summary>
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
