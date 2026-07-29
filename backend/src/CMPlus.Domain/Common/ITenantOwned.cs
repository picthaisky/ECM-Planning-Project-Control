namespace CMPlus.Domain.Common;

/// <summary>
/// Marker for every tenant-owned entity (ADR-0002). <c>CmPlusDbContext</c> applies a global EF
/// Core query filter to every entity implementing this interface, keyed on
/// <c>ITenantProvider.TenantId</c>, and stamps <see cref="TenantId"/> server-side on insert - a
/// TenantId arriving in a request payload/DTO is always ignored, never trusted. Entities that are
/// explicitly exempt (the multi-tenancy root <c>Tenant</c> itself, system tables) do not implement
/// this interface - that omission documents the exemption as deliberate, not an oversight
/// (docs/db-conventions.md §2 rule 5).
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
