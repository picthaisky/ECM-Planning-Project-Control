using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Baseline;
using CMPlus.Application.Features.Baseline.Commands.CaptureBaseline;

namespace CMPlus.Application.Tests.Features.Baseline;

public class CaptureBaselineCommandHandlerTests
{
    // `userId: null` deliberately means "use a fresh random actor", not "no actor" - `?? Guid.NewGuid()`
    // would otherwise silently defeat a genuine null, which is exactly the case the ActorRequired
    // test below needs to exercise. `hasUser: false` is the only way to construct a null-actor
    // context through this helper.
    private static (CaptureBaselineCommandHandler Handler, FakeBaselineRepository Repository) CreateHandler(
        Guid? tenantId = null, Guid? userId = null, bool hasUser = true, DateTimeOffset? now = null)
    {
        var repository = new FakeBaselineRepository();
        var resolvedUserId = hasUser ? userId ?? Guid.NewGuid() : (Guid?)null;
        var handler = new CaptureBaselineCommandHandler(
            repository,
            new FakeTenantProvider(tenantId ?? Guid.NewGuid()),
            new FakeCurrentUserContext(resolvedUserId),
            new FakeClock(now ?? DateTimeOffset.Parse("2026-08-11T09:00:00+07:00")));

        return (handler, repository);
    }

    [Fact]
    public async Task Handle_Returns_ActorRequired_When_The_Current_User_Is_Null()
    {
        // Fail closed on a null actor - never ?? Guid.Empty (this task's standing requirement,
        // mirrors RecalculateCpmCommandHandler/ADR-0019's identical discipline). Checked before any
        // repository read, so a rejected request never even looks up the project.
        var (handler, repository) = CreateHandler(hasUser: false);

        var result = await handler.Handle(new CaptureBaselineCommand(Guid.NewGuid(), "BL-1"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BaselineErrorCodes.ActorRequired, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var (handler, repository) = CreateHandler();
        repository.BacToReturn = null;

        var result = await handler.Handle(new CaptureBaselineCommand(Guid.NewGuid(), "BL-1"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BaselineErrorCodes.ProjectNotFound, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Captures_A_Baseline_Snapshotting_Every_Current_Activity()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-11T09:00:00+07:00");
        var activityId1 = Guid.NewGuid();
        var activityId2 = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00+07:00");

        var (handler, repository) = CreateHandler(tenantId, userId, now: now);
        repository.BacToReturn = 492_400_000.00m;
        repository.ActivitiesToReturn =
        [
            new BaselineActivitySourceRow(activityId1, start, start.AddDays(14), 14, 1_000_000.00m),
            new BaselineActivitySourceRow(activityId2, start.AddDays(14), start.AddDays(24), 10, 500_000.00m),
        ];

        var result = await handler.Handle(new CaptureBaselineCommand(Guid.NewGuid(), "Revised Baseline - Rev.2"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.AddCallCount);
        Assert.Equal(2, result.Value.ActivityCount);
        Assert.False(result.Value.IsActive); // never active on creation
        Assert.Equal("Revised Baseline - Rev.2", result.Value.Name);
        Assert.Equal(492_400_000.00m, result.Value.Bac);
        Assert.Equal(userId, result.Value.CapturedByUserId);
        Assert.Equal(now, result.Value.CapturedAt);

        var stored = repository.AddedBaseline!;
        Assert.Equal(tenantId, stored.TenantId);
        Assert.Contains(stored.Snapshots, s => s.ActivityId == activityId1 && s.BudgetCost == 1_000_000.00m && s.DurationDays == 14);
        Assert.Contains(stored.Snapshots, s => s.ActivityId == activityId2 && s.BudgetCost == 500_000.00m && s.DurationDays == 10);
    }

    [Fact]
    public async Task Handle_Captures_An_Empty_Baseline_When_The_Project_Has_No_Activities_Yet()
    {
        var (handler, repository) = CreateHandler();
        repository.ActivitiesToReturn = [];

        var result = await handler.Handle(new CaptureBaselineCommand(Guid.NewGuid(), "BL-0"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.ActivityCount);
        Assert.Equal(1, repository.AddCallCount);
    }
}
