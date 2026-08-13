using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Manpower.Queries.ListWorkCategories;

/// <summary>
/// <c>GET /api/v1/projects/{projectId}/work-categories</c> - the active work-category catalogue for a
/// project (the tenant-wide defaults plus any project-specific entries), ordered by display order.
/// Backs the Man/Equipment log form's category dropdown, replacing the raw-GUID text input (the S12
/// catalogue gap). Never 404s: an unknown/cross-tenant project yields an empty list.
/// </summary>
public sealed record ListWorkCategoriesQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<WorkCategoryDto>>>;
