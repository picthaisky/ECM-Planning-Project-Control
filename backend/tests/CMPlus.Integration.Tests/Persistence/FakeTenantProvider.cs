using CMPlus.Application.Abstractions;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>Mutable <see cref="ITenantProvider"/> test double - lets a test switch "the current
/// tenant" mid-test to exercise the global query filter and TenantId stamping.</summary>
internal sealed class FakeTenantProvider(Guid tenantId) : ITenantProvider
{
    public Guid TenantId { get; set; } = tenantId;
}
