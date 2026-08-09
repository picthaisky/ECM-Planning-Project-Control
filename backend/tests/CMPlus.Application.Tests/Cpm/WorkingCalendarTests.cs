using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Cpm;

/// <summary>S5-BE-02: "holidays/CalendarException entries are skipped when counting duration" -
/// verified directly against Sprint 1's <see cref="Calendar"/>/<see cref="CalendarException"/>
/// entities (via <see cref="Calendar.AddException"/>, the only supported way to construct one).</summary>
public class WorkingCalendarTests
{
    private static readonly Calendar MonToFri = new(Guid.NewGuid(), Guid.NewGuid(), "Standard Mon-Fri", WorkingDays.Weekdays);

    [Fact]
    public void IsWorkingDay_Returns_False_For_A_Weekend_Under_A_MonFri_Calendar()
    {
        var saturday = new DateOnly(2026, 8, 1); // 2026-08-01 is a Saturday
        Assert.False(WorkingCalendar.IsWorkingDay(saturday, MonToFri.WorkingDays, []));
    }

    [Fact]
    public void IsWorkingDay_Returns_True_For_A_Weekday_Under_A_MonFri_Calendar()
    {
        var monday = new DateOnly(2026, 8, 3);
        Assert.True(WorkingCalendar.IsWorkingDay(monday, MonToFri.WorkingDays, []));
    }

    [Fact]
    public void IsWorkingDay_Returns_False_For_An_Explicit_Holiday_Exception_On_An_Otherwise_Working_Weekday()
    {
        var monday = new DateOnly(2026, 8, 3);
        var holiday = MonToFri.AddException(monday.ToDateTime(TimeOnly.MinValue), isWorkingDay: false, "Public holiday");

        Assert.False(WorkingCalendar.IsWorkingDay(monday, MonToFri.WorkingDays, [holiday]));
    }

    [Fact]
    public void IsWorkingDay_Returns_True_For_An_Explicit_Added_Working_Day_Exception_On_An_Otherwise_Non_Working_Weekend()
    {
        var saturday = new DateOnly(2026, 8, 1);
        var addedWorkingDay = MonToFri.AddException(saturday.ToDateTime(TimeOnly.MinValue), isWorkingDay: true, "Makeup workday");

        Assert.True(WorkingCalendar.IsWorkingDay(saturday, MonToFri.WorkingDays, [addedWorkingDay]));
    }

    [Fact]
    public void AddWorkingDays_Skips_Weekends_When_Advancing_A_Five_Working_Day_Duration()
    {
        // Monday 2026-08-03 + 5 working days, Mon-Fri calendar, no holidays: Tue,Wed,Thu,Fri,
        // (skip Sat/Sun), Mon 2026-08-10 is the 5th working day advanced over.
        var start = new DateOnly(2026, 8, 3);

        var finish = WorkingCalendar.AddWorkingDays(start, 5, WorkingDays.Weekdays, []);

        Assert.Equal(new DateOnly(2026, 8, 10), finish);
    }

    [Fact]
    public void AddWorkingDays_Also_Skips_An_Explicit_Holiday_Exception_Not_Just_Weekends()
    {
        // Same 5-working-day span as above, but Wednesday 2026-08-05 is declared a holiday -
        // the DoD's literal "holidays ... are skipped when counting duration": the resulting
        // finish date must land one calendar day later than the no-holiday case.
        var start = new DateOnly(2026, 8, 3);
        var holiday = MonToFri.AddException(new DateOnly(2026, 8, 5).ToDateTime(TimeOnly.MinValue), isWorkingDay: false, "Holiday");

        var finish = WorkingCalendar.AddWorkingDays(start, 5, WorkingDays.Weekdays, [holiday]);

        Assert.Equal(new DateOnly(2026, 8, 11), finish);
    }

    [Fact]
    public void AddWorkingDays_Advances_Past_A_Non_Working_Start_Date_Before_Counting()
    {
        // Starting on a Saturday (not a working day at all under Mon-Fri) - the activity cannot
        // actually start there, so day 0 must first roll forward to the next working day (Monday)
        // before any of the requested working days are counted.
        var saturday = new DateOnly(2026, 8, 1);

        var finish = WorkingCalendar.AddWorkingDays(saturday, 1, WorkingDays.Weekdays, []);

        // Rolls to Monday 2026-08-03 (day 0), then 1 working day forward -> Tuesday 2026-08-04.
        Assert.Equal(new DateOnly(2026, 8, 4), finish);
    }

    [Fact]
    public void CountWorkingDays_Excludes_Weekends_And_Holidays_From_The_Elapsed_Count()
    {
        // Mon 2026-08-03 (inclusive) .. Mon 2026-08-10 (exclusive) = 7 calendar days, of which
        // Sat 08-08 and Sun 08-09 are non-working -> 5 working days, minus the Wed 08-05 holiday
        // exception -> 4.
        var start = new DateOnly(2026, 8, 3);
        var endExclusive = new DateOnly(2026, 8, 10);
        var holiday = MonToFri.AddException(new DateOnly(2026, 8, 5).ToDateTime(TimeOnly.MinValue), isWorkingDay: false, "Holiday");

        var count = WorkingCalendar.CountWorkingDays(start, endExclusive, WorkingDays.Weekdays, [holiday]);

        Assert.Equal(4, count);
    }

    [Fact]
    public void A_Calendar_With_All_Seven_Days_Working_And_No_Exceptions_Never_Skips_Anything()
    {
        // The default calendar shape cpm-method.md's canonical fixture implicitly assumes (a plain
        // day-count with no gaps) - CpmEngine's ES/EF/LS/LF math relies on this being possible.
        var start = new DateOnly(2026, 8, 1); // a Saturday

        var finish = WorkingCalendar.AddWorkingDays(start, 5, WorkingDays.All, []);

        Assert.Equal(new DateOnly(2026, 8, 6), finish);
    }
}
