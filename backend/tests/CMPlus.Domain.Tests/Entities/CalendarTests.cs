using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

public class CalendarTests
{
    [Fact]
    public void AddException_Attaches_To_The_Calendar_That_Created_It()
    {
        var tenantId = Guid.NewGuid();
        var calendar = new Calendar(tenantId, Guid.NewGuid(), "Standard", WorkingDays.Weekdays);

        var holiday = calendar.AddException(
            DateTimeOffset.Parse("2026-12-31T00:00:00+07:00"), isWorkingDay: false, description: "New Year's Eve");

        Assert.Equal(calendar.Id, holiday.CalendarId);
        Assert.Equal(tenantId, holiday.TenantId);
        Assert.False(holiday.IsWorkingDay);
        Assert.Equal("New Year's Eve", holiday.Description);
    }

    [Fact]
    public void SetWorkingDays_Updates_The_Mask()
    {
        var calendar = new Calendar(Guid.NewGuid(), Guid.NewGuid(), "6-day week", WorkingDays.Weekdays);

        calendar.SetWorkingDays(WorkingDays.Weekdays | WorkingDays.Saturday);

        Assert.True(calendar.WorkingDays.HasFlag(WorkingDays.Saturday));
        Assert.False(calendar.WorkingDays.HasFlag(WorkingDays.Sunday));
    }
}
