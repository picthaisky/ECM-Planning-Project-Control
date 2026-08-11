using CMPlus.Domain.Common;

namespace CMPlus.Domain.Entities;

/// <summary>
/// domain-rules.md (manpower-equipment) §4.3: หมวดงาน - a trade/discipline taxonomy, orthogonal to
/// the WBS. This is the closed, joinable vocabulary the prototype's free-text "work" column must
/// not become (§4.3: "free text cannot be grouped, cannot be rolled up, and cannot be joined to a
/// budget"). <see cref="ProjectId"/> is nullable so a tenant can maintain one default catalogue
/// (<see langword="null"/>) with a per-project override list layered on top - a
/// <see cref="Entities.ManpowerEquipmentLog.WorkCategoryId"/> may reference either.
/// </summary>
public sealed class WorkCategory : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    /// <summary><see langword="null"/> = tenant-wide default catalogue entry (§4.3).</summary>
    public Guid? ProjectId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string NameTh { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private WorkCategory()
    {
    }

    public WorkCategory(
        Guid tenantId,
        Guid? projectId,
        string code,
        string nameTh,
        string nameEn,
        int displayOrder,
        bool isActive = true)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Code = ValidateRequired(code, nameof(Code));
        NameTh = ValidateRequired(nameTh, nameof(NameTh));
        NameEn = ValidateRequired(nameEn, nameof(NameEn));
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }

    public void Rename(string nameTh, string nameEn)
    {
        NameTh = ValidateRequired(nameTh, nameof(NameTh));
        NameEn = ValidateRequired(nameEn, nameof(NameEn));
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void SetDisplayOrder(int displayOrder) => DisplayOrder = displayOrder;

    private static string ValidateRequired(string value, string propertyName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"WorkCategory.{propertyName} is required.")
            : value.Trim();
}
