namespace CMPlus.Domain.Enums;

/// <summary>
/// Bitmask of which days of the week a <c>Calendar</c> treats as working days by default;
/// per-date overrides (holidays, added working days) live in <c>CalendarException</c>. Used by the
/// CPM working-day math (docs/9 §4/§5).
/// </summary>
[Flags]
public enum WorkingDays
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    All = Weekdays | Saturday | Sunday,
}
