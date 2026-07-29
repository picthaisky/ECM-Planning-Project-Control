using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>User account, scoped to a tenant (docs/9 §4). RBAC role is <see cref="UserRole"/>.</summary>
public sealed class User : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public string PasswordHash { get; private set; } = string.Empty;

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private User()
    {
    }

    public User(Guid tenantId, string email, UserRole role, string passwordHash)
    {
        TenantId = tenantId;
        Email = ValidateEmail(email);
        Role = role;
        PasswordHash = ValidatePasswordHash(passwordHash);
    }

    public void ChangeRole(UserRole role) => Role = role;

    public void ChangeEmail(string email) => Email = ValidateEmail(email);

    public void ChangePasswordHash(string passwordHash) => PasswordHash = ValidatePasswordHash(passwordHash);

    private static string ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new DomainException("A valid email address is required.");
        }

        return email.Trim().ToLowerInvariant();
    }

    private static string ValidatePasswordHash(string passwordHash) =>
        string.IsNullOrWhiteSpace(passwordHash)
            ? throw new DomainException("PasswordHash is required.")
            : passwordHash;
}
