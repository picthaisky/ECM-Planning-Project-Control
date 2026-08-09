using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Gantt;
using CMPlus.Application.Features.Gantt.Queries.GetGantt;
using CMPlus.Application.Gantt;
using CMPlus.Application.Wbs;

namespace CMPlus.Application.Tests.Features.Gantt;

public class GetGanttQueryHandlerTests
{
    private sealed class FakeWbsTreeReader : IWbsTreeReader
    {
        public bool ProjectExists { get; set; } = true;
        public IReadOnlyList<WbsNodeFlatRow> NodesToReturn { get; set; } = [];

        public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectExists);

        public Task<IReadOnlyList<WbsNodeFlatRow>> GetNodesWithActivityCountsAsync(
            Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(NodesToReturn);
    }

    private sealed class FakeGanttActivityReader : IGanttActivityReader
    {
        public IReadOnlyList<GanttActivityFlatRow> ActivitiesToReturn { get; set; } = [];
        public DateTimeOffset DataDateToReturn { get; set; } = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
        public Guid? LastProjectIdRequested { get; private set; }
        public DateTimeOffset? LastFromRequested { get; private set; }
        public DateTimeOffset? LastToRequested { get; private set; }

        public Task<IReadOnlyList<GanttActivityFlatRow>> GetActivitiesAsync(
            Guid projectId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
        {
            LastProjectIdRequested = projectId;
            LastFromRequested = from;
            LastToRequested = to;
            return Task.FromResult(ActivitiesToReturn);
        }

        public Task<DateTimeOffset> GetProjectDataDateAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DataDateToReturn);
    }

    private static GanttActivityFlatRow CreateActivity(Guid wbsNodeId, string code) => new(
        Guid.NewGuid(),
        wbsNodeId,
        code,
        $"Activity {code}",
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        DateTimeOffset.Parse("2026-01-15T00:00:00Z"),
        null,
        null,
        false,
        null,
        null);

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var wbsTreeReader = new FakeWbsTreeReader { ProjectExists = false };
        var activityReader = new FakeGanttActivityReader();
        var handler = new GetGanttQueryHandler(wbsTreeReader, activityReader);

        var result = await handler.Handle(new GetGanttQuery(Guid.NewGuid(), null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GanttErrorCodes.ProjectNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Builds_The_Ordered_Bars_From_Both_Readers_Flat_Rows()
    {
        var projectId = Guid.NewGuid();
        var node = new WbsNodeFlatRow(Guid.NewGuid(), null, "1", "Structure", 100m, 1);
        var activity = CreateActivity(node.Id, "ACT-100");

        var wbsTreeReader = new FakeWbsTreeReader { NodesToReturn = [node] };
        var activityReader = new FakeGanttActivityReader { ActivitiesToReturn = [activity] };
        var handler = new GetGanttQueryHandler(wbsTreeReader, activityReader);

        var result = await handler.Handle(new GetGanttQuery(projectId, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(projectId, result.Value.ProjectId);
        var bar = Assert.Single(result.Value.Activities);
        Assert.Equal(activity.Id, bar.Id);
        Assert.Equal(node.Id, bar.WbsNodeId);
    }

    [Fact]
    public async Task Handle_Forwards_The_ProjectId_And_Date_Range_To_The_Activity_Reader()
    {
        var projectId = Guid.NewGuid();
        var from = DateTimeOffset.Parse("2026-02-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-02-28T00:00:00Z");

        var wbsTreeReader = new FakeWbsTreeReader();
        var activityReader = new FakeGanttActivityReader();
        var handler = new GetGanttQueryHandler(wbsTreeReader, activityReader);

        await handler.Handle(new GetGanttQuery(projectId, from, to), CancellationToken.None);

        Assert.Equal(projectId, activityReader.LastProjectIdRequested);
        Assert.Equal(from, activityReader.LastFromRequested);
        Assert.Equal(to, activityReader.LastToRequested);
    }

    [Fact]
    public async Task Handle_Returns_The_Projects_Real_DataDate_Not_The_Row_Orderers_Placeholder()
    {
        // Gap closure (post-S6-FE-01): GanttRowOrderer.Build has no reason to read Project (it's a
        // pure, in-memory row-ordering builder) and fills DataDate with a placeholder the handler
        // must overwrite. This proves the handler's `with { DataDate = ... }` actually replaces it
        // with the real value from IGanttActivityReader.GetProjectDataDateAsync, not the placeholder.
        var expectedDataDate = DateTimeOffset.Parse("2026-03-15T00:00:00+07:00");
        var wbsTreeReader = new FakeWbsTreeReader();
        var activityReader = new FakeGanttActivityReader { DataDateToReturn = expectedDataDate };
        var handler = new GetGanttQueryHandler(wbsTreeReader, activityReader);

        var result = await handler.Handle(new GetGanttQuery(Guid.NewGuid(), null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedDataDate, result.Value.DataDate);
        Assert.NotEqual(DateTimeOffset.MinValue, result.Value.DataDate);
    }

    [Fact]
    public async Task Handle_Succeeds_With_No_Activities_When_The_Project_Has_None()
    {
        var wbsTreeReader = new FakeWbsTreeReader();
        var activityReader = new FakeGanttActivityReader();
        var handler = new GetGanttQueryHandler(wbsTreeReader, activityReader);

        var result = await handler.Handle(new GetGanttQuery(Guid.NewGuid(), null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Activities);
    }
}
