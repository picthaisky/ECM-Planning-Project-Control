using CMPlus.Application.Features.Projects.Queries.GetProject;
using CMPlus.Application.Features.Projects.Queries.GetProjects;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Read-only projection boundary for <see cref="CMPlus.Domain.Entities.Project"/> (S4-BE-04).
/// Deliberately separate from <see cref="IProjectRepository"/> - that interface is scoped to
/// editing one tracked <c>Project</c> instance (see its own remarks); this one only ever returns
/// lean, untracked rows. Named generically (not e.g. <c>IProjectListReader</c>) precisely so the
/// single-project read (<see cref="GetDetailByIdAsync"/>, which closed the <c>features/info/api.ts</c>
/// <c>getProject</c> gap) could be added here without a rename.
/// </summary>
public interface IProjectReader
{
    /// <summary>Every project in the caller's tenant, ordered by name. Tenant-scoped by the global
    /// EF query filter (ADR-0002) - no explicit TenantId parameter exists here, mirroring
    /// <see cref="IWbsTreeReader"/>/<see cref="IImportRepository"/>'s convention.</summary>
    Task<IReadOnlyList<ProjectListItemDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>One project's full detail (the editable field set plus the ADR-0007(d) EAC config),
    /// or <see langword="null"/> if no project with this id exists in the caller's tenant - the global
    /// EF query filter (ADR-0002) makes "wrong tenant" and "does not exist" indistinguishable. The
    /// single-project read this interface's remarks anticipated (<c>features/info/api.ts#getProject</c>).
    /// </summary>
    Task<ProjectDetailDto?> GetDetailByIdAsync(Guid projectId, CancellationToken cancellationToken = default);
}
