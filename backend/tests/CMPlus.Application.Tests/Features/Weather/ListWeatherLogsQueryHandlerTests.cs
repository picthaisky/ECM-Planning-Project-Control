using CMPlus.Application.Features.Weather;
using CMPlus.Application.Features.Weather.Queries.ListWeatherLogs;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Weather;

public class ListWeatherLogsQueryHandlerTests
{
    [Fact]
    public async Task Handle_Returns_InvalidDateRange_When_From_Is_Later_Than_To_Never_Queries_The_Repository()
    {
        var repository = new FakeDailyWeatherLogRepository();
        var handler = new ListWeatherLogsQueryHandler(repository);

        var result = await handler.Handle(
            new ListWeatherLogsQuery(
                Guid.NewGuid(),
                DateTimeOffset.Parse("2026-07-15T00:00:00+07:00"),
                DateTimeOffset.Parse("2026-07-01T00:00:00+07:00")),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WeatherLogErrorCodes.InvalidDateRange, result.Error);
        Assert.Null(repository.LastListCall);
    }

    [Fact]
    public async Task Handle_Passes_The_Date_Range_Through_Unchanged()
    {
        var repository = new FakeDailyWeatherLogRepository();
        var handler = new ListWeatherLogsQueryHandler(repository);
        var projectId = Guid.NewGuid();
        var from = DateTimeOffset.Parse("2026-07-01T00:00:00+07:00");
        var to = DateTimeOffset.Parse("2026-07-15T00:00:00+07:00");

        var result = await handler.Handle(new ListWeatherLogsQuery(projectId, from, to), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((projectId, from, to), repository.LastListCall);
    }

    [Fact]
    public async Task Handle_Returns_An_Empty_List_Rather_Than_404_For_An_Unknown_Project()
    {
        // Established list-read precedent in this codebase (ListVariationOrdersQueryHandler/
        // ListEvmSnapshotsQueryHandler's identical remarks) - never leaks whether a project id
        // exists via a 404/200 distinction.
        var repository = new FakeDailyWeatherLogRepository { RowsToList = [] };
        var handler = new ListWeatherLogsQueryHandler(repository);

        var result = await handler.Handle(new ListWeatherLogsQuery(Guid.NewGuid(), null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Handle_Maps_Every_Row_To_A_Dto()
    {
        var log = DailyWeatherLog.CreateOriginal(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"),
            WeatherCondition.HeavyRain, "ฝนตกหนัก", 42.5m, WeatherImpact.FullStoppage,
            "หยุดเทคอนกรีตโซน B ครึ่งวัน", 8.00m, Guid.NewGuid(),
            DateTimeOffset.Parse("2026-07-11T18:00:00+07:00"), []);
        var repository = new FakeDailyWeatherLogRepository { RowsToList = [log] };
        var handler = new ListWeatherLogsQueryHandler(repository);

        var result = await handler.Handle(new ListWeatherLogsQuery(log.ProjectId, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = Assert.Single(result.Value);
        Assert.Equal(log.Id, dto.Id);
        Assert.Equal(log.RainfallMm, dto.RainfallMm);
        Assert.Equal(log.WorkStoppage, dto.WorkStoppage);
        Assert.Equal(log.EntryKind, dto.EntryKind);
    }
}
