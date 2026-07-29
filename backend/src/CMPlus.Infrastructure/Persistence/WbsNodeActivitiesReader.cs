using CMPlus.Application.Abstractions;
using CMPlus.Application.Wbs;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S4-BE-05: <see cref="IWbsNodeActivitiesReader"/> against <see cref="CmPlusDbContext"/>.</summary>
public sealed class WbsNodeActivitiesReader(CmPlusDbContext dbContext) : IWbsNodeActivitiesReader
{
    public Task<bool> NodeExistsInProjectAsync(Guid projectId, Guid wbsNodeId, CancellationToken cancellationToken = default) =>
        dbContext.WBSNodes.AsNoTracking().AnyAsync(n => n.Id == wbsNodeId && n.ProjectId == projectId, cancellationToken);

    public async Task<IReadOnlyList<ActivityForProgressDto>> GetActivitiesForNodeAsync(
        Guid wbsNodeId, CancellationToken cancellationToken = default) =>
        await dbContext.Activities
            .AsNoTracking()
            .Where(a => a.WbsNodeId == wbsNodeId)
            .OrderBy(a => a.ActivityCode)
            .Select(a => new ActivityForProgressDto(a.Id, a.ActivityCode, a.Name, a.ProgressPercentage))
            .ToListAsync(cancellationToken);
}
