using MediatR;

namespace CMPlus.Application.Features.Projects.Queries.GetProjects;

/// <summary>
/// S4-BE-04 (fast-follow, US-4.2): every project in the caller's tenant (<c>GET /api/v1/projects</c>),
/// so a logged-in user can discover which projects exist without already knowing a project id. No
/// parameters - which tenant is answered entirely by <see cref="Abstractions.ITenantProvider"/> via
/// the global EF query filter (ADR-0002), never by a client-supplied filter. Mirrors
/// <c>GetImportJobHistoryQuery</c>'s convention of returning the list directly (not wrapped in
/// <c>Result</c>) - a tenant with zero projects yet is not an error, just an empty list.
/// </summary>
public sealed record GetProjectsQuery : IRequest<IReadOnlyList<ProjectListItemDto>>;
