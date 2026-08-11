using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using MediatR;

namespace CMPlus.Application.Features.Weather.Commands.RecordWeatherLog;

/// <summary>
/// S11-BE-01: posts one immutable <see cref="DailyWeatherLog"/> Original row. Every existence check
/// happens before anything is constructed (mirrors <c>RecordActualCostCommandHandler</c>'s "check
/// everything, then mutate" ordering) so a bad reference never reaches the append-only log at all.
/// </summary>
public sealed class RecordWeatherLogCommandHandler(
    IDailyWeatherLogRepository repository, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IRequestHandler<RecordWeatherLogCommand, Result<WeatherLogDto>>
{
    public async Task<Result<WeatherLogDto>> Handle(RecordWeatherLogCommand request, CancellationToken cancellationToken)
    {
        if (!await repository.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<WeatherLogDto>.Failure(WeatherLogErrorCodes.ProjectNotFound);
        }

        // Sprint 10 L-01 fix pattern (this task's brief): fail closed on a null actor id rather than
        // fabricating Guid.Empty - structurally unreachable behind [Authorize] but never trusted here
        // either.
        if (currentUser.UserId is not { } recordedByUserId)
        {
            return Result<WeatherLogDto>.Failure(WeatherLogErrorCodes.ActorRequired);
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

        var log = DailyWeatherLog.CreateOriginal(
            tenantProvider.TenantId,
            request.ProjectId,
            request.LogDate,
            request.Condition,
            request.ConditionNote,
            request.RainfallMm,
            request.Impact,
            request.ImpactNote,
            request.HoursLost,
            recordedByUserId,
            clock.UtcNow,
            request.AffectedActivityIds);

        await repository.AddAsync(log, cancellationToken);

        return Result<WeatherLogDto>.Success(WeatherLogDto.From(log));
    }
}
