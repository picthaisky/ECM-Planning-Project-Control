using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Weather.Commands.RecordWeatherLogCorrection;

/// <summary>
/// <c>POST /api/v1/projects/{projectId}/weather-logs/{logId}/corrections</c> (S11-BE-01,
/// domain-rules.md weather-eot §8.3) - creates a <see cref="WeatherLogEntryKind.Correction"/> or
/// <see cref="WeatherLogEntryKind.Retraction"/> entry. <see cref="CorrectsWeatherLogId"/> is always
/// taken from the route (never the request body - §8.3's explicit instruction, so a client cannot
/// aim a correction at an arbitrary row by editing the payload while the URL says otherwise).
/// </summary>
public sealed record RecordWeatherLogCorrectionCommand(
    Guid ProjectId,
    Guid CorrectsWeatherLogId,
    WeatherLogEntryKind EntryKind,
    string CorrectionReason,
    DateTimeOffset LogDate,
    WeatherCondition Condition,
    string? ConditionNote,
    decimal? RainfallMm,
    WeatherImpact Impact,
    string? ImpactNote,
    decimal? HoursLost,
    IReadOnlyList<Guid> AffectedActivityIds) : IRequest<Result<WeatherLogDto>>;
