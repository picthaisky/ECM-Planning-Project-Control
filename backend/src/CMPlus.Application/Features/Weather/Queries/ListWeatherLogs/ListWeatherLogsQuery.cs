using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Weather.Queries.ListWeatherLogs;

/// <summary><c>GET /api/v1/projects/{projectId}/weather-logs</c> (docs/9 §4 route) - the
/// project-scoped weather register, optionally bounded by an inclusive <paramref name="From"/>/
/// <paramref name="To"/> <c>LogDate</c> range (S11-DB-01: date range as an index seek).</summary>
public sealed record ListWeatherLogsQuery(
    Guid ProjectId, DateTimeOffset? From, DateTimeOffset? To) : IRequest<Result<IReadOnlyList<WeatherLogDto>>>;
