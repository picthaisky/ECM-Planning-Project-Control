using CMPlus.Application.Features.Weather;
using CMPlus.Application.Features.Weather.Commands.RecordWeatherLogCorrection;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Weather;

/// <summary>domain-rules.md weather-eot §8.2's chain-integrity rules 1/2/4, at the handler level
/// (persistence-backed proof, including the authoritative filtered unique index, lives in
/// <c>CMPlus.Integration.Tests.Persistence</c>).</summary>
public class RecordWeatherLogCorrectionCommandHandlerTests
{
    private static DailyWeatherLog SeedOriginal(FakeDailyWeatherLogRepository repository, Guid projectId, DateTimeOffset recordedAt)
    {
        var original = DailyWeatherLog.CreateOriginal(
            Guid.NewGuid(), projectId, DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"), WeatherCondition.HeavyRain,
            null, 42.5m, WeatherImpact.FullStoppage, null, 8.00m, Guid.NewGuid(), recordedAt, []);
        repository.LogsById[original.Id] = original;
        return original;
    }

    private static RecordWeatherLogCorrectionCommand CorrectionCommand(
        Guid projectId, Guid correctsWeatherLogId, WeatherLogEntryKind entryKind = WeatherLogEntryKind.Correction,
        string correctionReason = "ตรวจใบบันทึกกะแล้ว หยุดจริง 3 ชั่วโมง", decimal? hoursLost = 3.00m) => new(
        projectId, correctsWeatherLogId, entryKind, correctionReason,
        DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"), WeatherCondition.HeavyRain, null, 45.0m,
        WeatherImpact.PartialStoppage, null, hoursLost, []);

    private static (RecordWeatherLogCorrectionCommandHandler Handler, FakeDailyWeatherLogRepository Repository) CreateHandler(
        Guid? tenantId = null, Guid? userId = null, DateTimeOffset? now = null)
    {
        var repository = new FakeDailyWeatherLogRepository();
        var handler = new RecordWeatherLogCorrectionCommandHandler(
            repository,
            new FakeTenantProvider(tenantId ?? Guid.NewGuid()),
            new FakeCurrentUserContext(userId ?? Guid.NewGuid()),
            new FakeClock(now ?? DateTimeOffset.Parse("2026-07-09T00:00:00+07:00")));

        return (handler, repository);
    }

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var (handler, repository) = CreateHandler();
        repository.ProjectExists = false;

        var result = await handler.Handle(CorrectionCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WeatherLogErrorCodes.ProjectNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Fails_Closed_With_ActorRequired_On_A_Null_UserId()
    {
        var repository = new FakeDailyWeatherLogRepository();
        var projectId = Guid.NewGuid();
        var original = SeedOriginal(repository, projectId, DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"));
        var handler = new RecordWeatherLogCorrectionCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(null),
            new FakeClock(DateTimeOffset.Parse("2026-07-09T00:00:00+07:00")));

        var result = await handler.Handle(CorrectionCommand(projectId, original.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WeatherLogErrorCodes.ActorRequired, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Rule1_Returns_CorrectionTargetNotFound_When_The_Target_Does_Not_Exist_In_This_Project()
    {
        var (handler, repository) = CreateHandler();

        var result = await handler.Handle(CorrectionCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WeatherLogErrorCodes.CorrectionTargetNotFound, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Rule2_Returns_AlreadySuperseded_When_The_Target_Already_Has_A_Correction()
    {
        // The load-bearing rule (domain-rules.md's own words). W-10 step 8's exact scenario: a
        // second attempt to correct E1 after E2 already did, must be refused - a correction can
        // only ever target the current chain tail.
        var (handler, repository) = CreateHandler();
        var projectId = Guid.NewGuid();
        var e1 = SeedOriginal(repository, projectId, DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"));
        var e2 = DailyWeatherLog.CreateCorrection(
            repository.LogsById[e1.Id].TenantId, projectId, e1.Id, "first correction",
            DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"), WeatherCondition.HeavyRain, null, 45.0m,
            WeatherImpact.PartialStoppage, null, 3.00m, Guid.NewGuid(), DateTimeOffset.Parse("2026-07-08T12:00:00+07:00"), []);
        repository.LogsById[e2.Id] = e2;

        var result = await handler.Handle(CorrectionCommand(projectId, e1.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WeatherLogErrorCodes.AlreadySuperseded, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Rule2_Allows_Correcting_The_Current_Chain_Tail()
    {
        // The other half of rule 2: correcting E2 (the tail), not E1, must succeed.
        var (handler, repository) = CreateHandler();
        var projectId = Guid.NewGuid();
        var e1 = SeedOriginal(repository, projectId, DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"));
        var e2 = DailyWeatherLog.CreateCorrection(
            e1.TenantId, projectId, e1.Id, "first correction",
            DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"), WeatherCondition.HeavyRain, null, 45.0m,
            WeatherImpact.PartialStoppage, null, 3.00m, Guid.NewGuid(), DateTimeOffset.Parse("2026-07-08T12:00:00+07:00"), []);
        repository.LogsById[e2.Id] = e2;

        var result = await handler.Handle(CorrectionCommand(projectId, e2.Id, hoursLost: 7.00m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(e2.Id, repository.AddedLog!.CorrectsWeatherLogId);
    }

    [Fact]
    public async Task Handle_Rule4_Returns_CorrectionOrdering_When_The_Target_Is_Not_Older_Than_Now()
    {
        // The target must be strictly older than the correction's own RecordedAt (server clock).
        var (handler, repository) = CreateHandler(now: DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"));
        var projectId = Guid.NewGuid();
        // Seeded target's RecordedAt (2026-07-08T00:00:00) is NOT strictly before the handler's
        // clock (also 2026-07-08T00:00:00) - equal, which must fail the strict "<" test.
        var original = SeedOriginal(repository, projectId, DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"));

        var result = await handler.Handle(CorrectionCommand(projectId, original.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WeatherLogErrorCodes.CorrectionOrdering, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Persists_A_Retraction_Voiding_The_Target()
    {
        var (handler, repository) = CreateHandler();
        var projectId = Guid.NewGuid();
        var original = SeedOriginal(repository, projectId, DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"));

        var result = await handler.Handle(
            CorrectionCommand(projectId, original.Id, entryKind: WeatherLogEntryKind.Retraction, correctionReason: "บันทึกผิดวัน"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WeatherLogEntryKind.Retraction, repository.AddedLog!.EntryKind);
        Assert.Equal("บันทึกผิดวัน", repository.AddedLog.CorrectionReason);
    }

    [Fact]
    public async Task Handle_Persists_A_Correction_Stamped_With_Server_Side_Tenant_RecordedAt_And_RecordedBy()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-07-09T09:00:00+07:00");
        var projectId = Guid.NewGuid();
        var (handler, repository) = CreateHandler(tenantId, userId, now);
        var original = SeedOriginal(repository, projectId, DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"));

        var result = await handler.Handle(CorrectionCommand(projectId, original.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var correction = repository.AddedLog!;
        Assert.Equal(tenantId, correction.TenantId);
        Assert.Equal(userId, correction.RecordedByUserId);
        Assert.Equal(now, correction.RecordedAt);
        Assert.Equal(WeatherLogEntryKind.Correction, correction.EntryKind);
        Assert.Equal(original.Id, correction.CorrectsWeatherLogId);
        Assert.Equal(3.00m, correction.HoursLost);
    }
}
