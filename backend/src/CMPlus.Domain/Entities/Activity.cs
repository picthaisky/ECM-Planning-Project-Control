using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// A schedule activity belonging to a <see cref="WBSNode"/> (docs/9 §4). <see cref="IsCritical"/>,
/// <see cref="TotalFloat"/> and <see cref="FreeFloat"/> are written by the Sprint 5 CPM engine, not
/// by any Sprint 1 code path.
/// </summary>
public sealed class Activity : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public Guid WbsNodeId { get; private set; }

    public string ActivityCode { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset PlannedStart { get; private set; }

    public DateTimeOffset PlannedFinish { get; private set; }

    public DateTimeOffset? ActualStart { get; private set; }

    public DateTimeOffset? ActualFinish { get; private set; }

    public int DurationDays { get; private set; }

    public decimal BudgetCost { get; private set; }

    /// <summary>
    /// Denormalised cache (ADR-0009) of the <see cref="ActivityProgressLog"/> entry with the
    /// greatest <see cref="LatestProgressPeriodEndDate"/> recorded so far; a backdated correction
    /// is appended via <see cref="RecordProgress"/> but never moves this cache.
    /// </summary>
    public decimal ProgressPercentage { get; private set; }

    /// <summary>
    /// PeriodEndDate of the log entry currently backing <see cref="ProgressPercentage"/>; null
    /// until the first entry is recorded. Cache-tracking metadata only - not itself one of
    /// design.md's listed Activity fields, but required to evaluate the ADR-0009 "is this the new
    /// maximum" rule in O(1) without reloading the whole ActivityProgressLog history on every
    /// write (a deliberate Sprint 1 modelling decision - see the backend-developer report).
    /// </summary>
    public DateTimeOffset? LatestProgressPeriodEndDate { get; private set; }

    /// <summary>RecordedAt of the log entry currently backing the cache; used only to break a tie
    /// when two entries share the same PeriodEndDate (ADR-0009).</summary>
    public DateTimeOffset? LatestProgressRecordedAt { get; private set; }

    public bool IsCritical { get; private set; }

    public int? TotalFloat { get; private set; }

    public int? FreeFloat { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private Activity()
    {
    }

    public Activity(
        Guid tenantId,
        Guid wbsNodeId,
        string activityCode,
        string name,
        DateTimeOffset plannedStart,
        DateTimeOffset plannedFinish,
        int durationDays,
        decimal budgetCost)
    {
        TenantId = tenantId;
        WbsNodeId = wbsNodeId;
        ActivityCode = ValidateCode(activityCode);
        Name = ValidateName(name);
        PlannedStart = plannedStart;
        PlannedFinish = plannedFinish;
        DurationDays = durationDays >= 0
            ? durationDays
            : throw new DomainException("DurationDays cannot be negative.");
        BudgetCost = MoneyGuard.EnsureNonNegative(budgetCost, nameof(BudgetCost));
    }

    /// <summary>
    /// Appends a new, immutable <see cref="ActivityProgressLog"/> row (ADR-0009). If
    /// <paramref name="periodEndDate"/> is at least the greatest one seen so far (ties broken by
    /// <paramref name="recordedAt"/>), <see cref="ProgressPercentage"/> moves to the new value;
    /// otherwise this is a backdated correction and the cache is left untouched. This is the only
    /// path that may change <see cref="ProgressPercentage"/> - no handler may assign it directly.
    /// </summary>
    public ActivityProgressLog RecordProgress(
        DateTimeOffset periodEndDate,
        decimal progressPercentage,
        decimal? actualQuantity,
        Guid recordedByUserId,
        ProgressSource source,
        DateTimeOffset recordedAt)
    {
        var clampedPct = PercentageGuard.Clamp(progressPercentage);

        var entry = new ActivityProgressLog(
            TenantId, Id, periodEndDate, clampedPct, actualQuantity, recordedByUserId, recordedAt, source);

        var movesCache = LatestProgressPeriodEndDate is null
            || periodEndDate > LatestProgressPeriodEndDate.Value
            || (periodEndDate == LatestProgressPeriodEndDate.Value
                && recordedAt >= LatestProgressRecordedAt!.Value);

        if (movesCache)
        {
            ProgressPercentage = clampedPct;
            LatestProgressPeriodEndDate = periodEndDate;
            LatestProgressRecordedAt = recordedAt;
        }

        return entry;
    }

    public void SetActuals(DateTimeOffset? actualStart, DateTimeOffset? actualFinish)
    {
        ActualStart = actualStart;
        ActualFinish = actualFinish;
    }

    /// <summary>Written by the CPM engine (Sprint 5), not by any Sprint 1 code path.</summary>
    public void SetCpmResults(bool isCritical, int? totalFloat, int? freeFloat)
    {
        IsCritical = isCritical;
        TotalFloat = totalFloat;
        FreeFloat = freeFloat;
    }

    private static string ValidateCode(string activityCode) =>
        string.IsNullOrWhiteSpace(activityCode)
            ? throw new DomainException("ActivityCode is required.")
            : activityCode.Trim();

    private static string ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new DomainException("Activity name is required.")
            : name.Trim();
}
