using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Cpm.Commands.RecalculateCpm;

public sealed class RecalculateCpmCommandHandler(ICpmScheduleRepository repository)
    : IRequestHandler<RecalculateCpmCommand, Result<RecalculateCpmResultDto>>
{
    public async Task<Result<RecalculateCpmResultDto>> Handle(
        RecalculateCpmCommand request, CancellationToken cancellationToken)
    {
        if (!await repository.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<RecalculateCpmResultDto>.Failure(CpmErrorCodes.ProjectNotFound);
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
        // any write instead of a pre-check loop.
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

        await repository.SaveResultsAsync(request.ProjectId, writeBacks, cancellationToken);

        var criticalActivityCount = calculation.Value.Activities.Count(a => a.IsCritical);

        return Result<RecalculateCpmResultDto>.Success(new RecalculateCpmResultDto(
            graph.Activities.Count, criticalActivityCount, calculation.Value.ProjectDurationDays, calculation.Value.CriticalPath));
    }
}
