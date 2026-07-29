using CMPlus.Application.Abstractions;

namespace CMPlus.Infrastructure.Persistence.Seed;

/// <summary>
/// Mutable <see cref="ITenantProvider"/> used only by <see cref="DevDataSeeder"/> (S1-DB-03) to
/// switch "the current tenant" between each of the seed tenants it creates in a single pass over
/// one <see cref="CmPlusDbContext"/>. The real runtime <c>ITenantProvider</c> (wired in Sprint 2)
/// reads a single, fixed value from the authenticated request's JWT claim and is never mutated
/// mid-request (ADR-0002) - this type must never be registered in the WebApi's request-scoped DI
/// container; it exists purely so a dev/CI seed run can legitimately write rows for more than one
/// tenant without ever bypassing the global tenant query filter or the server-side TenantId stamp
/// that every other write path goes through.
/// </summary>
public sealed class SeedTenantContext : ITenantProvider
{
    public Guid TenantId { get; set; }
}
