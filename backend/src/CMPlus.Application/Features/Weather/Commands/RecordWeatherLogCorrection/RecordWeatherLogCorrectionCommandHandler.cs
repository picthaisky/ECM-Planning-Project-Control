using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Weather.Commands.RecordWeatherLogCorrection;

/// <summary>
/// S11-BE-01: posts a <see cref="WeatherLogEntryKind.Correction"/>/<see cref="WeatherLogEntryKind.Retraction"/>
/// row. Enforces domain-rules.md §8.2's chain-integrity rules 1/2/4 (5 is FluentValidation +
/// the Domain constructor's own belt-and-braces check) - every check happens before anything is
/// constructed, all-or-nothing, mirroring every other handler in this codebase.
/// </summary>
public sealed class RecordWeatherLogCorrectionCommandHandler(
    IDailyWeatherLogRepository repository, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IRequestHandler<RecordWeatherLogCorrectionCommand, Result<WeatherLogDto>>
{
    public async Task<Result<WeatherLogDto>> Handle(RecordWeatherLogCorrectionCommand request, CancellationToken cancellationToken)
    {
        if (!await repository.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<WeatherLogDto>.Failure(WeatherLogErrorCodes.ProjectNotFound);
        }

        if (currentUser.UserId is not { } recordedByUserId)
        {
            return Result<WeatherLogDto>.Failure(WeatherLogErrorCodes.ActorRequired);
        }

        // Rule 1: the target must resolve within the same tenant and project.
        var target = await repository.GetByIdAsync(request.ProjectId, request.CorrectsWeatherLogId, cancellationToken);
        if (target is null)
        {
            return Result<WeatherLogDto>.Failure(WeatherLogErrorCodes.CorrectionTargetNotFound);
        }

        // Rule 2 (the load-bearing one): at most one entry may point at any given entry - a
        // correction must target the current chain tail, never an already-superseded entry.
        if (await repository.HasAnyCorrectionTargetingAsync(request.ProjectId, request.CorrectsWeatherLogId, cancellationToken))
        {
            return Result<WeatherLogDto>.Failure(WeatherLogErrorCodes.AlreadySuperseded);
        }

        var recordedAt = clock.UtcNow;

        // Rule 4: the target must already exist and be strictly older.
        if (target.RecordedAt >= recordedAt)
        {
            return Result<WeatherLogDto>.Failure(WeatherLogErrorCodes.CorrectionOrdering);
        }

        if (request.AffectedActivityIds.Count > 0)
        {
            var distinctActivityIds = request.AffectedActivityIds.Distinct().ToList();
            var existingActivityIds = await repository.FindExistingActivityIdsAsync(request.ProjectId, distinctActivityIds, cancellationToken);
            if (existingActivityIds.Count != distinctActivityIds.Count)
            {
                return Result<WeatherLogDto>.Failure(WeatherLogErrorCodes.UnknownActivity);
            }
        }

        var entry = request.EntryKind == WeatherLogEntryKind.Retraction
            ? DailyWeatherLog.CreateRetraction(
                tenantProvider.TenantId, request.ProjectId, request.CorrectsWeatherLogId, request.CorrectionReason,
                request.LogDate, request.Condition, request.ConditionNote, request.RainfallMm, request.Impact,
                request.ImpactNote, request.HoursLost, recordedByUserId, recordedAt, request.AffectedActivityIds)
            : DailyWeatherLog.CreateCorrection(
                tenantProvider.TenantId, request.ProjectId, request.CorrectsWeatherLogId, request.CorrectionReason,
                request.LogDate, request.Condition, request.ConditionNote, request.RainfallMm, request.Impact,
                request.ImpactNote, request.HoursLost, recordedByUserId, recordedAt, request.AffectedActivityIds);

        await repository.AddAsync(entry, cancellationToken);

        return Result<WeatherLogDto>.Success(WeatherLogDto.From(entry));
    }
}
