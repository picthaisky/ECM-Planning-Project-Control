using CMPlus.Domain.Enums;

namespace CMPlus.WebApi.Controllers.Weather;

/// <summary>Request body for <c>POST /api/v1/projects/{projectId}/weather-logs</c> (Original entries
/// only - domain-rules.md weather-eot §8.3).</summary>
public sealed record RecordWeatherLogRequest(
    DateTimeOffset LogDate,
    WeatherCondition Condition,
    string? ConditionNote,
    decimal? RainfallMm,
    WeatherImpact Impact,
    string? ImpactNote,
    decimal? HoursLost,
    IReadOnlyList<Guid>? AffectedActivityIds);

/// <summary>Request body for <c>POST /api/v1/projects/{projectId}/weather-logs/{logId}/corrections</c>.
/// <c>CorrectsWeatherLogId</c> is deliberately NOT a field here - it always comes from the route
/// (domain-rules.md §8.3).</summary>
public sealed record RecordWeatherLogCorrectionRequest(
    WeatherLogEntryKind EntryKind,
    string CorrectionReason,
    DateTimeOffset LogDate,
    WeatherCondition Condition,
    string? ConditionNote,
    decimal? RainfallMm,
    WeatherImpact Impact,
    string? ImpactNote,
    decimal? HoursLost,
    IReadOnlyList<Guid>? AffectedActivityIds);
