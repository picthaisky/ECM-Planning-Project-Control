using CMPlus.Domain.Enums;

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

    /// <summary>
    /// The role claim on the caller's JWT (S9-BE-05: first real consumer, alongside
    /// <see cref="UserId"/>) - the same "tenantId/userId/role are always present together"
    /// triad <c>JwtClaimTypes</c> already documents. Used both to authorize an approval act (does
    /// this actor's role match the resolved chain step's required role) and to stamp
    /// <c>ApprovalAction.ActorRoleAtTime</c> - reading it here, once, from the token issued at
    /// login/sign-in time is what makes "the role at the moment of acting, not looked up later"
    /// true: a role change that happens after this token was issued cannot retroactively alter
    /// what an already-recorded <c>ApprovalAction</c> says the actor's role was. Implemented in
    /// WebApi by reading the standard <see cref="System.Security.Claims.ClaimTypes.Role"/> claim -
    /// never trusted from request input, never re-queried from <c>User.Role</c> at write time.
    /// Throws if called on an unauthenticated request - callers that also accept <c>null</c>
    /// actors (e.g. system/seed contexts) must guard on <see cref="UserId"/> first, exactly as
    /// every existing caller of this interface already does.
    /// </summary>
    UserRole Role { get; }
}
