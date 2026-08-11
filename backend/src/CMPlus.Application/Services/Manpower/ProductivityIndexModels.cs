namespace CMPlus.Application.Services.Manpower;

/// <summary>
/// docs/specs/manpower-equipment/domain-rules.md §5.4, adopted verbatim from ADR-0013(f)'s
/// <c>ActualCostResult</c> precedent: <see cref="Value"/> is <see langword="null"/> - rendered "-" -
/// whenever earned hours are unknown or the bucket is otherwise unusable, <b>never</b> <c>0m</c> as a
/// stand-in. <see cref="Reason"/> travels with a null <see cref="Value"/> so the frontend can show
/// the right Thai copy (§5.7's degenerate-case table gives each reason distinct wording).
///
/// <para><see cref="LogEntryCount"/> is what makes "no rows at all" (<see cref="PiNullReason.NotReported"/>,
/// fixture M-06a/d) distinguishable from "rows exist, reporting zero" (<see cref="PiNullReason.NoActualManHours"/>,
/// fixture M-06b) - the identical <c>EntryCount</c> discipline <c>ActualCostResult</c> already uses
/// for the same reason.</para>
///
/// <para><b>This type never appears anywhere near money.</b> §3's hard invariant: PI is a read-only
/// hours-only indicator that feeds no <c>ActualCostEntry</c>/<c>EvmPeriodSnapshot</c>/EVM metric -
/// see <see cref="ProductivityIndexCalculator"/>'s own remarks.</para>
/// </summary>
public sealed record ProductivityIndexResult(
    decimal? Value,
    PiNullReason? Reason,
    decimal EarnedManHours,
    decimal ActualManHoursInScope,
    decimal ActualManHoursTotal,
    decimal ExcludedManHours,
    decimal CoveragePercentage,
    int LogEntryCount,
    IReadOnlyList<PiDataQualityWarning> Warnings);

/// <summary>domain-rules.md §5.4/§5.7 - the exhaustive, closed set of reasons a
/// <see cref="ProductivityIndexResult.Value"/> can be null. Every member maps to distinct Thai UI
/// copy (S12-FE-02); none of them is "0" wearing a different hat.</summary>
public enum PiNullReason
{
    /// <summary>No log rows at all in the period (§5.7(a)/(d), fixture M-06a/d).</summary>
    NotReported = 1,

    /// <summary>Rows exist, but the matched hours sum to 0.00 (§5.7(b), fixture M-06b) - distinct
    /// Thai copy from <see cref="NotReported"/> is why <see cref="ProductivityIndexResult.LogEntryCount"/>
    /// exists on the result at all.</summary>
    NoActualManHours = 2,

    /// <summary>No in-scope activity has a non-null <c>BudgetManHours</c> (§5.4, fixture M-06e) - the
    /// DoD's "-" case.</summary>
    NoBudgetManHours = 3,

    /// <summary>The bucket contains no progress observation for the in-scope activities (§5.5's
    /// reporting-cadence ruling, fixture M-08).</summary>
    NoProgressInPeriod = 4,

    /// <summary>The queried scope resolves to zero activities at all (§4.3's empty-match-set case,
    /// fixture M-06g queried directly at that scope).</summary>
    NoMatchingBudgetedScope = 5,

    /// <summary>Tier 1 asked for a per-work-category PI (§4.3's Tier 2 gate, Q3).</summary>
    ActivitiesNotCategorised = 6,
}

/// <summary>domain-rules.md §5.4/§5.7/§7.4 - advisory-only data-quality flags. None of these ever
/// changes <see cref="ProductivityIndexResult.Value"/> or gates anything (§5.3: "it does not change
/// the colour or the value; a genuine 3.5 exists and hiding it would be worse than explaining it").</summary>
public enum PiDataQualityWarning
{
    /// <summary><c>|PI|</c> outside [0.20, 3.00] (§5.3).</summary>
    ImplausiblePi = 1,

    /// <summary>Progress was recorded with no man-hours logged at all (§5.7(a)).</summary>
    ProgressWithoutManHours = 2,

    /// <summary>An explicit <c>BudgetManHours = 0.00</c> scope carried matched hours &gt; 0 (§5.7(f),
    /// fixture M-06f) - a deliberate "no labour budgeted here" decision, distinct from <c>NULL</c>.</summary>
    UnbudgetedLabourHours = 3,

    /// <summary>A correction drove <c>EMH</c> negative in the bucket (§5.7(h), fixture M-06h) - never
    /// clamped.</summary>
    NegativeEarnedHours = 4,

    /// <summary>Progress in this scope appears to be derived from hours themselves, so PI trivially
    /// reads ~1.00 while measuring nothing (§7.4, fixture M-12) - advisory only, fires after three
    /// consecutive such buckets, never gates anything.</summary>
    CircularEarningBasisRisk = 5,
}

/// <summary>domain-rules.md §5.1 - the manning ratio, deliberately never bound to the
/// <c>productivityIndex</c> identifier anywhere (§5.1's ruling; fixture M-02). No output term, no
/// agreed direction - a staffing-compliance measure only, never a performance index.</summary>
public sealed record ManningRatioResult(decimal? Value, int ActualWorkerCount, int? PlannedWorkerCount);

/// <summary>
/// The already-aggregated scalars <see cref="ProductivityIndexCalculator.Compute"/> needs for one
/// scope/period, resolved by <see cref="CMPlus.Application.Abstractions.IProductivityIndexReader"/> -
/// see that interface's remarks for why every field here is a plain summed/counted scalar and never
/// an EF Core type.
/// </summary>
public sealed record ProductivityIndexAggregate(
    decimal EarnedManHours,
    decimal ActualManHoursInScope,
    decimal ActualManHoursTotal,
    int LogEntryCount,
    bool AnyActivityInScope,
    bool AnyBudgetedActivityInScope,
    bool HasProgressObservationInPeriod,
    bool HasExplicitZeroBudgetWithMatchedHours);

/// <summary>The manning ratio's raw inputs for one scope/day - see
/// <see cref="CMPlus.Application.Abstractions.IProductivityIndexReader.GetManningInputsAsync"/>.</summary>
public sealed record ManpowerReportingInputs(int ActualWorkerCount, int? PlannedWorkerCount);

/// <summary>domain-rules.md §8 - equipment utilisation (hours-based, cost-relevant) and availability
/// (count-based, the prototype's "14 / 16" tile). Both <c>decimal(5,2)</c> percentages, both
/// deliberately kept out of <see cref="ProductivityIndexResult"/> - §8's "do not merge man-hours and
/// equipment-hours into one denominator" ruling (fixture M-10).</summary>
public sealed record EquipmentMetricsResult(decimal? UtilisationPercentage, decimal? AvailabilityPercentage);
