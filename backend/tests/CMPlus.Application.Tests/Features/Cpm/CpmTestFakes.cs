using CMPlus.Application.Abstractions;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Cpm;

/// <summary>Shared hand-written fakes for the Cpm handler test suites - mirrors this codebase's
/// established shared-fakes-per-feature convention (see
/// <c>CMPlus.Application.Tests.Features.Weather.WeatherTestFakes</c>).</summary>
internal sealed class FakeTenantProvider(Guid tenantId) : ITenantProvider
{
    public Guid TenantId { get; } = tenantId;
}

internal sealed class FakeCurrentUserContext(Guid? userId) : ICurrentUserContext
{
    public Guid? UserId { get; } = userId;

    public UserRole Role => UserRole.PM;
}

internal sealed class FakeClock(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = now;
}
