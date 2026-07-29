using CMPlus.Domain.Enums;

namespace CMPlus.Application.Abstractions;

/// <summary>Just enough of a <c>User</c> row to authenticate and mint a token - never the full
/// entity, so a handler cannot accidentally leak more than login needs.</summary>
public sealed record UserAuthRecord(Guid Id, Guid TenantId, string Email, UserRole Role, string PasswordHash);

/// <summary>
/// Read-only lookup used only by the login flow (S2-BE-01). Login happens *before* the caller
/// has a tenant context, so <see cref="FindByEmailAsync"/> deliberately searches across every
/// tenant (the one legitimate, tightly-scoped exception to ADR-0002's "every query is
/// tenant-scoped" rule - there is no tenant to scope by yet). <c>User.Email</c> is assumed
/// globally unique for authentication purposes (see backend-developer's Sprint 2 report for why -
/// the alternative, tenant-first login, was not specified anywhere in docs/9-10 or the seed data
/// design). The implementation must use <c>IgnoreQueryFilters()</c> explicitly and only for this
/// single, auditable purpose.
/// </summary>
public interface IUserReader
{
    Task<UserAuthRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
}
