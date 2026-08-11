using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Services.Eot;

/// <summary>
/// S11-BE-02: one <see cref="Entities.DailyWeatherLog"/> entry as <see cref="EotEvaluator"/> sees it -
/// already resolved to the effective log set ($\mathcal{L}^{eff}$, domain-rules.md weather-eot §8.2)
/// by the caller; <see cref="EotEvaluator"/> itself only applies §3's remaining gates (in-window,
/// attributed, working day, threshold) plus §3.7 per named activity.
/// </summary>
public sealed record EotWeatherEntryInput(
    Guid Id,
    DateOnly LogDate,
    WeatherImpact Impact,
    decimal? HoursLost,
    decimal? RainfallMm,
    IReadOnlyList<Guid> AffectedActivityIds);

/// <summary>Just enough of an <see cref="Activity"/> for domain-rules.md §3.7's InWindow gate and
/// §5.4's driver "who" columns - deliberately not the full entity (mirrors <c>CpmActivityInput</c>'s
/// same minimalism).</summary>
public sealed record EotActivityContext(
    Guid ActivityId,
    string ActivityCode,
    string Name,
    DateTimeOffset? ActualStart,
    DateTimeOffset PlannedStart,
    DateTimeOffset? ActualFinish);

/// <summary>domain-rules.md weather-eot §3.5's <c>ProjectEotPolicy</c>, as the pure evaluator
/// consumes it - decoupled from the persisted <see cref="ProjectEotPolicy"/> entity so "no policy row
/// configured yet" (every field's documented default, §12 Q2/Q3) and "a policy row exists" both reach
/// <see cref="EotEvaluator"/> as the exact same shape.</summary>
public sealed record EotPolicySettings(
    EotPartialDayPolicy PartialDayPolicy,
    decimal FullDayHours,
    decimal MinHoursLostForCountableDay,
    decimal? MinRainfallMmForCountableDay,
    bool CountUnmeasuredRainfallWhenThresholdSet,
    int? NoticePeriodDays)
{
    /// <summary>domain-rules.md §12 Q2/Q3's stated defaults - applied whenever a project has no
    /// <see cref="ProjectEotPolicy"/> row.</summary>
    public static readonly EotPolicySettings Default = new(
        EotPartialDayPolicy.ThresholdWholeDay, FullDayHours: 8.00m, MinHoursLostForCountableDay: 4.00m,
        MinRainfallMmForCountableDay: null, CountUnmeasuredRainfallWhenThresholdSet: false, NoticePeriodDays: null);

    public static EotPolicySettings From(ProjectEotPolicy policy) => new(
        policy.PartialDayPolicy,
        policy.FullDayHours,
        policy.MinHoursLostForCountableDay,
        policy.MinRainfallMmForCountableDay,
        policy.CountUnmeasuredRainfallWhenThresholdSet,
        policy.NoticePeriodDays);
}

/// <summary>The governing calendar (domain-rules.md §3.3) as the pure evaluator consumes it - reuses
/// the real <see cref="CalendarException"/> entity type directly (rather than a parallel DTO) so
/// <c>WorkingCalendar.IsWorkingDay</c> can be called unchanged (this feature must not write a second
/// calendar implementation any more than it may write a second scheduling one).</summary>
public sealed record EotCalendarContext(WorkingDays WorkingDays, IReadOnlyList<CalendarException> Exceptions);

/// <summary>
/// The whole, fully-resolved input to one <see cref="EotEvaluator.Evaluate"/> call. Every field here
/// is already-loaded data - <see cref="EotEvaluator"/> performs no I/O of its own, exactly like
/// <c>CpmEngine</c>. <see cref="GoverningRunsByDate"/> only contains an entry for a date that actually
/// resolved to a governing run via <c>ICpmRunHistoryReader.GetGoverningRunAsync</c> (domain-rules.md
/// §4.1); a missing key means "use <see cref="EarliestRunFallback"/>" (§4.4's degraded mode), which
/// the caller must always supply once it has confirmed the project has at least one <c>CpmRun</c> at
/// all (the 422 <c>NoCpmRunAvailable</c> gate belongs to the caller, not this pure engine).
/// </summary>
public sealed record EotEvaluationInput(
    Guid ProjectId,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    DateTimeOffset EvaluatedAt,
    EotPolicySettings Policy,
    EotCalendarContext Calendar,
    IReadOnlyList<EotWeatherEntryInput> EffectiveEntries,
    IReadOnlyDictionary<Guid, EotActivityContext> Activities,
    IReadOnlyDictionary<DateOnly, CpmRun> GoverningRunsByDate,
    CpmRun EarliestRunFallback);

/// <summary>One <see cref="Entities.EotEvaluationRun"/>'s computed outcome (domain-rules.md §5.1).</summary>
public sealed record EotRunOutcome(
    Guid CpmRunId,
    DateOnly WindowFrom,
    DateOnly WindowTo,
    int AsScheduledDurationDays,
    int ImpactedDurationDays,
    int DeltaDays);

/// <summary>One considered weather log entry's computed fate (domain-rules.md §3).
/// <see cref="HoursLostClampedToFullDay"/> is §3.4/§8.5's disclosure flag: <see langword="true"/> when
/// this entry's recorded <c>HoursLost</c> exceeded the policy's <c>FullDayHours</c> and was charged as
/// <c>FullDayHours</c> instead (only ever possible under <see cref="EotPartialDayPolicy.FractionalAccrual"/> -
/// see <see cref="EotCountabilityGate.GateResult"/>'s own remarks) - so a reviewer can see that 24.00
/// was recorded and 8.00 was charged (fixture W-18).</summary>
public sealed record EotSourceOutcome(
    Guid DailyWeatherLogId, decimal CountableDays, EotExclusionReason? ExclusionReason, bool HoursLostClampedToFullDay = false);

/// <summary>One (governing run, activity) driver row's computed outcome (domain-rules.md §5.4).
/// <see cref="StoppageDays"/> ($S_j$) is what the schedule was charged - after
/// <see cref="SerialChainCollapse"/>'s §5.2a antichain reduction; <see cref="SerialChainAbsorbedDays"/>
/// is what the evidence additionally recorded against this activity but the network could not accept
/// twice, and <see cref="AbsorbedIntoActivityCodes"/> names which activity/activities absorbed it, in
/// topological order - <see langword="null"/> iff <see cref="SerialChainAbsorbedDays"/> is 0. Both
/// fields are 0/<see langword="null"/> for every domain-rules.md fixture except W-17 and W-20.</summary>
public sealed record EotDriverOutcome(
    Guid CpmRunId,
    Guid ActivityId,
    string ActivityCode,
    string ActivityName,
    int StoppageDays,
    int TotalFloatAtRun,
    bool WasCriticalAtRun,
    bool IsOnImpactedCriticalPath,
    int IndicativeEotDays,
    int MarginalEotDays,
    int RemainingFloatAfter,
    decimal? UnclaimedFractionalHours,
    int SerialChainAbsorbedDays = 0,
    string? AbsorbedIntoActivityCodes = null);

/// <summary>The whole, pure result of one <see cref="EotEvaluator.Evaluate"/> call - everything
/// <c>EvaluateEotCommandHandler</c> needs to build an <see cref="Entities.EotEvaluation"/> aggregate,
/// with no further computation of its own.</summary>
public sealed record EotEvaluationOutcome(
    EotCriticalityBasis CriticalityBasis,
    EotConfidence Confidence,
    int AsScheduledDurationDays,
    int ImpactedDurationDays,
    int EotEligibleDays,
    int CountableStoppageDayCount,
    int DistinctCountableDateCount,
    int UnattributedStoppageDayCount,
    DateOnly? LatestNoticeDate,
    bool? NoticeWindowExpired,
    IReadOnlyList<EotRunOutcome> Runs,
    IReadOnlyList<EotSourceOutcome> Sources,
    IReadOnlyList<EotDriverOutcome> Drivers,
    int SerialChainAbsorbedDayCount = 0);
