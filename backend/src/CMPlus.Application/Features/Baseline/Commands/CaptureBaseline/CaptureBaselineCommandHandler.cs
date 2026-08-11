using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using MediatR;

namespace CMPlus.Application.Features.Baseline.Commands.CaptureBaseline;

public sealed class CaptureBaselineCommandHandler(
    IBaselineRepository repository, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IRequestHandler<CaptureBaselineCommand, Result<BaselineDto>>
{
    public async Task<Result<BaselineDto>> Handle(CaptureBaselineCommand request, CancellationToken cancellationToken)
    {
        // Fail closed on a null actor (this task's standing requirement, mirrors
        // RecalculateCpmCommandHandler/ADR-0019's identical "CpmRun.TriggeredByUserId must never be
        // fabricated" discipline) - checked before any read runs, so a rejected request never even
        // loads the schedule.
        if (currentUser.UserId is not { } capturedByUserId)
        {
            return Result<BaselineDto>.Failure(BaselineErrorCodes.ActorRequired);
        }

        var bac = await repository.GetProjectBacAsync(request.ProjectId, cancellationToken);
        if (bac is null)
        {
            return Result<BaselineDto>.Failure(BaselineErrorCodes.ProjectNotFound);
        }

        // One bounded query, never one round trip per activity - see IBaselineRepository's remarks.
        var sourceActivities = await repository.GetCurrentActivitiesAsync(request.ProjectId, cancellationToken);

        var baseline = Domain.Entities.Baseline.Capture(
            tenantProvider.TenantId,
            request.ProjectId,
            request.Name,
            clock.UtcNow,
            capturedByUserId,
            bac.Value,
            sourceActivities
                .Select(a => new BaselineActivitySnapshotInput(a.ActivityId, a.PlannedStart, a.PlannedFinish, a.DurationDays, a.BudgetCost))
                .ToList());

        // Never active on creation - a deliberate, separate ActivateBaselineCommand call is required
        // (see CaptureBaselineCommand's remarks). Persists the whole graph (Baseline + every
        // BaselineActivitySnapshot child) plus one summarizing AuditLog row.
        await repository.AddAsync(baseline, cancellationToken);

        return Result<BaselineDto>.Success(BaselineDto.From(baseline));
    }
}
