using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Progress.Commands.BatchRecordProgress;

public sealed class BatchRecordProgressCommandHandler(
    IBatchProgressRepository repository, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IRequestHandler<BatchRecordProgressCommand, Result<BatchRecordProgressResultDto>>
{
    public async Task<Result<BatchRecordProgressResultDto>> Handle(
        BatchRecordProgressCommand request, CancellationToken cancellationToken)
    {
        if (!await repository.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<BatchRecordProgressResultDto>.Failure(ProgressErrorCodes.ProjectNotFound);
        }

        var activityIds = request.Entries.Select(e => e.ActivityId).Distinct().ToArray();
        var activities = await repository.LoadActivitiesForProjectAsync(request.ProjectId, activityIds, cancellationToken);

        // All-or-nothing (S4-BE-03 DoD): every ActivityId is checked before anything is mutated in
        // memory, let alone saved - one unrecognised id fails the whole batch, never a partial
        // application of the other 19.
        if (activityIds.Any(id => !activities.ContainsKey(id)))
        {
            return Result<BatchRecordProgressResultDto>.Failure(ProgressErrorCodes.UnknownActivity);
        }

        // Security review sprint-15.md L-01b: fail closed on a null actor id rather than fabricating
        // Guid.Empty - structurally unreachable behind [Authorize] but never trusted here either.
        if (currentUser.UserId is not { } actorUserId)
        {
            return Result<BatchRecordProgressResultDto>.Failure(ProgressErrorCodes.ActorRequired);
        }

        // US-4.5/ADR-0009: every row goes through Activity.RecordProgress - the only path that may
        // append an ActivityProgressLog entry or move the ProgressPercentage cache. All entries
        // share the caller-supplied PeriodEndDate (S4-BE-03 DoD) but each gets its own freshly-read
        // RecordedAt - the same per-row clock read ImportExcelProgressCommandHandler already
        // established (Sprint 3 precedent).
        var newEntries = new List<ActivityProgressLog>(request.Entries.Count);
        foreach (var entry in request.Entries)
        {
            var activity = activities[entry.ActivityId];
            newEntries.Add(activity.RecordProgress(
                request.PeriodEndDate, entry.ProgressPercentage, entry.ActualQuantity, actorUserId,
                ProgressSource.Manual, clock.UtcNow));
        }

        await repository.SaveBatchAsync(request.ProjectId, request.PeriodEndDate, newEntries, cancellationToken);

        return Result<BatchRecordProgressResultDto>.Success(new BatchRecordProgressResultDto(newEntries.Count));
    }
}
