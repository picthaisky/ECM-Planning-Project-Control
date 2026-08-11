using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Weather;

/// <summary>Wire shape for a <see cref="DailyWeatherLog"/> (domain-rules.md weather-eot §8.1) -
/// shared by <c>RecordWeatherLogCommandHandler</c>, <c>RecordWeatherLogCorrectionCommandHandler</c>
/// and <c>ListWeatherLogsQueryHandler</c> (mirrors <c>VariationOrderDto</c>'s <c>From(entity)</c>
/// pattern).</summary>
public sealed record WeatherLogDto(
    Guid Id,
    Guid ProjectId,
    DateTimeOffset LogDate,
    WeatherCondition Condition,
    string? ConditionNote,
    decimal? RainfallMm,
    WeatherImpact Impact,
    string? ImpactNote,
    decimal? HoursLost,
    bool WorkStoppage,
    WeatherLogEntryKind EntryKind,
    Guid? CorrectsWeatherLogId,
    string? CorrectionReason,
    IReadOnlyList<Guid> AffectedActivityIds,
    Guid RecordedByUserId,
    DateTimeOffset RecordedAt)
{
    public static WeatherLogDto From(DailyWeatherLog log) => new(
        log.Id,
        log.ProjectId,
        log.LogDate,
        log.Condition,
        log.ConditionNote,
        log.RainfallMm,
        log.Impact,
        log.ImpactNote,
        log.HoursLost,
        log.WorkStoppage,
        log.EntryKind,
        log.CorrectsWeatherLogId,
        log.CorrectionReason,
        log.AffectedActivities.Select(a => a.ActivityId).ToList(),
        log.RecordedByUserId,
        log.RecordedAt);
}
