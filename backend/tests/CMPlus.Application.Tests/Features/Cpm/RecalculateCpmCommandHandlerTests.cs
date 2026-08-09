using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Cpm;
using CMPlus.Application.Features.Cpm.Commands.RecalculateCpm;
using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Cpm;

public class RecalculateCpmCommandHandlerTests
{
    private sealed class FakeCpmScheduleRepository : ICpmScheduleRepository
    {
        public bool ProjectExists { get; set; } = true;
        public CpmScheduleGraph GraphToReturn { get; set; } = new(new Dictionary<Guid, Activity>(), []);
        public (Guid ProjectId, IReadOnlyList<CpmActivityWriteBack> Results)? Saved { get; private set; }

        public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectExists);

        public Task<CpmScheduleGraph> LoadScheduleGraphAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GraphToReturn);

        public Task SaveResultsAsync(
            Guid projectId, IReadOnlyList<CpmActivityWriteBack> results, CancellationToken cancellationToken = default)
        {
            Saved = (projectId, results);
            return Task.CompletedTask;
        }
    }

    private static Activity CreateActivity(string code, int durationDays) => new(
        Guid.NewGuid(), Guid.NewGuid(), code, code,
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), durationDays, 100_000m);

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var repository = new FakeCpmScheduleRepository { ProjectExists = false };
        var handler = new RecalculateCpmCommandHandler(repository);

        var result = await handler.Handle(new RecalculateCpmCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CpmErrorCodes.ProjectNotFound, result.Error);
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
        var repository = new FakeCpmScheduleRepository
        {
            GraphToReturn = new CpmScheduleGraph(activities, relations),
        };
        var handler = new RecalculateCpmCommandHandler(repository);
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
        var handler = new RecalculateCpmCommandHandler(repository);

        var result = await handler.Handle(new RecalculateCpmCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith(CpmValidationErrorCodes.CycleDetected, result.Error);
        Assert.Null(repository.Saved);
        // All-or-nothing: a rejected graph must not have mutated IsCritical/TotalFloat/FreeFloat
        // on any Activity - both must remain at their pre-CPM defaults.
        Assert.False(a.IsCritical);
        Assert.Null(a.TotalFloat);
        Assert.False(b.IsCritical);
        Assert.Null(b.TotalFloat);
    }

    [Fact]
    public async Task Handle_Succeeds_With_Zero_Activities_When_The_Project_Has_None()
    {
        var repository = new FakeCpmScheduleRepository();
        var handler = new RecalculateCpmCommandHandler(repository);

        var result = await handler.Handle(new RecalculateCpmCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.ActivitiesProcessed);
        Assert.Equal(0, result.Value.CriticalActivityCount);
        Assert.Empty(result.Value.CriticalPath);
        Assert.NotNull(repository.Saved);
    }
}
