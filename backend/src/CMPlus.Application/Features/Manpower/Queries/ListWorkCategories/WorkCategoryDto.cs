using CMPlus.Domain.Entities;

namespace CMPlus.Application.Features.Manpower.Queries.ListWorkCategories;

/// <summary>One work-category catalogue entry for the Man/Equipment log form's dropdown. Both names
/// are carried so the UI can render Thai-first with the English term, per CLAUDE.md's copy rule.</summary>
public sealed record WorkCategoryDto(Guid Id, string Code, string NameTh, string NameEn, int DisplayOrder)
{
    public static WorkCategoryDto From(WorkCategory category) =>
        new(category.Id, category.Code, category.NameTh, category.NameEn, category.DisplayOrder);
}
