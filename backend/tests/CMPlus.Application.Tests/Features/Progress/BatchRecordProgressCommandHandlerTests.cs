using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Progress;
using CMPlus.Application.Features.Progress.Commands.BatchRecordProgress;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Progress;

public class BatchRecordProgressCommandHandlerTests
{
    private sealed class FakeBatchProgressRepository : IBatchProgressRepository
    {
        public bool ProjectExists { get; set; } = true;
        public IReadOnlyDictionary<Guid, Activity> ActivitiesToReturn { get; set; } = new Dictionary<Guid, Activity>();
        public (Guid ProjectId, DateTimeOffset PeriodEndDate, IReadOnlyList<ActivityProgressLog> Entries)? SavedBatch { get; private set; }

        public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectExists);

        public Task<IReadOnlyDictionary<Guid, Activity>> LoadActivitiesForProjectAsync(
            Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActivitiesToReturn);

        public Task SaveBatchAsync(
            Guid projectId, DateTimeOffset periodEndDate, IReadOnlyList<ActivityProgressLog> newEntries,
            CancellationToken cancellationToken = default)
        {
            SavedBatch = (projectId, periodEndDate, newEntries);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCurrentUserContext(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.PM;
    }

    private sealed class FakeClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static Activity CreateActivity() => new(
        Guid.NewGuid(), Guid.NewGuid(), "A-1", "Formwork",
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 500_000m);

    private static BatchRecordProgressCommandHandler CreateHandler(
        FakeBatchProgressRepository repository, DateTimeOffset now) =>
        new(repository, new FakeCurrentUserContext(Guid.NewGuid()), new FakeClock(now));

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var repository = new FakeBatchProgressRepository { ProjectExists = false };
        var handler = CreateHandler(repository, DateTimeOffset.UtcNow);

        var command = new BatchRecordProgressCommand(
            Guid.NewGuid(), DateTimeOffset.Parse("2026-07-27T00:00:00Z"), [new BatchProgressEntry(Guid.NewGuid(), 50m, null)]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProgressErrorCodes.ProjectNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Rejects_The_Whole_Batch_When_Any_ActivityId_Is_Unknown()
    {
        var known = CreateActivity();
        var repository = new FakeBatchProgressRepository
        {
            ActivitiesToReturn = new Dictionary<Guid, Activity> { [known.Id] = known },
        };
        var handler = CreateHandler(repository, DateTimeOffset.UtcNow);

        var command = new BatchRecordProgressCommand(
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-07-27T00:00:00Z"),
            [new BatchProgressEntry(known.Id, 50m, null), new BatchProgressEntry(Guid.NewGuid(), 40m, null)]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProgressErrorCodes.UnknownActivity, result.Error);
        Assert.Null(repository.SavedBatch);
        // All-or-nothing: the known activity's cache must not have moved either.
        Assert.Equal(0m, known.ProgressPercentage);
    }

    [Fact]
    public async Task Handle_Records_Twenty_Entries_Sharing_One_PeriodEndDate_Via_RecordProgress()
    {
        var activities = Enumerable.Range(0, 20).Select(_ => CreateActivity()).ToList();
        var repository = new FakeBatchProgressRepository
        {
            ActivitiesToReturn = activities.ToDictionary(a => a.Id),
        };
        var now = DateTimeOffset.Parse("2026-07-27T10:00:00Z");
        var handler = CreateHandler(repository, now);
        var periodEndDate = DateTimeOffset.Parse("2026-07-25T00:00:00Z");

        var entries = activities.Select((a, i) => new BatchProgressEntry(a.Id, 10m * (i + 1) % 100 + 1, i)).ToList();
        var command = new BatchRecordProgressCommand(Guid.NewGuid(), periodEndDate, entries);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value.EntriesRecorded);
        Assert.NotNull(repository.SavedBatch);
        var savedEntries = repository.SavedBatch!.Value.Entries;
        Assert.Equal(20, savedEntries.Count);
        Assert.All(savedEntries, e => Assert.Equal(periodEndDate, e.PeriodEndDate));
        Assert.All(savedEntries, e => Assert.Equal(ProgressSource.Manual, e.Source));
        Assert.All(savedEntries, e => Assert.Equal(now, e.RecordedAt));

        // Every row went through Activity.RecordProgress - proven by the cache having moved on
        // every activity (ADR-0009), never a direct assignment.
        foreach (var activity in activities)
        {
            Assert.True(activity.ProgressPercentage > 0m);
        }
    }

    [Fact]
    public async Task Handle_Fails_Closed_With_ActorRequired_On_A_Null_UserId_Never_Fabricates_Guid_Empty()
    {
        // Security review sprint-15.md L-01b - the central assertion is the SECOND one: no batch is
        // ever saved with a fabricated actor. Constructed directly (not via CreateHandler) so the
        // explicit `null` cannot be masked by a "generate one if absent" fallback.
        var known = CreateActivity();
        var repository = new FakeBatchProgressRepository
        {
            ActivitiesToReturn = new Dictionary<Guid, Activity> { [known.Id] = known },
        };
        var handler = new BatchRecordProgressCommandHandler(
            repository, new FakeCurrentUserContext(null), new FakeClock(DateTimeOffset.UtcNow));

        var command = new BatchRecordProgressCommand(
            Guid.NewGuid(), DateTimeOffset.Parse("2026-07-27T00:00:00Z"), [new BatchProgressEntry(known.Id, 50m, null)]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProgressErrorCodes.ActorRequired, result.Error);
        Assert.Null(repository.SavedBatch);
        Assert.Equal(0m, known.ProgressPercentage);
    }
}
