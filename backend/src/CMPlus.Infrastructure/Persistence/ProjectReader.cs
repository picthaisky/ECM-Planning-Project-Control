using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Projects.Queries.GetProjects;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S4-BE-04: <see cref="IProjectReader"/> against <see cref="CmPlusDbContext"/>.</summary>
public sealed class ProjectReader(CmPlusDbContext dbContext) : IProjectReader
{
    public async Task<IReadOnlyList<ProjectListItemDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProjectListItemDto(p.Id, p.Name, p.Code, p.Owner, p.ContractStart, p.ContractFinish))
            .ToListAsync(cancellationToken);
}
