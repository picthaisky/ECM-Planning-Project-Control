using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Wbs.Queries.GetWbsTree;
using CMPlus.Application.Wbs;

namespace CMPlus.Application.Tests.Features.Wbs;

public class GetWbsTreeQueryHandlerTests
{
    private sealed class FakeWbsTreeReader : IWbsTreeReader
    {
        public bool ProjectExists { get; set; } = true;
        public IReadOnlyList<WbsNodeFlatRow> NodesToReturn { get; set; } = [];
        public Guid? LastProjectIdRequested { get; private set; }

        public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectExists);

        public Task<IReadOnlyList<WbsNodeFlatRow>> GetNodesWithActivityCountsAsync(
            Guid projectId, CancellationToken cancellationToken = default)
        {
            LastProjectIdRequested = projectId;
            return Task.FromResult(NodesToReturn);
        }
    }

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var reader = new FakeWbsTreeReader { ProjectExists = false };
        var handler = new GetWbsTreeQueryHandler(reader);

        var result = await handler.Handle(new GetWbsTreeQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WbsErrorCodes.ProjectNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Builds_The_Tree_From_The_Readers_Flat_Rows()
    {
        var projectId = Guid.NewGuid();
        var root = new WbsNodeFlatRow(Guid.NewGuid(), null, "1", "Structure", 100m, 5);
        var reader = new FakeWbsTreeReader { NodesToReturn = [root] };
        var handler = new GetWbsTreeQueryHandler(reader);

        var result = await handler.Handle(new GetWbsTreeQuery(projectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(projectId, result.Value.ProjectId);
        Assert.Equal(projectId, reader.LastProjectIdRequested);
        var rootDto = Assert.Single(result.Value.RootNodes);
        Assert.Equal(root.Id, rootDto.Id);
        Assert.Equal(5, rootDto.ActivityCount);
    }
}
