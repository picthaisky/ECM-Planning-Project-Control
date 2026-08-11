using CMPlus.Application.Features.Weather;
using CMPlus.Application.Features.Weather.Commands.RecordWeatherLog;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Weather;

public class RecordWeatherLogCommandHandlerTests
{
    private static RecordWeatherLogCommand DefaultCommand(
        Guid? projectId = null,
        IReadOnlyList<Guid>? affectedActivityIds = null,
        decimal? rainfallMm = 42.5m,
        WeatherImpact impact = WeatherImpact.FullStoppage,
        decimal? hoursLost = 8.00m) => new(
        projectId ?? Guid.NewGuid(),
        DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"),
        WeatherCondition.HeavyRain,
        ConditionNote: "ฝนตกหนัก",
        rainfallMm,
        impact,
        ImpactNote: "หยุดเทคอนกรีตโซน B ครึ่งวัน",
        hoursLost,
        affectedActivityIds ?? []);

    /// <summary>Convenience factory for the common case: a valid, non-null actor. <c>userId: null</c>
    /// here means "don't care, generate one" (via <c>?? Guid.NewGuid()</c> below) - it does NOT
    /// simulate an unauthenticated caller. The ActorRequired test below deliberately bypasses this
    /// helper and constructs the handler directly for that reason.</summary>
    private static (RecordWeatherLogCommandHandler Handler, FakeDailyWeatherLogRepository Repository) CreateHandler(
        Guid? tenantId = null, Guid? userId = null, DateTimeOffset? now = null)
    {
        var repository = new FakeDailyWeatherLogRepository();
        var handler = new RecordWeatherLogCommandHandler(
            repository,
            new FakeTenantProvider(tenantId ?? Guid.NewGuid()),
            new FakeCurrentUserContext(userId ?? Guid.NewGuid()),
            new FakeClock(now ?? DateTimeOffset.UtcNow));

        return (handler, repository);
    }

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var (handler, repository) = CreateHandler();
        repository.ProjectExists = false;

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WeatherLogErrorCodes.ProjectNotFound, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Fails_Closed_With_ActorRequired_On_A_Null_UserId_Never_Fabricates_Guid_Empty()
    {
        // Sprint 10 L-01 fix pattern (this task's brief) - the central assertion is the SECOND one:
        // no row is ever added with a fabricated actor. Constructed directly (not via CreateHandler)
        // so the explicit `null` cannot be masked by a "generate one if absent" fallback.
        var repository = new FakeDailyWeatherLogRepository();
        var handler = new RecordWeatherLogCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(null), new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WeatherLogErrorCodes.ActorRequired, result.Error);
        Assert.Equal(0, repository.AddCallCount);
        Assert.Null(repository.AddedLog);
    }

    [Fact]
    public async Task Handle_Returns_UnknownActivity_When_An_Affected_Activity_Id_Does_Not_Belong_To_The_Project()
    {
        var (handler, repository) = CreateHandler();
        var knownActivityId = Guid.NewGuid();
        var unknownActivityId = Guid.NewGuid();
        repository.ExistingActivityIds = [knownActivityId];

        var result = await handler.Handle(
            DefaultCommand(affectedActivityIds: [knownActivityId, unknownActivityId]), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WeatherLogErrorCodes.UnknownActivity, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Persists_A_Log_Stamped_With_Server_Side_Tenant_RecordedAt_And_RecordedBy()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-07-11T19:00:00+07:00");
        var projectId = Guid.NewGuid();
        var (handler, repository) = CreateHandler(tenantId, userId, now);

        var result = await handler.Handle(DefaultCommand(projectId: projectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var log = repository.AddedLog!;
        Assert.Equal(tenantId, log.TenantId);
        Assert.Equal(projectId, log.ProjectId);
        Assert.Equal(now, log.RecordedAt);
        Assert.Equal(userId, log.RecordedByUserId);
        Assert.Equal(WeatherLogEntryKind.Original, log.EntryKind);
        Assert.Null(log.CorrectsWeatherLogId);
        Assert.Equal(1, repository.AddCallCount);

        Assert.Equal(result.Value.Id, log.Id);
        Assert.Equal(result.Value.RainfallMm, log.RainfallMm);
    }

    [Fact]
    public async Task Handle_Allows_RainfallMm_To_Be_Omitted_Genuinely_Not_Measured()
    {
        var (handler, repository) = CreateHandler();

        var result = await handler.Handle(DefaultCommand(rainfallMm: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(repository.AddedLog!.RainfallMm);
    }

    [Fact]
    public async Task Handle_Persists_The_Affected_Activity_Tags_When_All_Are_Known()
    {
        var (handler, repository) = CreateHandler();
        var activityId = Guid.NewGuid();
        repository.ExistingActivityIds = [activityId];

        var result = await handler.Handle(DefaultCommand(affectedActivityIds: [activityId]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([activityId], repository.AddedLog!.AffectedActivities.Select(a => a.ActivityId));
        Assert.Equal([activityId], result.Value.AffectedActivityIds);
    }
}
