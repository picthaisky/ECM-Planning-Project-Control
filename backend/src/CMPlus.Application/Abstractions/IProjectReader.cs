using CMPlus.Application.Features.Projects.Queries.GetProjects;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Read-only projection boundary for <see cref="CMPlus.Domain.Entities.Project"/> (S4-BE-04).
/// Deliberately separate from <see cref="IProjectRepository"/> - that interface is scoped to
/// editing one tracked <c>Project</c> instance (see its own remarks); this one only ever returns
/// lean, untracked rows. Named generically (not e.g. <c>IProjectListReader</c>) to leave room for
/// a future <c>GetByIdAsync</c> (the single-project-read gap frontend-developer separately flagged
/// as fast-follow work, `features/info/api.ts`'s <c>getProject</c>) without a rename - out of this
/// gap-closure's scope, but the natural next addition to this same seam.
/// </summary>
public interface IProjectReader
{
    /// <summary>Every project in the caller's tenant, ordered by name. Tenant-scoped by the global
    /// EF query filter (ADR-0002) - no explicit TenantId parameter exists here, mirroring
    /// <see cref="IWbsTreeReader"/>/<see cref="IImportRepository"/>'s convention.</summary>
    Task<IReadOnlyList<ProjectListItemDto>> GetAllAsync(CancellationToken cancellationToken = default);
}
