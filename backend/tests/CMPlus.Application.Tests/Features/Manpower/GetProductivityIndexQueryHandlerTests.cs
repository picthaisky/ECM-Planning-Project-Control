using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Manpower;
using CMPlus.Application.Features.Manpower.Queries.GetProductivityIndex;
using CMPlus.Application.Services.Manpower;

namespace CMPlus.Application.Tests.Features.Manpower;

internal sealed class FakeProductivityIndexReader : IProductivityIndexReader
{
    public bool ProjectExists { get; set; } = true;
    public bool WbsNodeExists { get; set; } = true;
    public bool ActivityExists { get; set; } = true;
    public ProductivityIndexAggregate Aggregate { get; set; } =
        new(0m, 0m, 0m, 0, false, false, false, false);
    public ManpowerReportingInputs ManningInputs { get; set; } = new(0, null);
    public (Guid ProjectId, Guid? WbsNodeId, Guid? ActivityId, DateTimeOffset From, DateTimeOffset To)? LastAggregateCall { get; private set; }
    public bool ManningInputsCalled { get; private set; }

    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(ProjectExists);

    public Task<bool> WbsNodeExistsInProjectAsync(Guid projectId, Guid wbsNodeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(WbsNodeExists);

    public Task<bool> ActivityExistsInProjectAsync(Guid projectId, Guid activityId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ActivityExists);

    public Task<ProductivityIndexAggregate> GetAggregateAsync(
        Guid projectId, Guid? wbsNodeId, Guid? activityId, DateTimeOffset periodStartExclusive, DateTimeOffset periodEndInclusive,
        CancellationToken cancellationToken = default)
    {
        LastAggregateCall = (projectId, wbsNodeId, activityId, periodStartExclusive, periodEndInclusive);
        return Task.FromResult(Aggregate);
    }

    public Task<ManpowerReportingInputs> GetManningInputsAsync(
        Guid projectId, Guid? wbsNodeId, DateTimeOffset logDate, CancellationToken cancellationToken = default)
    {
        ManningInputsCalled = true;
        return Task.FromResult(ManningInputs);
    }
}

/// <summary>S12-BE-02, fixture M-02 ★: <see cref="GetProductivityIndexQueryHandler"/> assembles
/// <see cref="ProductivityIndexResponseDto"/> with <c>ProductivityIndex</c> and <c>ManningRatio</c>
/// under distinct fields for the same single-day request - the load-bearing assertion this whole
/// feature exists to make impossible to get wrong.</summary>
public class GetProductivityIndexQueryHandlerTests
{
    [Fact]
    public async Task M02_A_Single_Day_Response_Carries_Both_ProductivityIndex_And_ManningRatio_Under_Distinct_Names()
    {
        // EMH=120.00, AMH=200.00 => PI=0.60. Actual 25 คน vs planned 20 คน => MR=1.25.
        var reader = new FakeProductivityIndexReader
        {
            Aggregate = new ProductivityIndexAggregate(120.00m, 200.00m, 200.00m, 1, true, true, true, false),
            ManningInputs = new ManpowerReportingInputs(25, 20),
        };
        var handler = new GetProductivityIndexQueryHandler(reader);

        var from = DateTimeOffset.Parse("2026-07-08T00:00:00+07:00");
        var to = DateTimeOffset.Parse("2026-07-09T00:00:00+07:00");
        var result = await handler.Handle(new GetProductivityIndexQuery(Guid.NewGuid(), null, null, from, to), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.60m, result.Value.ProductivityIndex);
        Assert.Equal(1.25m, result.Value.ManningRatio);
        Assert.NotEqual(result.Value.ProductivityIndex, result.Value.ManningRatio);
        Assert.True(reader.ManningInputsCalled);
    }

    [Fact]
    public async Task A_Multi_Day_Period_Query_Omits_ManningRatio_Entirely_Rather_Than_Inventing_An_Aggregation_Rule()
    {
        var reader = new FakeProductivityIndexReader
        {
            Aggregate = new ProductivityIndexAggregate(1_152.00m, 1_250.00m, 1_250.00m, 3, true, true, true, false),
        };
        var handler = new GetProductivityIndexQueryHandler(reader);

        var from = DateTimeOffset.Parse("2026-07-05T00:00:00+07:00");
        var to = DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"); // 3-day span
        var result = await handler.Handle(new GetProductivityIndexQuery(Guid.NewGuid(), null, null, from, to), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ManningRatio);
        Assert.False(reader.ManningInputsCalled);
    }

    [Fact]
    public async Task A_Cumulative_Query_Null_From_Reads_From_A_Sentinel_Project_Start()
    {
        var reader = new FakeProductivityIndexReader
        {
            Aggregate = new ProductivityIndexAggregate(4_560.00m, 4_800.00m, 4_800.00m, 5, true, true, true, false),
        };
        var handler = new GetProductivityIndexQueryHandler(reader);

        var to = DateTimeOffset.Parse("2026-07-11T00:00:00+07:00");
        var result = await handler.Handle(new GetProductivityIndexQuery(Guid.NewGuid(), null, null, null, to), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95m, result.Value.ProductivityIndex);
        Assert.Equal(DateTimeOffset.MinValue, reader.LastAggregateCall!.Value.From);
        Assert.Null(result.Value.ManningRatio); // From is null - not a single calendar day.
    }

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var reader = new FakeProductivityIndexReader { ProjectExists = false };
        var handler = new GetProductivityIndexQueryHandler(reader);

        var result = await handler.Handle(
            new GetProductivityIndexQuery(Guid.NewGuid(), null, null, null, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.ProjectNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_404_For_A_Wbs_Node_That_Does_Not_Resolve_In_This_Project_Never_A_Different_Status()
    {
        // Fixture M-06i/M-14: cross-tenant/unknown must be indistinguishable - always 404.
        var reader = new FakeProductivityIndexReader { WbsNodeExists = false };
        var handler = new GetProductivityIndexQueryHandler(reader);

        var result = await handler.Handle(
            new GetProductivityIndexQuery(Guid.NewGuid(), Guid.NewGuid(), null, null, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.WbsNodeNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_InvalidDateRange_When_From_Is_Later_Than_To()
    {
        var reader = new FakeProductivityIndexReader();
        var handler = new GetProductivityIndexQueryHandler(reader);

        var result = await handler.Handle(
            new GetProductivityIndexQuery(
                Guid.NewGuid(), null, null, DateTimeOffset.Parse("2026-07-10T00:00:00+07:00"),
                DateTimeOffset.Parse("2026-07-09T00:00:00+07:00")),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.InvalidDateRange, result.Error);
    }
}
