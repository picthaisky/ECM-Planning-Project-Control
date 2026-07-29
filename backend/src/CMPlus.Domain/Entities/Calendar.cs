using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// A working-day calendar used by the CPM engine's working-day math (docs/9 §4). Per-date
/// overrides (holidays, added working days) are separate <see cref="CalendarException"/> rows
/// created only through <see cref="AddException"/>.
/// </summary>
public sealed class Calendar : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public WorkingDays WorkingDays { get; private set; }

    public bool IsDefault { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private Calendar()
    {
    }

    public Calendar(Guid tenantId, Guid projectId, string name, WorkingDays workingDays, bool isDefault = false)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Name = ValidateName(name);
        WorkingDays = workingDays;
        IsDefault = isDefault;
    }

    public CalendarException AddException(DateTimeOffset date, bool isWorkingDay, string? description = null) =>
        new(TenantId, Id, date, isWorkingDay, description);

    public void Rename(string name) => Name = ValidateName(name);

    public void SetWorkingDays(WorkingDays workingDays) => WorkingDays = workingDays;

    public void SetAsDefault(bool isDefault) => IsDefault = isDefault;

    private static string ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new DomainException("Calendar name is required.")
            : name.Trim();
}
