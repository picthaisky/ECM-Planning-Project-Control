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
    /// domain-rules.md (manpower-equipment) §4.3/§5.4: <c>decimal(9,2)</c>, the estimator's labour
    /// build-up (ราคากลาง / BoQ labour content) or an imported P6 <c>Budgeted Labor Units</c> -
    /// Tier 1's required source of "budgeted man-hours" (BMH) for the Productivity Index.
    /// <see langword="null"/> means <b>not estimated in hours</b> - never 0.00, and never seeded with
    /// a placeholder (ADR-0015 discipline: a seeded default would be indistinguishable from a
    /// decision once in production data). <b>Deriving this from <see cref="BudgetCost"/> ÷ an
    /// assumed labour rate is forbidden</b> (§5.4 rule 4) - <see cref="BudgetCost"/> includes
    /// material, plant, subcontract and prelims, so dividing it by a guessed rate would produce a
    /// number with the shape of a budget and none of its meaning. Set only via
    /// <see cref="SetBudgetManHours"/>, mirroring <see cref="BudgetCost"/>'s own non-negative
    /// discipline.
    /// </summary>
    public decimal? BudgetManHours { get; private set; }

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

    /// <summary>
    /// S10-BE-03 (domain-rules.md §5.2): applies one <see cref="VariationOrderScopeItem.BudgetCostDelta"/>
    /// line to this activity's budget. This - never a directly-assigned negative-budget activity - is
    /// how a <c>Deduct</c> Variation Order is represented: <see cref="BudgetCost"/> stays
    /// <see cref="MoneyGuard.EnsureNonNegative"/> at every step, so a delta large enough to drive an
    /// individual activity's budget below zero throws here (the caller - <c>ApproveVariationOrderCommandHandler</c> -
    /// is expected to have already pre-validated this per-activity, the same defense-in-depth
    /// discipline every other domain guard in this codebase follows, so this is not the first place a
    /// well-formed request would ever hit it).
    /// </summary>
    public void AdjustBudgetCost(decimal delta) =>
        BudgetCost = MoneyGuard.EnsureNonNegative(BudgetCost + delta, nameof(BudgetCost));

    /// <summary>domain-rules.md (manpower-equipment) §4.3. <see langword="null"/> clears the
    /// estimate back to "not estimated in hours" - distinct from setting it to 0.00, which is a
    /// deliberate "no labour budgeted here" decision (§5.7(f), fixture M-06f).</summary>
    public void SetBudgetManHours(decimal? budgetManHours) =>
        BudgetManHours = MoneyGuard.EnsureNonNegative(budgetManHours, nameof(BudgetManHours));

    private static string ValidateCode(string activityCode) =>
        string.IsNullOrWhiteSpace(activityCode)
            ? throw new DomainException("ActivityCode is required.")
            : activityCode.Trim();

    private static string ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new DomainException("Activity name is required.")
            : name.Trim();
}
