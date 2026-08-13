using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Projects.Queries.GetProject;
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

    public Task<ProjectDetailDto?> GetDetailByIdAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new ProjectDetailDto(
                p.Id, p.Name, p.Code, p.Owner, p.ContractStart, p.ContractFinish, p.BAC, p.ContractValue,
                p.RetentionRate, p.AdvanceRate, p.RetentionCapPercentage, p.RetentionRelease1Percentage,
                p.DefectsLiabilityMonths, p.AdvanceAmountPaid, p.AdvanceRecoveryMethod,
                p.AdvanceRecoveryStartPct, p.AdvanceRecoveryRatePct, p.AdvanceRecoveryEndPct,
                p.EacVariantDefault, p.EacManualEtc, p.EacCustomPerformanceFactor, p.EacManualEtcStaleSince))
            .SingleOrDefaultAsync(cancellationToken);
}
