using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Root of multi-tenancy (docs/9 §4). Deliberately does NOT implement <see cref="ITenantOwned"/> -
/// it is the tenant, not tenant-owned data (docs/db-conventions.md §2 rule 5).
/// </summary>
public sealed class Tenant : Entity
{
    public string Name { get; private set; } = string.Empty;

    public TenantStatus Status { get; private set; } = TenantStatus.Active;

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private Tenant()
    {
    }

    public Tenant(string name)
    {
        Name = ValidateName(name);
    }

    public void Rename(string name) => Name = ValidateName(name);

    public void Suspend() => Status = TenantStatus.Suspended;

    public void Activate() => Status = TenantStatus.Active;

    private static string ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new DomainException("Tenant name is required.")
            : name.Trim();
}
