using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Services.Cpm;

/// <summary>
/// S5-BE-02: pure working-day calendar math over <see cref="Calendar"/>/<see cref="CalendarException"/>
/// (Sprint 1 entities) - the "holidays/CalendarException entries are skipped when counting
/// duration" half of S5-BE-02's DoD. Deliberately decoupled from <see cref="CpmEngine"/>'s abstract
/// ES/EF/LS/LF day-count math (which stays calendar-agnostic on purpose, so the cpm-method.md
/// canonical fixture reproduces exactly regardless of which calendar a project uses): this class
/// answers the separate question "given an activity's calendar and a start date, which actual
/// calendar date does its Nth working day fall on" - the building block Sprint 6 (Gantt) will need
/// to render real bar dates from the CPM engine's day-count output. Not wired into
/// <c>RecalculateCpmCommand</c>'s persistence this sprint (<c>Activity</c> exposes no setter for a
/// calendar-adjusted date - only <c>IsCritical</c>/<c>TotalFloat</c>/<c>FreeFloat</c>); see the
/// backend-developer Sprint 5 report for this scope boundary.
/// </summary>
public static class WorkingCalendar
{
    /// <summary>True if <paramref name="date"/> is a working day: an explicit
    /// <see cref="CalendarException"/> for that date wins outright (whichever way it overrides);
    /// otherwise falls back to whether <paramref name="workingDays"/> includes that day of week.</summary>
    public static bool IsWorkingDay(DateOnly date, WorkingDays workingDays, IReadOnlyCollection<CalendarException> exceptions)
    {
        var overriding = FindException(date, exceptions);
        return overriding?.IsWorkingDay ?? (workingDays & ToFlag(date.DayOfWeek)) != 0;
    }

    /// <summary>
    /// Advances <paramref name="start"/> by exactly <paramref name="workingDaysToAdd"/> working
    /// days, skipping every non-working day (a weekend per <paramref name="workingDays"/>, or an
    /// explicit holiday <see cref="CalendarException"/>) - this is the literal "duration counts
    /// working days only, not calendar days" rule. <paramref name="start"/> is first advanced onto
    /// a working day if it is not already one (an activity cannot start on a holiday), then counted
    /// as day 0 of the activity - only the days advanced *past* that count towards
    /// <paramref name="workingDaysToAdd"/>, mirroring <c>EF = ES + D</c>.
    /// </summary>
    public static DateOnly AddWorkingDays(
        DateOnly start, int workingDaysToAdd, WorkingDays workingDays, IReadOnlyCollection<CalendarException> exceptions)
    {
        if (workingDaysToAdd < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workingDaysToAdd), workingDaysToAdd, "Working days to add cannot be negative.");
        }

        var current = start;
        while (!IsWorkingDay(current, workingDays, exceptions))
        {
            current = current.AddDays(1);
        }

        var remaining = workingDaysToAdd;
        while (remaining > 0)
        {
            current = current.AddDays(1);
            if (IsWorkingDay(current, workingDays, exceptions))
            {
                remaining--;
            }
        }

        return current;
    }

    /// <summary>Counts working days in <c>[startInclusive, endExclusive)</c>, skipping non-working
    /// days/holidays - the direct "count duration in working days, not calendar days" building
    /// block.</summary>
    public static int CountWorkingDays(
        DateOnly startInclusive, DateOnly endExclusive, WorkingDays workingDays, IReadOnlyCollection<CalendarException> exceptions)
    {
        if (endExclusive < startInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(endExclusive), endExclusive, "endExclusive cannot precede startInclusive.");
        }

        var count = 0;
        for (var date = startInclusive; date < endExclusive; date = date.AddDays(1))
        {
            if (IsWorkingDay(date, workingDays, exceptions))
            {
                count++;
            }
        }

        return count;
    }

    private static CalendarException? FindException(DateOnly date, IReadOnlyCollection<CalendarException> exceptions)
    {
        foreach (var exception in exceptions)
        {
            // Calendar dates are compared by their calendar-day identity (year/month/day), not as
            // an instant - the same convention docs/perf's own deterministic-seed dates use for a
            // DateTimeOffset that represents "a day", not a point in time.
            if (DateOnly.FromDateTime(exception.Date.DateTime) == date)
            {
                return exception;
            }
        }

        return null;
    }

    private static WorkingDays ToFlag(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => WorkingDays.Monday,
        DayOfWeek.Tuesday => WorkingDays.Tuesday,
        DayOfWeek.Wednesday => WorkingDays.Wednesday,
        DayOfWeek.Thursday => WorkingDays.Thursday,
        DayOfWeek.Friday => WorkingDays.Friday,
        DayOfWeek.Saturday => WorkingDays.Saturday,
        DayOfWeek.Sunday => WorkingDays.Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(dayOfWeek), dayOfWeek, "Unsupported day of week."),
    };
}
