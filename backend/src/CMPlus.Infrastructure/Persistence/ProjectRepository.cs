using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S4-BE-02: <see cref="IProjectRepository"/> against <see cref="CmPlusDbContext"/>.</summary>
public sealed class ProjectRepository(CmPlusDbContext dbContext) : IProjectRepository
{
    public Task<Project?> FindAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
