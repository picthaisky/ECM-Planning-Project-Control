using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

public class ActivityTests
{
    private static Activity CreateActivity() =>
        new(
            tenantId: Guid.NewGuid(),
            wbsNodeId: Guid.NewGuid(),
            activityCode: "A-100",
            name: "Excavation",
            plannedStart: DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"),
            plannedFinish: DateTimeOffset.Parse("2026-02-01T00:00:00+07:00"),
            durationDays: 30,
            budgetCost: 100_000m);

    [Fact]
    public void New_Activity_Has_Zero_Progress_And_No_Latest_Period()
    {
        var activity = CreateActivity();

        Assert.Equal(0m, activity.ProgressPercentage);
        Assert.Null(activity.LatestProgressPeriodEndDate);
    }

    [Fact]
    public void RecordProgress_With_New_Maximum_PeriodEndDate_Moves_The_Cache()
    {
        var activity = CreateActivity();
        var userId = Guid.NewGuid();
        var t0 = DateTimeOffset.Parse("2026-06-01T00:00:00+07:00");

        activity.RecordProgress(
            periodEndDate: DateTimeOffset.Parse("2026-06-07T00:00:00+07:00"),
            progressPercentage: 20m,
            actualQuantity: null,
            recordedByUserId: userId,
            source: ProgressSource.Manual,
            recordedAt: t0);

        Assert.Equal(20m, activity.ProgressPercentage);
        Assert.Equal(DateTimeOffset.Parse("2026-06-07T00:00:00+07:00"), activity.LatestProgressPeriodEndDate);

        activity.RecordProgress(
            periodEndDate: DateTimeOffset.Parse("2026-06-14T00:00:00+07:00"),
            progressPercentage: 35m,
            actualQuantity: 120.5m,
            recordedByUserId: userId,
            source: ProgressSource.Manual,
            recordedAt: t0.AddDays(7));

        Assert.Equal(35m, activity.ProgressPercentage);
        Assert.Equal(DateTimeOffset.Parse("2026-06-14T00:00:00+07:00"), activity.LatestProgressPeriodEndDate);
    }

    [Fact]
    public void RecordProgress_Backdated_Entry_Appends_But_Does_Not_Move_Cache()
    {
        var activity = CreateActivity();
        var userId = Guid.NewGuid();
        var t0 = DateTimeOffset.Parse("2026-06-01T00:00:00+07:00");

        activity.RecordProgress(
            DateTimeOffset.Parse("2026-06-14T00:00:00+07:00"), 35m, null, userId, ProgressSource.Manual, t0.AddDays(7));

        // A correction for an EARLIER period arrives after the fact.
        var backdated = activity.RecordProgress(
            DateTimeOffset.Parse("2026-06-07T00:00:00+07:00"), 99m, null, userId, ProgressSource.Manual, t0.AddDays(10));

        // The log entry itself is created (appended)...
        Assert.Equal(99m, backdated.ProgressPercentage);
        Assert.Equal(DateTimeOffset.Parse("2026-06-07T00:00:00+07:00"), backdated.PeriodEndDate);

        // ...but the Activity's cache still reflects the later period.
        Assert.Equal(35m, activity.ProgressPercentage);
        Assert.Equal(DateTimeOffset.Parse("2026-06-14T00:00:00+07:00"), activity.LatestProgressPeriodEndDate);
    }

    [Fact]
    public void RecordProgress_Same_PeriodEndDate_Tie_Broken_By_RecordedAt()
    {
        var activity = CreateActivity();
        var userId = Guid.NewGuid();
        var periodEnd = DateTimeOffset.Parse("2026-06-14T00:00:00+07:00");
        var t0 = DateTimeOffset.Parse("2026-06-15T09:00:00+07:00");

        activity.RecordProgress(periodEnd, 40m, null, userId, ProgressSource.Manual, recordedAt: t0);
        Assert.Equal(40m, activity.ProgressPercentage);

        // Same PeriodEndDate, but recorded LATER -> the later-recorded value wins the tie.
        activity.RecordProgress(periodEnd, 45m, null, userId, ProgressSource.Manual, recordedAt: t0.AddHours(1));
        Assert.Equal(45m, activity.ProgressPercentage);

        // Same PeriodEndDate, recorded EARLIER than the current cache's RecordedAt -> does not win.
        activity.RecordProgress(periodEnd, 10m, null, userId, ProgressSource.Manual, recordedAt: t0.AddMinutes(-30));
        Assert.Equal(45m, activity.ProgressPercentage);
    }

    [Fact]
    public void RecordProgress_ActualQuantity_Defaults_To_Null_Not_Zero()
    {
        var activity = CreateActivity();

        var entry = activity.RecordProgress(
            DateTimeOffset.UtcNow, 10m, actualQuantity: null, recordedByUserId: Guid.NewGuid(),
            source: ProgressSource.Manual, recordedAt: DateTimeOffset.UtcNow);

        Assert.Null(entry.ActualQuantity);
    }

    [Fact]
    public void RecordProgress_Clamps_Percentage_To_0_100()
    {
        var activity = CreateActivity();

        var entry = activity.RecordProgress(
            DateTimeOffset.UtcNow, 150m, null, Guid.NewGuid(), ProgressSource.Manual, DateTimeOffset.UtcNow);

        Assert.Equal(100m, entry.ProgressPercentage);
        Assert.Equal(100m, activity.ProgressPercentage);
    }

    [Fact]
    public void Constructor_Rejects_Negative_Duration_And_Budget()
    {
        Assert.Throws<DomainException>(() => new Activity(
            Guid.NewGuid(), Guid.NewGuid(), "A", "Name",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), durationDays: -1, budgetCost: 0m));

        Assert.Throws<DomainException>(() => new Activity(
            Guid.NewGuid(), Guid.NewGuid(), "A", "Name",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), durationDays: 1, budgetCost: -1m));
    }
}
