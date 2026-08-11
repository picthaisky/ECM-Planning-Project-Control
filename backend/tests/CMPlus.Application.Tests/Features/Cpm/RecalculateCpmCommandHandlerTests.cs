using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Cpm;
using CMPlus.Application.Features.Cpm.Commands.RecalculateCpm;
using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Cpm;

public class RecalculateCpmCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T09:00:00Z");

    private sealed class FakeCpmScheduleRepository : ICpmScheduleRepository
    {
        public CpmProjectContext? ProjectContext { get; set; } = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        public CpmScheduleGraph GraphToReturn { get; set; } = new(new Dictionary<Guid, Activity>(), []);
        public (Guid ProjectId, IReadOnlyList<CpmActivityWriteBack> Results, CpmRun Run)? Saved { get; private set; }

        public Task<CpmProjectContext?> GetProjectContextAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectContext);

        public Task<CpmScheduleGraph> LoadScheduleGraphAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GraphToReturn);

        public Task SaveResultsAsync(
            Guid projectId, IReadOnlyList<CpmActivityWriteBack> results, CpmRun run, CancellationToken cancellationToken = default)
        {
            Saved = (projectId, results, run);
            return Task.CompletedTask;
        }
    }

    private static Activity CreateActivity(string code, int durationDays) => new(
        Guid.NewGuid(), Guid.NewGuid(), code, code,
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), durationDays, 100_000m);

    private static RecalculateCpmCommandHandler CreateHandler(
        FakeCpmScheduleRepository repository, Guid? tenantId = null, Guid? actorUserId = null, DateTimeOffset? now = null) =>
        new(repository, new FakeTenantProvider(tenantId ?? Guid.NewGuid()), new FakeCurrentUserContext(actorUserId ?? Guid.NewGuid()),
            new FakeClock(now ?? Now));

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var repository = new FakeCpmScheduleRepository { ProjectContext = null };
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new RecalculateCpmCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CpmErrorCodes.ProjectNotFound, result.Error);
        Assert.Null(repository.Saved);
    }

    /// <summary>ADR-0019/L-01: <c>CpmRun.TriggeredByUserId</c> must never be fabricated - a null
    /// actor fails closed before the schedule graph is even loaded, mirroring
    /// <c>RecordWeatherLogCommandHandler</c>/<c>SubmitVariationOrderCommandHandler</c>'s identical
    /// guard.</summary>
    [Fact]
    public async Task Handle_Returns_ActorRequired_When_The_Caller_Has_No_Resolvable_User_Id()
    {
        var repository = new FakeCpmScheduleRepository();
        var handler = new RecalculateCpmCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(null), new FakeClock(Now));

        var result = await handler.Handle(new RecalculateCpmCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CpmErrorCodes.ActorRequired, result.Error);
        Assert.Null(repository.Saved);
    }

    [Fact]
    public async Task Handle_Reproduces_The_Canonical_Fixture_End_To_End_And_Writes_Back_Via_SetCpmResults()
    {
        var a = CreateActivity("A", 5);
        var b = CreateActivity("B", 3);
        var c = CreateActivity("C", 6);
        var d = CreateActivity("D", 4);

        var relations = new List<ActivityRelation>
        {
            new(Guid.NewGuid(), a.Id, b.Id, RelationType.FS, 0),
            new(Guid.NewGuid(), a.Id, c.Id, RelationType.FS, 0),
            new(Guid.NewGuid(), b.Id, d.Id, RelationType.FS, 0),
            new(Guid.NewGuid(), c.Id, d.Id, RelationType.FS, 0),
        };

        var activities = new[] { a, b, c, d }.ToDictionary(x => x.Id);
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var dataDate = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var calculatedAt = DateTimeOffset.Parse("2026-08-10T09:00:00Z");
        var repository = new FakeCpmScheduleRepository
        {
            GraphToReturn = new CpmScheduleGraph(activities, relations),
            ProjectContext = new CpmProjectContext(dataDate),
        };
        var handler = CreateHandler(repository, tenantId, actorUserId, calculatedAt);
        var projectId = Guid.NewGuid();

        var result = await handler.Handle(new RecalculateCpmCommand(projectId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(4, result.Value.ActivitiesProcessed);
        Assert.Equal(3, result.Value.CriticalActivityCount); // A, C, D
        Assert.Equal(15, result.Value.ProjectDurationDays);
        Assert.Equal([a.Id, c.Id, d.Id], result.Value.CriticalPath);

        // Written back onto the tracked Activity entities via SetCpmResults - never a direct field
        // assignment (there is none exposed).
        Assert.True(a.IsCritical);
        Assert.Equal(0, a.TotalFloat);
        Assert.Equal(0, a.FreeFloat);

        Assert.False(b.IsCritical);
        Assert.Equal(3, b.TotalFloat);
        Assert.Equal(3, b.FreeFloat);

        Assert.True(c.IsCritical);
        Assert.Equal(0, c.TotalFloat);
        Assert.Equal(0, c.FreeFloat);

        Assert.True(d.IsCritical);
        Assert.Equal(0, d.TotalFloat);
        Assert.Equal(0, d.FreeFloat);

        Assert.NotNull(repository.Saved);
        Assert.Equal(projectId, repository.Saved!.Value.ProjectId);
        Assert.Equal(4, repository.Saved.Value.Results.Count);
        Assert.Contains(repository.Saved.Value.Results, r => r.ActivityId == a.Id && r.IsCritical && r.TotalFloat == 0 && r.FreeFloat == 0);
        Assert.Contains(repository.Saved.Value.Results, r => r.ActivityId == b.Id && !r.IsCritical && r.TotalFloat == 3 && r.FreeFloat == 3);

        // ADR-0019: the append-only CpmRun captured alongside (never instead of) the write-back.
        var run = repository.Saved.Value.Run;
        Assert.Equal(tenantId, run.TenantId);
        Assert.Equal(projectId, run.ProjectId);
        Assert.Equal(calculatedAt, run.CalculatedAt);
        Assert.Equal(dataDate, run.DataDate);
        Assert.Equal(15, run.ProjectDurationDays);
        Assert.Equal(actorUserId, run.TriggeredByUserId);
        Assert.Equal(CpmRunTrigger.Manual, run.Trigger); // RecalculateCpmCommand's default.
        Assert.Equal(4, run.Activities.Count);
        Assert.Equal(4, run.Relations.Count);

        var runActivityA = Assert.Single(run.Activities, x => x.ActivityId == a.Id);
        Assert.True(runActivityA.IsCritical);
        Assert.Equal(0, runActivityA.TotalFloat);
        Assert.Equal(0, runActivityA.FreeFloat);
        Assert.Equal(5, runActivityA.DurationDays);
        Assert.Equal(0, runActivityA.EarlyStart);
        Assert.Equal(5, runActivityA.EarlyFinish);
        Assert.Equal(0, runActivityA.LateStart);
        Assert.Equal(5, runActivityA.LateFinish);

        var runActivityB = Assert.Single(run.Activities, x => x.ActivityId == b.Id);
        Assert.False(runActivityB.IsCritical);
        Assert.Equal(3, runActivityB.TotalFloat);
        Assert.Equal(3, runActivityB.FreeFloat);
        Assert.Equal(3, runActivityB.DurationDays);

        Assert.Contains(run.Relations, r => r.PredecessorActivityId == a.Id && r.SuccessorActivityId == b.Id && r.RelationType == RelationType.FS);
        Assert.Contains(run.Relations, r => r.PredecessorActivityId == c.Id && r.SuccessorActivityId == d.Id && r.RelationType == RelationType.FS);
    }

    [Fact]
    public async Task Handle_Rejects_A_Cyclic_Graph_Without_Mutating_Any_Activity_Or_Saving()
    {
        var a = CreateActivity("A", 5);
        var b = CreateActivity("B", 3);

        var relations = new List<ActivityRelation>
        {
            new(Guid.NewGuid(), a.Id, b.Id, RelationType.FS, 0),
            new(Guid.NewGuid(), b.Id, a.Id, RelationType.FS, 0),
        };

        var repository = new FakeCpmScheduleRepository
        {
            GraphToReturn = new CpmScheduleGraph(new[] { a, b }.ToDictionary(x => x.Id), relations),
        };
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new RecalculateCpmCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith(CpmValidationErrorCodes.CycleDetected, result.Error);
        // All-or-nothing: a rejected graph must not have mutated IsCritical/TotalFloat/FreeFloat on
        // any Activity, and (ADR-0019) must not have captured a CpmRun either - a rejected
        // recalculation is not history worth keeping.
        Assert.Null(repository.Saved);
        Assert.False(a.IsCritical);
        Assert.Null(a.TotalFloat);
        Assert.False(b.IsCritical);
        Assert.Null(b.TotalFloat);
    }

    [Fact]
    public async Task Handle_Succeeds_With_Zero_Activities_When_The_Project_Has_None()
    {
        var repository = new FakeCpmScheduleRepository();
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new RecalculateCpmCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.ActivitiesProcessed);
        Assert.Equal(0, result.Value.CriticalActivityCount);
        Assert.Empty(result.Value.CriticalPath);
        Assert.NotNull(repository.Saved);

        // ADR-0019: an empty project still captures a valid (empty) run - "no activities yet" is
        // not the same as "recalculation did not happen".
        var run = repository.Saved!.Value.Run;
        Assert.Equal(0, run.ProjectDurationDays);
        Assert.Empty(run.Activities);
        Assert.Empty(run.Relations);
    }

    [Fact]
    public void RecalculateCpmCommand_Defaults_Trigger_To_Manual()
    {
        // The only wired production caller (CpmController) constructs RecalculateCpmCommand with
        // just a ProjectId - this pins that the implicit Trigger stays Manual if the record's
        // parameter order/defaults are ever touched.
        Assert.Equal(CpmRunTrigger.Manual, new RecalculateCpmCommand(Guid.NewGuid()).Trigger);
    }

    [Fact]
    public async Task Handle_Threads_The_Requested_Trigger_Onto_The_Captured_CpmRun()
    {
        var repository = new FakeCpmScheduleRepository();
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new RecalculateCpmCommand(Guid.NewGuid(), CpmRunTrigger.Import), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CpmRunTrigger.Import, repository.Saved!.Value.Run.Trigger);
    }
}
