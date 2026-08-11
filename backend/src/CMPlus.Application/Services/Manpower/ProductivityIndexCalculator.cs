namespace CMPlus.Application.Services.Manpower;

/// <summary>
/// S12-BE-02: pure Application-layer Productivity Index engine - domain-rules.md
/// (manpower-equipment) §5/§6/§7.4. No dependency on EF Core (or any other package) anywhere in this
/// class, mirroring <c>CMPlus.Application.Services.Evm.EvmEngine</c>'s "already-aggregated project-
/// wide scalars in, one small pure function out" shape exactly - the caller (a reader/query handler
/// in Infrastructure/Application) does the DB-dependent work of resolving the WBS subtree, matching
/// log rows to budgeted activities (§4.3) and summing the half-open-bucket totals (§4.5); this type
/// only ever turns already-summed scalars into a <see cref="ProductivityIndexResult"/>.
///
/// <para><b>Ratio-of-sums, by construction (§6).</b> There is deliberately no separate "aggregate
/// across scopes/time" method here: §6's ruling ("sum the numerators, sum the denominators, divide
/// once - never an unweighted mean of component indices") is satisfied simply by summing
/// <c>earnedManHours</c>/<c>actualManHoursInScope</c> across whatever scopes or time buckets the
/// caller is rolling up <b>before</b> calling <see cref="Compute"/> once - fixture M-03 (cross-scope)
/// and M-04 (cross-time) are the identical arithmetic operation from this engine's point of view.</para>
///
/// <para><b>No rounding until the very end (M-16).</b> The division is performed on full
/// <see cref="decimal"/> precision inputs and rounded exactly once with
/// <see cref="MidpointRounding.AwayFromZero"/> - never .NET's default banker's rounding, and never
/// by rounding intermediate per-scope ratios before summing (that would silently produce a different,
/// wrong answer - M-16's own warning).</para>
///
/// <para><b>PI never feeds anything (§3/§7.5).</b> This type has no output path to
/// <c>ActualCostEntry</c>/<c>EvmPeriodSnapshot</c>/any EVM metric - it is a pure function from scalars
/// to a read-model record, called only by PI's own query handler.</para>
/// </summary>
public static class ProductivityIndexCalculator
{
    /// <summary>Default plausibility band (§5.3/Q4) - outside this range the result still carries the
    /// real computed value plus an advisory <see cref="PiDataQualityWarning.ImplausiblePi"/>; it is
    /// never clamped and never suppressed (§5.7(g): "a genuine 3.5 exists and hiding it would be
    /// worse than explaining it").</summary>
    public const decimal PlausibleLowerBound = 0.20m;

    public const decimal PlausibleUpperBound = 3.00m;

    /// <summary>
    /// §5.2/§5.4/§5.5/§5.6/§5.7's full ruling in one function - see this type's remarks for the
    /// "already-aggregated scalars in" contract each parameter fulfils.
    /// </summary>
    /// <param name="earnedManHours">EMH: <c>Σ BMH_i · ΔP_i/100</c> over the in-scope, budgeted
    /// activities (§5.2) - may legitimately be negative (§5.7(h), a backdated downward correction).</param>
    /// <param name="actualManHoursInScope">AMH: the sum of <c>ManHours</c> for log rows whose own
    /// scope (§4.3) matched at least one budgeted activity - PI's denominator (§5.6).</param>
    /// <param name="actualManHoursTotal">Every log row's hours in the bucket, matched or not - always
    /// <c>&gt;= actualManHoursInScope</c>; the difference is <see cref="ProductivityIndexResult.ExcludedManHours"/>
    /// and always still appears on the histogram (§5.6/§4.3).</param>
    /// <param name="logEntryCount">Total log rows in the bucket (matched or not) - what distinguishes
    /// "not reported" from "reported as zero" (§5.7(a) vs (b), ADR-0013(f)).</param>
    /// <param name="anyActivityInScope">Does the queried scope resolve to at least one Activity at
    /// all (§4.3's scope(l) non-empty)? <see langword="false"/> ⟹ <see cref="PiNullReason.NoMatchingBudgetedScope"/>.</param>
    /// <param name="anyBudgetedActivityInScope">Of the in-scope activities, does at least one carry a
    /// non-null <c>BudgetManHours</c> (§5.4)? An explicit <c>0.00</c> counts as budgeted here
    /// (§5.7(f)) - only <see langword="null"/> excludes an activity.</param>
    /// <param name="hasProgressObservationInPeriod">§5.5's reporting-cadence gate: at least one
    /// <c>ActivityProgressLog</c> entry for an in-scope activity falls inside the bucket.</param>
    /// <param name="hasExplicitZeroBudgetWithMatchedHours">§5.7(f): at least one in-scope activity
    /// has <c>BudgetManHours == 0.00</c> explicitly (not null) and matched hours were logged against
    /// its scope - drives the advisory <see cref="PiDataQualityWarning.UnbudgetedLabourHours"/>.</param>
    public static ProductivityIndexResult Compute(
        decimal earnedManHours,
        decimal actualManHoursInScope,
        decimal actualManHoursTotal,
        int logEntryCount,
        bool anyActivityInScope,
        bool anyBudgetedActivityInScope,
        bool hasProgressObservationInPeriod,
        bool hasExplicitZeroBudgetWithMatchedHours = false)
    {
        var excludedManHours = actualManHoursTotal - actualManHoursInScope;
        var coveragePercentage = actualManHoursTotal == 0m
            ? 0m
            : Round(actualManHoursInScope / actualManHoursTotal * 100m);

        // §5.7(a)/(d): "no log rows at all" is checked before anything else - it must win over
        // every other reason, including NoBudgetManHours/NoMatchingBudgetedScope, because those
        // describe the *scope's* configuration, not whether anyone reported hours in this bucket.
        if (logEntryCount == 0)
        {
            var warnings = earnedManHours > 0m
                ? new[] { PiDataQualityWarning.ProgressWithoutManHours }
                : [];
            return new ProductivityIndexResult(
                null, PiNullReason.NotReported, earnedManHours, actualManHoursInScope, actualManHoursTotal,
                excludedManHours, coveragePercentage, logEntryCount, warnings);
        }

        // §4.3's empty-match-set case: the queried scope has no Activity under it at all.
        if (!anyActivityInScope)
        {
            return new ProductivityIndexResult(
                null, PiNullReason.NoMatchingBudgetedScope, earnedManHours, actualManHoursInScope,
                actualManHoursTotal, excludedManHours, coveragePercentage, logEntryCount, []);
        }

        // §5.4's DoD "-" case: activities exist, none of them were ever estimated in hours.
        if (!anyBudgetedActivityInScope)
        {
            return new ProductivityIndexResult(
                null, PiNullReason.NoBudgetManHours, earnedManHours, actualManHoursInScope,
                actualManHoursTotal, excludedManHours, coveragePercentage, logEntryCount, []);
        }

        // §5.5's reporting-cadence ruling: a bucket finer than the progress-observation interval
        // must read "-", never a naive (and wrong) 0.00 for the days progress was not re-measured.
        if (!hasProgressObservationInPeriod)
        {
            return new ProductivityIndexResult(
                null, PiNullReason.NoProgressInPeriod, earnedManHours, actualManHoursInScope,
                actualManHoursTotal, excludedManHours, coveragePercentage, logEntryCount, []);
        }

        // §5.7(b): rows exist, but none of them matched a budgeted scope (or they matched and summed
        // to exactly zero) - distinct Thai copy from NotReported, which is why LogEntryCount is
        // carried on the result at all (ADR-0013(f)).
        if (actualManHoursInScope == 0m)
        {
            return new ProductivityIndexResult(
                null, PiNullReason.NoActualManHours, earnedManHours, actualManHoursInScope,
                actualManHoursTotal, excludedManHours, coveragePercentage, logEntryCount, []);
        }

        // §5.7's division guard: checked by the numerator's existence above, never by try/catch - no
        // NaN, no Infinity, no exception, ever (the last line of §5.7).
        var value = Round(earnedManHours / actualManHoursInScope);

        var resultWarnings = new List<PiDataQualityWarning>();
        if (value < PlausibleLowerBound || value > PlausibleUpperBound)
        {
            // §5.3/§5.7(g): advisory only - never changes the colour or the value.
            resultWarnings.Add(PiDataQualityWarning.ImplausiblePi);
        }

        if (earnedManHours < 0m)
        {
            // §5.7(h): a downward correction drove EMH negative - report it, never clamp.
            resultWarnings.Add(PiDataQualityWarning.NegativeEarnedHours);
        }

        if (hasExplicitZeroBudgetWithMatchedHours)
        {
            // §5.7(f): an explicit "no labour budgeted here" decision, distinct from NULL/excluded.
            resultWarnings.Add(PiDataQualityWarning.UnbudgetedLabourHours);
        }

        return new ProductivityIndexResult(
            value, null, earnedManHours, actualManHoursInScope, actualManHoursTotal, excludedManHours,
            coveragePercentage, logEntryCount, resultWarnings);
    }

    /// <summary>
    /// §7.4's advisory-only heuristic: progress that is itself derived from hours expended makes
    /// <c>EMH ≡ AMH</c> identically, so PI reads a perfect ~1.00 while measuring nothing (fixture
    /// M-12). Fires only after three or more <b>consecutive</b> non-null values within 0.01 of 1.00 -
    /// <paramref name="recentBucketValuesNewestLast"/> is the scope's own trailing window, oldest
    /// first. Never gates anything: the caller still shows the real (green, 1.00) value regardless.
    /// </summary>
    public static bool DetectCircularEarningBasisRisk(IReadOnlyList<decimal?> recentBucketValuesNewestLast)
    {
        const decimal tolerance = 0.01m;
        var consecutive = 0;

        foreach (var value in recentBucketValuesNewestLast)
        {
            if (value is { } v && Math.Abs(v - 1.00m) < tolerance)
            {
                consecutive++;
                if (consecutive >= 3)
                {
                    return true;
                }
            }
            else
            {
                consecutive = 0;
            }
        }

        return false;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
