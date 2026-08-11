namespace CMPlus.Application.Features.Baseline.Queries.CompareBaseline;

/// <summary>
/// One activity's current-vs-baseline delta (S14-BE-02: "delta วัน (ปัจจุบัน − baseline)"). Every
/// variance field is <see langword="null"/> together when <see cref="IsRemoved"/> is
/// <see langword="true"/> (no live activity matches this snapshot's <c>ActivityId</c> any more -
/// see <c>IBaselineComparisonReader</c>'s remarks on why this is a defensive allowance, not a
/// reachable case today) - the baseline-side fields are always populated regardless, since every row
/// here originates from a captured <c>BaselineActivitySnapshot</c>.
/// </summary>
public sealed record BaselineActivityDeltaDto(
    Guid ActivityId,
    string? ActivityCode,
    string? Name,
    bool IsRemoved,
    DateTimeOffset BaselinePlannedStart,
    DateTimeOffset BaselinePlannedFinish,
    int BaselineDurationDays,
    decimal BaselineBudgetCost,
    DateTimeOffset? CurrentPlannedStart,
    DateTimeOffset? CurrentPlannedFinish,
    int? CurrentDurationDays,
    decimal? CurrentBudgetCost,
    bool? IsCritical,
    int? StartVarianceDays,
    int? FinishVarianceDays,
    int? DurationVarianceDays,
    decimal? BudgetVarianceAmount);

/// <summary>
/// S14-BE-02 response. The four prototype "เปรียบเทียบแผนปัจจุบัน vs {{ activeBlName }}" summary
/// tiles are reproduced as three fields here (<see cref="ProjectFinishVarianceDays"/>,
/// <see cref="DriftedActivityCount"/>/<see cref="TotalActivityCount"/>, <see cref="BacVarianceAmount"/>)
/// - the fourth ("Critical Path เปลี่ยนเส้นทาง") is deliberately absent: <c>BaselineActivitySnapshot</c>'s
/// field list (docs/10 §9/§3) does not capture <c>IsCritical</c>/float at capture time, so "did the
/// critical path change" is not reconstructable from what S14-DB-01/BE-01 actually persist. See the
/// backend-developer report - this is a gap to raise with domain-expert/system-architect, not
/// something this response fabricates an answer for.
/// </summary>
public sealed record BaselineComparisonDto(
    Guid ProjectId,
    Guid BaselineId,
    string BaselineName,
    DateTimeOffset BaselineCapturedAt,
    int TotalActivityCount,
    int DriftedActivityCount,
    int? ProjectFinishVarianceDays,
    decimal CurrentBac,
    decimal BaselineBac,
    decimal BacVarianceAmount,
    IReadOnlyList<BaselineActivityDeltaDto> Activities);
