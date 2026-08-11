namespace CMPlus.Domain.Enums;

/// <summary>
/// domain-rules.md (weather-eot) §3: why a weather log entry's day did not become countable -
/// "the evaluator never silently drops a row" (fixture W-09), so every non-countable
/// <see cref="Entities.EotEvaluationSource"/> row carries one of these.
///
/// <para><b>Deliberately does not include</b> a value for §3.1's "Superseded"/"Retracted" (an entry
/// not in force - domain-rules.md §8.2) or a value for "the entry's own date is outside the
/// evaluation window": both are filtered out before the evaluator ever considers a candidate row
/// (<c>EotEvaluator</c>'s effective-log-set/window pre-filter), so no <see cref="Entities.EotEvaluationSource"/>
/// row for a still-in-window, in-force entry can ever carry either reason - see the S11-BE-02
/// backend-developer report for why persisting a row for every out-of-force/out-of-window entry
/// project-wide was rejected (fixture W-10 step 7 explicitly shows a superseded entry does NOT
/// appear in a later evaluation's sources at all, and it would not scale to a multi-year project's
/// full weather history).</para>
/// </summary>
public enum EotExclusionReason
{
    /// <summary>§3.2: <c>AffectedActivityIds</c> is empty (or every named id failed to resolve).</summary>
    NoAffectedActivity = 1,

    /// <summary>§3.3: the log date is not a working day on the governing calendar, and no
    /// <c>CalendarException</c> overrides it - an ordinary weekend.</summary>
    NonWorkingDay = 2,

    /// <summary>§3.3: an explicit <c>CalendarException(IsWorkingDay = false)</c> makes an
    /// otherwise-working day non-working (a declared holiday).</summary>
    CalendarHoliday = 3,

    /// <summary>§3.4: <c>HoursLost</c> did not clear the active <see cref="EotPartialDayPolicy"/>'s
    /// threshold (<c>FullDayHours</c> under <see cref="EotPartialDayPolicy.FullDayOnly"/>,
    /// <c>MinHoursLostForCountableDay</c> under <see cref="EotPartialDayPolicy.ThresholdWholeDay"/>).</summary>
    BelowHoursThreshold = 4,

    /// <summary>§3.5: a rainfall depth threshold is configured and <c>RainfallMm</c> is below it.</summary>
    BelowRainfallThreshold = 5,

    /// <summary>§3.5: a rainfall depth threshold is configured, <c>RainfallMm</c> is <c>NULL</c>,
    /// and the policy does not opt in to counting unmeasured rainfall (fail-closed default).</summary>
    RainfallNotMeasured = 6,

    /// <summary>§3.7: the stoppage date is after the named activity's <c>ActualFinish</c>.</summary>
    ActivityAlreadyComplete = 7,

    /// <summary>§3.7: the stoppage date is before the named activity could have started
    /// (<c>ActualStart</c> if set, else <c>PlannedStart</c>).</summary>
    ActivityNotYetScheduled = 8,

    /// <summary>W-16b: the entry is in force and in window but carries <c>Impact = NoImpact</c> -
    /// weather was recorded, but no stoppage.</summary>
    NoStoppageRecorded = 9,
}
