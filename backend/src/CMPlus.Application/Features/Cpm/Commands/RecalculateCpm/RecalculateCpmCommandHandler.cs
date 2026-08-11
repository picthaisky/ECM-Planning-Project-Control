using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using MediatR;

namespace CMPlus.Application.Features.Cpm.Commands.RecalculateCpm;

public sealed class RecalculateCpmCommandHandler(
    ICpmScheduleRepository repository, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IRequestHandler<RecalculateCpmCommand, Result<RecalculateCpmResultDto>>
{
    public async Task<Result<RecalculateCpmResultDto>> Handle(
        RecalculateCpmCommand request, CancellationToken cancellationToken)
    {
        var project = await repository.GetProjectContextAsync(request.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<RecalculateCpmResultDto>.Failure(CpmErrorCodes.ProjectNotFound);
        }

        // ADR-0019: fail closed on a null actor - CpmRun.TriggeredByUserId must never be
        // fabricated (same L-01 discipline as VariationOrder/DailyWeatherLog). Structurally
        // unreachable behind [Authorize] in production, checked before anything else runs so a
        // rejected request never even loads the schedule graph.
        if (currentUser.UserId is not { } triggeredByUserId)
        {
            return Result<RecalculateCpmResultDto>.Failure(CpmErrorCodes.ActorRequired);
        }

        var graph = await repository.LoadScheduleGraphAsync(request.ProjectId, cancellationToken);

        var activityInputs = graph.Activities.Values
            .Select(a => new CpmActivityInput(a.Id, a.DurationDays))
            .ToList();
        var relationInputs = graph.Relations
            .Select(r => new CpmRelationInput(r.PredecessorActivityId, r.SuccessorActivityId, r.RelationType, r.LagDays))
            .ToList();

        var calculation = CpmEngine.Calculate(activityInputs, relationInputs);

        // A rejected graph (cycle/duplicate/unknown-activity relation) never mutates a single
        // Activity - the whole recalculation is all-or-nothing, same discipline as S4-BE-03's
        // batch progress all-or-nothing rule, just enforced by the engine running entirely before
        // any write instead of a pre-check loop. It also never captures a CpmRun (ADR-0019) - only
        // a genuine, complete recalculation is history worth keeping.
        if (calculation.IsFailure)
        {
            return Result<RecalculateCpmResultDto>.Failure(calculation.Error);
        }

        // Still routed through Activity.SetCpmResults (the domain's own mutator, mutating the
        // tracked in-memory instances LoadScheduleGraphAsync returned) for domain-level
        // correctness/testability, even though persistence itself (below) no longer relies on the
        // EF Core change tracker to notice these mutations - see ICpmScheduleRepository's remarks.
        foreach (var activityResult in calculation.Value.Activities)
        {
            graph.Activities[activityResult.ActivityId].SetCpmResults(
                activityResult.IsCritical, activityResult.TotalFloat, activityResult.FreeFloat);
        }

        var writeBacks = calculation.Value.Activities
            .Select(a => new CpmActivityWriteBack(a.ActivityId, a.IsCritical, a.TotalFloat, a.FreeFloat))
            .ToList();

        // ADR-0019: capture the append-only CpmRun snapshot from the SAME calculation the write-back
        // above used - never a second CpmEngine.Calculate call, which could in principle disagree
        // (it will not, CpmEngine is pure, but there is no reason to pay for or risk it).
        var durationByActivityId = activityInputs.ToDictionary(a => a.ActivityId, a => a.DurationDays);
        var cpmRun = CpmRun.Capture(
            tenantProvider.TenantId,
            request.ProjectId,
            clock.UtcNow,
            project.DataDate,
            calculation.Value.ProjectDurationDays,
            triggeredByUserId,
            request.Trigger,
            calculation.Value.Activities
                .Select(a => new CpmRunActivityInput(
                    a.ActivityId, durationByActivityId[a.ActivityId], a.EarlyStart, a.EarlyFinish, a.LateStart, a.LateFinish,
                    a.TotalFloat, a.FreeFloat, a.IsCritical))
                .ToList(),
            relationInputs
                .Select(r => new CpmRunRelationInput(r.PredecessorActivityId, r.SuccessorActivityId, r.RelationType, r.LagDays))
                .ToList());

        await repository.SaveResultsAsync(request.ProjectId, writeBacks, cpmRun, cancellationToken);

        var criticalActivityCount = calculation.Value.Activities.Count(a => a.IsCritical);

        return Result<RecalculateCpmResultDto>.Success(new RecalculateCpmResultDto(
            graph.Activities.Count, criticalActivityCount, calculation.Value.ProjectDurationDays, calculation.Value.CriticalPath));
    }
}
