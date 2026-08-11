using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Services.Eot;

/// <summary>
/// S11-BE-02: domain-rules.md weather-eot §3's countability gates, as pure, independently-testable
/// functions - "a stoppage on activity j on day d is countable iff all six predicates hold ... the
/// first failure is recorded as the day's exclusion reason". <see cref="Evaluate"/> covers the
/// entry-level gates (in force is assumed already true - the caller passes only
/// $\mathcal{L}^{eff}$ - attributed/working-day/threshold, §3.1-§3.5); <see cref="CheckInWindow"/>
/// covers §3.7, which is necessarily per-activity rather than per-entry (a single entry can name
/// several activities, each with its own Actual/PlannedStart/ActualFinish).
///
/// <para>Reuses <c>WorkingCalendar.IsWorkingDay</c> unchanged (S5-BE-02) rather than re-implementing
/// calendar math - the only new logic here is classifying <i>why</i> a day is non-working
/// (<see cref="EotExclusionReason.NonWorkingDay"/> vs <see cref="EotExclusionReason.CalendarHoliday"/>,
/// fixture W-04/W-04b), which the shared calendar helper does not itself expose.</para>
/// </summary>
public static class EotCountabilityGate
{
    /// <summary><see cref="ExclusionReason"/> <see langword="null"/> ⟺ countable, in which case
    /// <see cref="DayWeight"/> is the day's contribution (1.00 under
    /// <see cref="EotPartialDayPolicy.FullDayOnly"/>/<see cref="EotPartialDayPolicy.ThresholdWholeDay"/>,
    /// <c>min(HoursLost, FullDayHours)</c> under <see cref="EotPartialDayPolicy.FractionalAccrual"/> -
    /// flooring the (already clamped) sum into whole days happens later, per (run, activity), never
    /// here). <see cref="HoursLostClampedToFullDay"/> is domain-rules.md §3.4/§8.5's disclosure flag -
    /// <see langword="true"/> only when the clamp actually bit, which by construction can only ever
    /// happen under <see cref="EotPartialDayPolicy.FractionalAccrual"/> (the other two policies compare
    /// <c>HoursLost</c> against a threshold and never sum it, so a value above <c>FullDayHours</c>
    /// changes nothing there - fixture W-18b).</summary>
    public readonly record struct GateResult(decimal DayWeight, EotExclusionReason? ExclusionReason, bool HoursLostClampedToFullDay = false)
    {
        public bool IsCountable => ExclusionReason is null;
    }

    public static GateResult Evaluate(EotWeatherEntryInput entry, EotPolicySettings policy, EotCalendarContext calendar)
    {
        // W-16b: weather was recorded, but no stoppage - the reason exists precisely so this is
        // never silently indistinguishable from an entry the evaluator never looked at.
        if (entry.Impact == WeatherImpact.NoImpact)
        {
            return new GateResult(0m, EotExclusionReason.NoStoppageRecorded);
        }

        // §3.2: a stoppage that names nothing is real evidence but not evaluable - never spread
        // across in-progress activities.
        if (entry.AffectedActivityIds.Count == 0)
        {
            return new GateResult(0m, EotExclusionReason.NoAffectedActivity);
        }

        // §3.3: "weekend" is whatever the calendar says; a CalendarException wins outright in
        // either direction (W-04/W-04b).
        if (!WorkingCalendar.IsWorkingDay(entry.LogDate, calendar.WorkingDays, calendar.Exceptions))
        {
            var reason = HasExplicitException(entry.LogDate, calendar.Exceptions)
                ? EotExclusionReason.CalendarHoliday
                : EotExclusionReason.NonWorkingDay;
            return new GateResult(0m, reason);
        }

        // §3.5: the rainfall depth test, when configured, combines with the hours test as AND.
        // Fail-closed on unmeasured rainfall unless the policy explicitly opts in (W-06).
        if (policy.MinRainfallMmForCountableDay is { } minRainfall)
        {
            if (entry.RainfallMm is null)
            {
                if (!policy.CountUnmeasuredRainfallWhenThresholdSet)
                {
                    return new GateResult(0m, EotExclusionReason.RainfallNotMeasured);
                }
            }
            else if (entry.RainfallMm.Value < minRainfall)
            {
                return new GateResult(0m, EotExclusionReason.BelowRainfallThreshold);
            }
        }

        // §3.4: "HoursLost IS NULL with Impact <> NoImpact => treat as a full day" - the impact
        // classification is the recorder's primary assertion; hours are a refinement, and a missing
        // refinement must not delete the primary assertion. Only reachable for legacy/imported rows,
        // since HoursLost is mandatory-by-validation for any row written through today's API.
        var hoursLost = entry.HoursLost ?? policy.FullDayHours;

        return policy.PartialDayPolicy switch
        {
            // Comparisons are inclusive (>=) in both remaining policies - "half a shift or more is
            // lost" includes exactly half a shift (§3.4, fixture W-05).
            EotPartialDayPolicy.FullDayOnly => hoursLost >= policy.FullDayHours
                ? new GateResult(1m, null)
                : new GateResult(0m, EotExclusionReason.BelowHoursThreshold),

            EotPartialDayPolicy.ThresholdWholeDay => hoursLost >= policy.MinHoursLostForCountableDay
                ? new GateResult(1m, null)
                : new GateResult(0m, EotExclusionReason.BelowHoursThreshold),

            // §3.4/§5.3's hypothesis H1: "one calendar day can never contribute more than one day of
            // duration" - hoursLost is clamped at FullDayHours (H) HERE, per date, before it ever
            // enters EotEvaluator's per-activity summation, never after summing/flooring. HoursLost is
            // validated to [0, 24] while H defaults to 8.00, so a legitimate two/three-shift
            // HoursLost = 24.00 would otherwise turn one calendar day into floor(24/8) = 3 duration
            // days - breaching the §5.3 cap (fixture W-18a). The clamp is never applied at write time
            // (RecordWeatherLogCommand) - H is per-project policy pinned per evaluation, and the log
            // row is immutable evidence (§3.4's own three-part rationale).
            EotPartialDayPolicy.FractionalAccrual => hoursLost > 0m
                ? new GateResult(Math.Min(hoursLost, policy.FullDayHours), null, HoursLostClampedToFullDay: hoursLost > policy.FullDayHours)
                : new GateResult(0m, EotExclusionReason.BelowHoursThreshold),

            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy.PartialDayPolicy, "Unsupported EotPartialDayPolicy."),
        };
    }

    /// <summary>
    /// §3.7: "the activity must have been performable on that day". Deliberately permissive for the
    /// not-yet-started case (a planned-but-not-yet-actual-started activity may still be stopped by
    /// weather) - the tighter reading is a concurrency argument and out of scope (§7, Q4).
    /// <see langword="null"/> return ⟺ in window (passes); otherwise the specific reason it failed.
    /// </summary>
    public static EotExclusionReason? CheckInWindow(EotActivityContext activity, DateOnly date)
    {
        var startDate = activity.ActualStart is { } actualStart
            ? DateOnly.FromDateTime(actualStart.DateTime)
            : DateOnly.FromDateTime(activity.PlannedStart.DateTime);

        if (date < startDate)
        {
            return EotExclusionReason.ActivityNotYetScheduled;
        }

        if (activity.ActualFinish is { } actualFinish && date > DateOnly.FromDateTime(actualFinish.DateTime))
        {
            return EotExclusionReason.ActivityAlreadyComplete;
        }

        return null;
    }

    private static bool HasExplicitException(DateOnly date, IReadOnlyList<CMPlus.Domain.Entities.CalendarException> exceptions)
    {
        foreach (var exception in exceptions)
        {
            // Calendar-day identity, not an instant - same convention WorkingCalendar's own
            // (private) FindException uses.
            if (DateOnly.FromDateTime(exception.Date.DateTime) == date)
            {
                return true;
            }
        }

        return false;
    }
}
