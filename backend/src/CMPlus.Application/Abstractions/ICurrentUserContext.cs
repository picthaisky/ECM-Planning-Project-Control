namespace CMPlus.Application.Abstractions;

/// <summary>
/// Resolves the current request's authenticated user id, the same way
/// <see cref="ITenantProvider"/> resolves the tenant (S2-BE-02: <c>AuditLog.UserId</c> needs
/// this). Implemented in WebApi by reading the <c>userId</c> JWT claim - never trusted from
/// request input. <c>null</c> for unauthenticated/system contexts (dev/CI seeding, design-time
/// migrations), which is a legitimate, distinguishable value: those audit rows simply have no
/// human actor.
/// </summary>
public interface ICurrentUserContext
{
    Guid? UserId { get; }
}
