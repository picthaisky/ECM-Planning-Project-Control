using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Manpower.Queries.ListWorkCategories;

/// <summary>The projection + tenant scoping live in
/// <see cref="IManpowerEquipmentLogRepository.ListWorkCategoriesForProjectAsync"/>; this handler just
/// maps to the DTO. Never fails - an unknown/cross-tenant project produces an empty list, not a 404.</summary>
public sealed class ListWorkCategoriesQueryHandler(IManpowerEquipmentLogRepository repository)
    : IRequestHandler<ListWorkCategoriesQuery, Result<IReadOnlyList<WorkCategoryDto>>>
{
    public async Task<Result<IReadOnlyList<WorkCategoryDto>>> Handle(
        ListWorkCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await repository.ListWorkCategoriesForProjectAsync(request.ProjectId, cancellationToken);

        return Result<IReadOnlyList<WorkCategoryDto>>.Success(categories.Select(WorkCategoryDto.From).ToList());
    }
}
