using System.Text.Json;
using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Eot;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using MediatR;

namespace CMPlus.Application.Features.Eot.Commands.EvaluateEot;

/// <summary>
/// S11-BE-02: orchestrates one <c>EotEvaluator.Evaluate</c> call - loads every input
/// (§3.3's default calendar, §3.5's policy or its documented defaults, §8.2's effective weather log
/// set, §4.1's governing <c>CpmRun</c> per distinct stoppage date, §3.7's activity context), then
/// persists exactly one new <see cref="EotEvaluation"/> via <see cref="IEotEvaluationRepository.SaveAsync"/>
/// (§2.1's zero-side-effect boundary - see that method's own remarks). All I/O; the actual TIA
/// arithmetic is entirely <c>EotEvaluator</c>'s (pure, no dependency on any interface in this file).
/// </summary>
public sealed class EvaluateEotCommandHandler(
    IEotEvaluationRepository repository,
    IDailyWeatherLogRepository weatherLogRepository,
    ICpmRunHistoryReader cpmRunHistoryReader,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<EvaluateEotCommand, Result<EotEvaluationDto>>
{
    public async Task<Result<EotEvaluationDto>> Handle(EvaluateEotCommand request, CancellationToken cancellationToken)
    {
        var project = await repository.GetProjectContextAsync(request.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<EotEvaluationDto>.Failure(EotErrorCodes.ProjectNotFound);
        }

        // Fail closed on a null actor id, mirroring RecordWeatherLogCommandHandler/
        // RecalculateCpmCommandHandler's identical L-01 guard - checked before any further I/O.
        if (currentUser.UserId is not { } evaluatedByUserId)
        {
            return Result<EotEvaluationDto>.Failure(EotErrorCodes.ActorRequired);
        }

        // §1: "Default: project start → data date".
        var windowStart = request.WindowStart ?? project.ContractStart;
        var windowEnd = request.WindowEnd ?? project.DataDate;
        if (windowEnd < windowStart)
        {
            return Result<EotEvaluationDto>.Failure(EotErrorCodes.InvalidWindowRange);
        }

        // §3.3: "No default calendar ⟹ block, never guess" - checked unconditionally, before any
        // weather data is even loaded.
        var calendar = await repository.GetDefaultCalendarAsync(request.ProjectId, cancellationToken);
        if (calendar is null)
        {
            return Result<EotEvaluationDto>.Failure(EotErrorCodes.ProjectCalendarNotConfigured);
        }

        // §4.4's fourth degraded-mode row: "No CpmRun at all ⟹ 422 refuse" - also checked
        // unconditionally (not only when a countable day is later found), the same fail-closed
        // discipline as the calendar gate above. This doubles as the §4.4 fallback run for every
        // date that has no direct governing-run hit.
        var earliestRun = await cpmRunHistoryReader.GetEarliestRunAsync(request.ProjectId, cancellationToken);
        if (earliestRun is null)
        {
            return Result<EotEvaluationDto>.Failure(EotErrorCodes.NoCpmRunAvailable);
        }

        var policyRow = await repository.GetPolicyAsync(request.ProjectId, cancellationToken);
        var policy = policyRow is null ? EotPolicySettings.Default : EotPolicySettings.From(policyRow);

        // §8.2: L^eff is computed over the WHOLE project history (a correction chain is not
        // date-scoped), then filtered to the window - never the reverse.
        var allLogs = await weatherLogRepository.ListByProjectAsync(request.ProjectId, from: null, to: null, cancellationToken);
        var effectiveLogs = EotEffectiveLogSet.Resolve(allLogs);

        var windowStartDate = DateOnly.FromDateTime(windowStart.DateTime);
        var windowEndDate = DateOnly.FromDateTime(windowEnd.DateTime);
        var entries = effectiveLogs
            .Select(l => new EotWeatherEntryInput(
                l.Id, DateOnly.FromDateTime(l.LogDate.DateTime), l.Impact, l.HoursLost, l.RainfallMm,
                l.AffectedActivities.Select(a => a.ActivityId).ToList()))
            .Where(e => e.LogDate >= windowStartDate && e.LogDate <= windowEndDate)
            .ToList();

        // §4.1's contemporaneous ruling: resolve the governing run per distinct stoppage date. Uses
        // the END of each calendar day so a same-day recalculation is correctly treated as "at or
        // before" - deliberately over-fetches (every distinct date among ALL in-window effective
        // entries, before §3's other gates run) rather than trying to predict which dates will end up
        // countable; EotEvaluator itself only ever reads the subset that actually matters for
        // Confidence/CriticalityBasis classification (its own remarks explain why).
        var governingRunsByDate = new Dictionary<DateOnly, CpmRun>();
        foreach (var date in entries.Select(e => e.LogDate).Distinct())
        {
            var asOf = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            var run = await cpmRunHistoryReader.GetGoverningRunAsync(request.ProjectId, asOf, cancellationToken);
            if (run is not null)
            {
                governingRunsByDate[date] = run;
            }
        }

        var activityIds = entries.SelectMany(e => e.AffectedActivityIds).Distinct().ToList();
        var activityContexts = await repository.GetActivityContextsAsync(request.ProjectId, activityIds, cancellationToken);

        var evaluatedAt = clock.UtcNow;
        var input = new EotEvaluationInput(
            request.ProjectId, windowStart, windowEnd, evaluatedAt, policy, calendar, entries, activityContexts,
            governingRunsByDate, earliestRun);

        var evaluated = EotEvaluator.Evaluate(input);
        if (evaluated.IsFailure)
        {
            return Result<EotEvaluationDto>.Failure(evaluated.Error);
        }

        var outcome = evaluated.Value;
        var policySnapshotJson = JsonSerializer.Serialize(policy);

        var evaluation = EotEvaluation.Capture(
            tenantProvider.TenantId,
            request.ProjectId,
            windowStart,
            windowEnd,
            evaluatedAt,
            evaluatedByUserId,
            outcome.CriticalityBasis,
            outcome.Confidence,
            outcome.AsScheduledDurationDays,
            outcome.ImpactedDurationDays,
            outcome.EotEligibleDays,
            outcome.CountableStoppageDayCount,
            outcome.SerialChainAbsorbedDayCount,
            outcome.DistinctCountableDateCount,
            outcome.UnattributedStoppageDayCount,
            policySnapshotJson,
            ToDateTimeOffset(outcome.LatestNoticeDate),
            outcome.NoticeWindowExpired,
            outcome.Runs
                .Select(r => new EotEvaluationRunInput(
                    r.CpmRunId, ToDateTimeOffset(r.WindowFrom)!.Value, ToDateTimeOffset(r.WindowTo)!.Value,
                    r.AsScheduledDurationDays, r.ImpactedDurationDays, r.DeltaDays))
                .ToList(),
            outcome.Sources
                .Select(s => new EotEvaluationSourceInput(
                    s.DailyWeatherLogId, s.CountableDays, s.ExclusionReason, s.HoursLostClampedToFullDay))
                .ToList(),
            outcome.Drivers
                .Select(d => new EotEvaluationDriverInput(
                    d.CpmRunId, d.ActivityId, d.ActivityCode, d.ActivityName, d.StoppageDays, d.TotalFloatAtRun,
                    d.WasCriticalAtRun, d.IsOnImpactedCriticalPath, d.IndicativeEotDays, d.MarginalEotDays,
                    d.RemainingFloatAfter, d.UnclaimedFractionalHours, d.SerialChainAbsorbedDays, d.AbsorbedIntoActivityCodes))
                .ToList());

        await repository.SaveAsync(evaluation, cancellationToken);

        return Result<EotEvaluationDto>.Success(EotEvaluationDto.From(evaluation));
    }

    private static DateTimeOffset? ToDateTimeOffset(DateOnly? date) =>
        date is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
}
