using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S12-BE-01: <see cref="IPhotoRepository"/> against <see cref="CmPlusDbContext"/>.</summary>
public sealed class PhotoRepository(CmPlusDbContext dbContext) : IPhotoRepository
{
    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, cancellationToken);

    public Task<bool> ActivityExistsAsync(Guid projectId, Guid activityId, CancellationToken cancellationToken = default) =>
        dbContext.Activities.AsNoTracking()
            .AnyAsync(a => a.Id == activityId && dbContext.WBSNodes.Any(n => n.Id == a.WbsNodeId && n.ProjectId == projectId), cancellationToken);

    public void Add(Photo photo) => dbContext.Photos.Add(photo);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public Task<Photo?> FindAsync(Guid photoId, CancellationToken cancellationToken = default) =>
        dbContext.Photos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == photoId, cancellationToken);
}
