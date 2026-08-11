using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Weather.Commands.RecordWeatherLog;

/// <summary>
/// <c>POST /api/v1/projects/{projectId}/weather-logs</c> (S11-BE-01, domain-rules.md weather-eot
/// §8.3) - creates an <see cref="WeatherLogEntryKind.Original"/> entry only. A correction/retraction
/// is a separate command (<c>RecordWeatherLogCorrectionCommand</c>) hitting a separate route, per
/// §8.3's two-endpoint split. <c>RecordedByUserId</c>/<c>RecordedAt</c> are resolved server-side by
/// the handler (<c>ICurrentUserContext</c>/<c>IDateTimeProvider</c>), never trusted from the request.
/// </summary>
public sealed record RecordWeatherLogCommand(
    Guid ProjectId,
    DateTimeOffset LogDate,
    WeatherCondition Condition,
    string? ConditionNote,
    decimal? RainfallMm,
    WeatherImpact Impact,
    string? ImpactNote,
    decimal? HoursLost,
    IReadOnlyList<Guid> AffectedActivityIds) : IRequest<Result<WeatherLogDto>>;
