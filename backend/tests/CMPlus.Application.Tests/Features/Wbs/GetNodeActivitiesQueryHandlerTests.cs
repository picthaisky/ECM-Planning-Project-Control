using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Wbs.Queries.GetNodeActivities;
using CMPlus.Application.Wbs;

namespace CMPlus.Application.Tests.Features.Wbs;

public class GetNodeActivitiesQueryHandlerTests
{
    private sealed class FakeWbsNodeActivitiesReader : IWbsNodeActivitiesReader
    {
        public bool NodeExists { get; set; } = true;
        public IReadOnlyList<ActivityForProgressDto> ActivitiesToReturn { get; set; } = [];
        public Guid? LastWbsNodeIdRequested { get; private set; }

        public Task<bool> NodeExistsInProjectAsync(Guid projectId, Guid wbsNodeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(NodeExists);

        public Task<IReadOnlyList<ActivityForProgressDto>> GetActivitiesForNodeAsync(
            Guid wbsNodeId, CancellationToken cancellationToken = default)
        {
            LastWbsNodeIdRequested = wbsNodeId;
            return Task.FromResult(ActivitiesToReturn);
        }
    }

    [Fact]
    public async Task Handle_Returns_NodeNotFound_When_The_Node_Does_Not_Belong_To_The_Project()
    {
        var reader = new FakeWbsNodeActivitiesReader { NodeExists = false };
        var handler = new GetNodeActivitiesQueryHandler(reader);

        var result = await handler.Handle(
            new GetNodeActivitiesQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WbsErrorCodes.NodeNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_The_Readers_Activities_When_The_Node_Exists()
    {
        var wbsNodeId = Guid.NewGuid();
        var activities = new List<ActivityForProgressDto>
        {
            new(Guid.NewGuid(), "A-001", "Excavation", 45.00m),
        };
        var reader = new FakeWbsNodeActivitiesReader { ActivitiesToReturn = activities };
        var handler = new GetNodeActivitiesQueryHandler(reader);

        var result = await handler.Handle(
            new GetNodeActivitiesQuery(Guid.NewGuid(), wbsNodeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(activities, result.Value);
        Assert.Equal(wbsNodeId, reader.LastWbsNodeIdRequested);
    }
}
