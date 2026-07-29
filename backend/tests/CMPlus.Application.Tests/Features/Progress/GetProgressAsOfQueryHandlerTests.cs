using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Progress.Queries.GetProgressAsOf;

namespace CMPlus.Application.Tests.Features.Progress;

public class GetProgressAsOfQueryHandlerTests
{
    private sealed class FakeActivityProgressReader : IActivityProgressReader
    {
        public Guid? LastActivityId { get; private set; }
        public DateTimeOffset? LastAsOf { get; private set; }
        public decimal ResultToReturn { get; set; }

        public Task<decimal> GetProgressAsOfAsync(Guid activityId, DateTimeOffset asOf, CancellationToken cancellationToken = default)
        {
            LastActivityId = activityId;
            LastAsOf = asOf;
            return Task.FromResult(ResultToReturn);
        }
    }

    [Fact]
    public async Task Handle_Delegates_To_The_Reader_With_The_Requested_Arguments()
    {
        var reader = new FakeActivityProgressReader { ResultToReturn = 42.50m };
        var handler = new GetProgressAsOfQueryHandler(reader);
        var activityId = Guid.NewGuid();
        var asOf = DateTimeOffset.Parse("2026-06-30T00:00:00+07:00");

        var result = await handler.Handle(new GetProgressAsOfQuery(activityId, asOf), CancellationToken.None);

        Assert.Equal(42.50m, result);
        Assert.Equal(activityId, reader.LastActivityId);
        Assert.Equal(asOf, reader.LastAsOf);
    }

    [Fact]
    public async Task Handle_Returns_Zero_When_The_Reader_Has_No_Entry()
    {
        var reader = new FakeActivityProgressReader { ResultToReturn = 0m };
        var handler = new GetProgressAsOfQueryHandler(reader);

        var result = await handler.Handle(
            new GetProgressAsOfQuery(Guid.NewGuid(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(0m, result);
    }
}
