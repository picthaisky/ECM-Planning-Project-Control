namespace CMPlus.Domain.Enums;

/// <summary>
/// RBAC role assigned to <c>User.Role</c>. Seven values - <see cref="ProjectDirector"/> was
/// added by ADR-0008 to support a 3-tier Delegation-of-Authority chain (PM under a threshold,
/// Project Director above it, Executive above that).
/// </summary>
public enum UserRole
{
    PM = 1,
    Planning = 2,
    Site = 3,
    QS = 4,
    Executive = 5,
    Admin = 6,
    ProjectDirector = 7,
}
