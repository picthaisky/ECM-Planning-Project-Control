using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Baseline.Queries.CompareBaseline;

/// <summary>
/// Three bounded queries total (<see cref="IBaselineComparisonReader"/>'s own remarks): resolve the
/// baseline, resolve the current BAC, then one bulk projected join for every activity - never a
/// per-activity round trip, satisfying S14-BE-02's "10,000 กิจกรรมอยู่ในเกณฑ์ perf" DoD by
/// construction rather than by measurement (no real SQL Server is reachable in this environment to
/// measure against - Docker cannot start, docs/perf/gantt-frontend-s6.md §3;
/// <c>database-engineer</c>'s S14-DB-01 owns the index that makes the join a seek and the
/// row-size/volume estimate at that scale).
///
/// <para>Every day/money variance is computed here, in plain C#, from the raw component values the
/// reader already fetched - never inside the LINQ projection itself. See
/// <see cref="BaselineActivityComparisonRow"/>'s remarks for why (<c>EF.Functions.DateDiffDay</c>
/// has no InMemory translation) - the same "plain subtraction between two already-fetched values"
/// shape <c>GetCashFlowQueryHandler</c> already establishes for its own PV/EV/AC period deltas.</para>
/// </summary>
public sealed class CompareBaselineQueryHandler(IBaselineComparisonReader reader)
    : IRequestHandler<CompareBaselineQuery, Result<BaselineComparisonDto>>
{
    public async Task<Result<BaselineComparisonDto>> Handle(CompareBaselineQuery request, CancellationToken cancellationToken)
    {
        var currentBac = await reader.GetCurrentBacAsync(request.ProjectId, cancellationToken);
        if (currentBac is null)
        {
            return Result<BaselineComparisonDto>.Failure(BaselineErrorCodes.ProjectNotFound);
        }

        var baseline = request.BaselineId is { } explicitBaselineId
            ? await reader.FindBaselineAsync(request.ProjectId, explicitBaselineId, cancellationToken)
            : await reader.FindActiveBaselineAsync(request.ProjectId, cancellationToken);

        if (baseline is null)
        {
            // The project itself was already confirmed to exist above - an explicit baselineId that
            // does not resolve is BaselineErrorCodes.NotFound (a request-shape problem: the caller
            // named something that is not there), while an omitted one with no active baseline
            // configured is NoActiveBaseline (a project-state problem: the request is well-formed,
            // there is simply nothing to compare against yet) - never the same code for both, since
            // the caller's remedy differs (pick a real id vs activate a baseline first).
            return Result<BaselineComparisonDto>.Failure(
                request.BaselineId is not null ? BaselineErrorCodes.NotFound : BaselineErrorCodes.NoActiveBaseline);
        }

        var rows = await reader.GetActivityComparisonAsync(request.ProjectId, baseline.Id, cancellationToken);

        var activities = rows.Select(ToDeltaDto).ToList();

        // Aggregates derived entirely from the same already-fetched row set - no further query.
        var baselineProjectFinish = rows.Count == 0 ? (DateTimeOffset?)null : rows.Max(r => r.BaselinePlannedFinish);
        var currentFinishes = rows.Where(r => r.CurrentPlannedFinish is not null).Select(r => r.CurrentPlannedFinish!.Value).ToList();
        var currentProjectFinish = currentFinishes.Count == 0 ? (DateTimeOffset?)null : currentFinishes.Max();

        int? projectFinishVarianceDays = baselineProjectFinish is not null && currentProjectFinish is not null
            ? (currentProjectFinish.Value - baselineProjectFinish.Value).Days
            : null;

        // "Drift" = an activity whose current planned finish is later than its baseline finish
        // (prototype: "กิจกรรมเลื่อน ... กิจกรรมช้ากว่า Target") - a removed activity (no current
        // side at all) contributes neither a variance nor a drift count, since there is nothing
        // left to be "late".
        var driftedActivityCount = activities.Count(a => a.FinishVarianceDays is > 0);

        var response = new BaselineComparisonDto(
            request.ProjectId,
            baseline.Id,
            baseline.Name,
            baseline.CapturedAt,
            activities.Count,
            driftedActivityCount,
            projectFinishVarianceDays,
            RoundingRules.ToMoney(currentBac.Value),
            RoundingRules.ToMoney(baseline.Bac),
            RoundingRules.ToMoney(currentBac.Value - baseline.Bac),
            activities);

        return Result<BaselineComparisonDto>.Success(response);
    }

    private static BaselineActivityDeltaDto ToDeltaDto(BaselineActivityComparisonRow row)
    {
        if (row.CurrentPlannedStart is null || row.CurrentPlannedFinish is null
            || row.CurrentDurationDays is null || row.CurrentBudgetCost is null)
        {
            return new BaselineActivityDeltaDto(
                row.ActivityId,
                row.CurrentActivityCode,
                row.CurrentName,
                IsRemoved: true,
                row.BaselinePlannedStart,
                row.BaselinePlannedFinish,
                row.BaselineDurationDays,
                RoundingRules.ToMoney(row.BaselineBudgetCost),
                CurrentPlannedStart: null,
                CurrentPlannedFinish: null,
                CurrentDurationDays: null,
                CurrentBudgetCost: null,
                IsCritical: null,
                StartVarianceDays: null,
                FinishVarianceDays: null,
                DurationVarianceDays: null,
                BudgetVarianceAmount: null);
        }

        var startVarianceDays = (row.CurrentPlannedStart.Value - row.BaselinePlannedStart).Days;
        var finishVarianceDays = (row.CurrentPlannedFinish.Value - row.BaselinePlannedFinish).Days;
        var durationVarianceDays = row.CurrentDurationDays.Value - row.BaselineDurationDays;
        var budgetVarianceAmount = RoundingRules.ToMoney(row.CurrentBudgetCost.Value - row.BaselineBudgetCost);

        return new BaselineActivityDeltaDto(
            row.ActivityId,
            row.CurrentActivityCode,
            row.CurrentName,
            IsRemoved: false,
            row.BaselinePlannedStart,
            row.BaselinePlannedFinish,
            row.BaselineDurationDays,
            RoundingRules.ToMoney(row.BaselineBudgetCost),
            row.CurrentPlannedStart,
            row.CurrentPlannedFinish,
            row.CurrentDurationDays,
            RoundingRules.ToMoney(row.CurrentBudgetCost.Value),
            row.CurrentIsCritical,
            startVarianceDays,
            finishVarianceDays,
            durationVarianceDays,
            budgetVarianceAmount);
    }
}
