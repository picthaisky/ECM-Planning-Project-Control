using CMPlus.Application.Features.Baseline.Commands.CaptureBaseline;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Baseline.Queries.ListBaselines;

/// <summary>
/// <c>GET /api/v1/projects/{projectId}/baselines</c> (US-14.1) - the Baseline screen's list of every
/// captured baseline for a project (the frontend's <c>features/baseline/api.ts#listBaselines</c>,
/// which until now had no backend and degraded to a session-local list). Mirrors
/// <c>ListVariationOrdersQuery</c>: project-scoped, tenant-scoped, and never 404s on an
/// unknown/cross-tenant project - it returns an empty list instead.
/// </summary>
public sealed record ListBaselinesQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<BaselineDto>>>;
