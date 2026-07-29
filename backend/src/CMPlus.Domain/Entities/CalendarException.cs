using CMPlus.Domain.Common;

namespace CMPlus.Domain.Entities;

/// <summary>
/// A single-date override of a <see cref="Calendar"/>'s normal working-day pattern - a holiday
/// (non-working day) or an explicitly added working day (docs/9 §4). The constructor is
/// <c>internal</c>: the only supported way to create one is <see cref="Calendar.AddException"/>,
/// so every exception is always attached to the calendar that produced it.
/// </summary>
public sealed class CalendarException : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public Guid CalendarId { get; private set; }

    public DateTimeOffset Date { get; private set; }

    /// <summary>True = this date is an added working day overriding the calendar's normal
    /// pattern; false = this date is a non-working day/holiday.</summary>
    public bool IsWorkingDay { get; private set; }

    public string? Description { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private CalendarException()
    {
    }

    internal CalendarException(Guid tenantId, Guid calendarId, DateTimeOffset date, bool isWorkingDay, string? description)
    {
        TenantId = tenantId;
        CalendarId = calendarId;
        Date = date;
        IsWorkingDay = isWorkingDay;
        Description = description;
    }
}
