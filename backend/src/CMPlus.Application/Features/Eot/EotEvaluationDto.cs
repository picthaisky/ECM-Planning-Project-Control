using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Eot;

/// <summary>Wire shape for an <see cref="EotEvaluation"/> (domain-rules.md weather-eot §8.5) -
/// mirrors <c>WeatherLogDto</c>'s <c>From(entity)</c> pattern. <see cref="EotEligibleDays"/> is named
/// exactly as the domain document requires (§2.2: a schedule fact, not an entitlement) - never
/// renamed to anything implying entitlement anywhere on the wire.</summary>
public sealed record EotEvaluationDto(
    Guid Id,
    Guid ProjectId,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    DateTimeOffset EvaluatedAt,
    Guid EvaluatedByUserId,
    EotCriticalityBasis CriticalityBasis,
    EotConfidence Confidence,
    int AsScheduledDurationDays,
    int ImpactedDurationDays,
    int EotEligibleDays,
    int CountableStoppageDayCount,
    int SerialChainAbsorbedDayCount,
    int DistinctCountableDateCount,
    int UnattributedStoppageDayCount,
    bool ConcurrencyAssessed,
    bool EntitlementBasisAssessed,
    DateTimeOffset? LatestNoticeDate,
    bool? NoticeWindowExpired,
    IReadOnlyList<EotEvaluationRunDto> Runs,
    IReadOnlyList<EotEvaluationSourceDto> Sources,
    IReadOnlyList<EotEvaluationDriverDto> Drivers)
{
    public static EotEvaluationDto From(EotEvaluation evaluation) => new(
        evaluation.Id,
        evaluation.ProjectId,
        evaluation.WindowStart,
        evaluation.WindowEnd,
        evaluation.EvaluatedAt,
        evaluation.EvaluatedByUserId,
        evaluation.CriticalityBasis,
        evaluation.Confidence,
        evaluation.AsScheduledDurationDays,
        evaluation.ImpactedDurationDays,
        evaluation.EotEligibleDays,
        evaluation.CountableStoppageDayCount,
        evaluation.SerialChainAbsorbedDayCount,
        evaluation.DistinctCountableDateCount,
        evaluation.UnattributedStoppageDayCount,
        evaluation.ConcurrencyAssessed,
        evaluation.EntitlementBasisAssessed,
        evaluation.LatestNoticeDate,
        evaluation.NoticeWindowExpired,
        evaluation.Runs.Select(EotEvaluationRunDto.From).ToList(),
        evaluation.Sources.Select(EotEvaluationSourceDto.From).ToList(),
        evaluation.Drivers.Select(EotEvaluationDriverDto.From).ToList());
}

public sealed record EotEvaluationRunDto(
    Guid CpmRunId,
    DateTimeOffset WindowFrom,
    DateTimeOffset WindowTo,
    int AsScheduledDurationDays,
    int ImpactedDurationDays,
    int DeltaDays)
{
    public static EotEvaluationRunDto From(EotEvaluationRun run) => new(
        run.CpmRunId, run.WindowFrom, run.WindowTo, run.AsScheduledDurationDays, run.ImpactedDurationDays, run.DeltaDays);
}

public sealed record EotEvaluationSourceDto(
    Guid DailyWeatherLogId, decimal CountableDays, EotExclusionReason? ExclusionReason, bool HoursLostClampedToFullDay)
{
    public static EotEvaluationSourceDto From(EotEvaluationSource source) => new(
        source.DailyWeatherLogId, source.CountableDays, source.ExclusionReason, source.HoursLostClampedToFullDay);
}

public sealed record EotEvaluationDriverDto(
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
    int SerialChainAbsorbedDays,
    string? AbsorbedIntoActivityCodes)
{
    public static EotEvaluationDriverDto From(EotEvaluationDriver driver) => new(
        driver.CpmRunId, driver.ActivityId, driver.ActivityCode, driver.ActivityName, driver.StoppageDays,
        driver.TotalFloatAtRun, driver.WasCriticalAtRun, driver.IsOnImpactedCriticalPath, driver.IndicativeEotDays,
        driver.MarginalEotDays, driver.RemainingFloatAfter, driver.UnclaimedFractionalHours,
        driver.SerialChainAbsorbedDays, driver.AbsorbedIntoActivityCodes);
}
