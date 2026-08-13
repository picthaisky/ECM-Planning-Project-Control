using CMPlus.Application.Features.Baseline.Queries.ListBaselines;

namespace CMPlus.Application.Tests.Features.Baseline;

public class ListBaselinesQueryHandlerTests
{
    private static Domain.Entities.Baseline Seed(
        FakeBaselineRepository repository, Guid projectId, string name, DateTimeOffset capturedAt, bool active = false)
    {
        var baseline = Domain.Entities.Baseline.Capture(
            Guid.NewGuid(), projectId, name, capturedAt, Guid.NewGuid(), 1_000_000.00m, []);
        if (active)
        {
            baseline.Activate();
        }

        repository.BaselinesById[baseline.Id] = baseline;
        return baseline;
    }

    [Fact]
    public async Task Handle_Returns_The_Projects_Baselines_Newest_First()
    {
        var repository = new FakeBaselineRepository();
        var projectId = Guid.NewGuid();
        var older = Seed(repository, projectId, "Baseline v1", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = Seed(repository, projectId, "Baseline v2", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), active: true);
        var handler = new ListBaselinesQueryHandler(repository);

        var result = await handler.Handle(new ListBaselinesQuery(projectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value,
            first =>
            {
                // Newest capture first.
                Assert.Equal(newer.Id, first.Id);
                Assert.Equal("Baseline v2", first.Name);
                Assert.True(first.IsActive);
                Assert.Equal(projectId, first.ProjectId);
                Assert.Equal(1_000_000.00m, first.Bac);
            },
            second =>
            {
                Assert.Equal(older.Id, second.Id);
                Assert.False(second.IsActive);
            });
    }

    [Fact]
    public async Task Handle_Excludes_Other_Projects_Baselines()
    {
        var repository = new FakeBaselineRepository();
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var mine = Seed(repository, projectId, "Mine", DateTimeOffset.UtcNow);
        Seed(repository, otherProjectId, "Theirs", DateTimeOffset.UtcNow);
        var handler = new ListBaselinesQueryHandler(repository);

        var result = await handler.Handle(new ListBaselinesQuery(projectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var only = Assert.Single(result.Value);
        Assert.Equal(mine.Id, only.Id);
    }

    [Fact]
    public async Task Handle_Returns_An_Empty_List_For_A_Project_With_No_Baselines_Never_Fails()
    {
        var repository = new FakeBaselineRepository();
        var handler = new ListBaselinesQueryHandler(repository);

        var result = await handler.Handle(new ListBaselinesQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
