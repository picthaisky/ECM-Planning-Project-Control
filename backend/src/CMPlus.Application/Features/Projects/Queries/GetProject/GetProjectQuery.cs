using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Projects.Queries.GetProject;

/// <summary>
/// <c>GET /api/v1/projects/{projectId}</c> (US-4.3/4.4) - one project's full detail for the Project
/// Info screen's "view" half. Tenant-scoped by the global query filter (ADR-0002); an unknown or
/// cross-tenant id fails with <c>ProjectErrorCodes.NotFound</c> (404, indistinguishable - the IDOR
/// discipline every other id-scoped read here follows).
/// </summary>
public sealed record GetProjectQuery(Guid ProjectId) : IRequest<Result<ProjectDetailDto>>;
