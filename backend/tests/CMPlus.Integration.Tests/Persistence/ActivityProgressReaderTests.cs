using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S1-BE-04 DoD: <see cref="ActivityProgressReader"/> (the Infrastructure implementation of
/// <c>IActivityProgressReader</c>) must return the ADR-0009 step-function value in every case,
/// including the RecordedAt tie-break, and 0 when nothing has been recorded yet.
/// </summary>
public class ActivityProgressReaderTests
{
    private static async Task<(Activity Activity, Guid TenantId)> SeedActivityAsync(TestDbContextFactory factory)
    {
        var tenantId = factory.TenantProvider.TenantId;
        var activity = new Activity(
            tenantId, Guid.NewGuid(), "A-1", "Formwork",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), 30, 50_000m);

        using var context = factory.CreateContext();
        context.Activities.Add(activity);
        await context.SaveChangesAsync();

        return (activity, tenantId);
    }

    [Fact]
    public async Task Returns_Zero_When_No_Entry_Exists_Yet()
    {
        var factory = new TestDbContextFactory(Guid.NewGuid());
        var (activity, _) = await SeedActivityAsync(factory);

        using var context = factory.CreateContext();
        var reader = new ActivityProgressReader(context);

        var result = await reader.GetProgressAsOfAsync(activity.Id, DateTimeOffset.UtcNow);

        Assert.Equal(0m, result);
    }

    [Fact]
    public async Task Returns_The_Entry_With_The_Greatest_PeriodEndDate_Less_Than_Or_Equal_To_AsOf()
    {
        var factory = new TestDbContextFactory(Guid.NewGuid());
        var (activity, tenantId) = await SeedActivityAsync(factory);
        var userId = Guid.NewGuid();

        using (var writeContext = factory.CreateContext())
        {
            var tracked = await writeContext.Activities.SingleAsync(a => a.Id == activity.Id);

            var e1 = tracked.RecordProgress(
                DateTimeOffset.Parse("2026-06-07T00:00:00+07:00"), 20m, null, userId,
                ProgressSource.Manual, DateTimeOffset.Parse("2026-06-08T00:00:00+07:00"));
            var e2 = tracked.RecordProgress(
                DateTimeOffset.Parse("2026-06-14T00:00:00+07:00"), 35m, null, userId,
                ProgressSource.Manual, DateTimeOffset.Parse("2026-06-15T00:00:00+07:00"));
            var e3 = tracked.RecordProgress(
                DateTimeOffset.Parse("2026-06-21T00:00:00+07:00"), 50m, null, userId,
                ProgressSource.Manual, DateTimeOffset.Parse("2026-06-22T00:00:00+07:00"));

            writeContext.ActivityProgressLogs.AddRange(e1, e2, e3);
            await writeContext.SaveChangesAsync();
        }

        using var context = factory.CreateContext();
        var reader = new ActivityProgressReader(context);

        // asOf between the 2nd and 3rd entry -> the 2nd entry (greatest PeriodEndDate <= asOf).
        var asOfMidway = DateTimeOffset.Parse("2026-06-18T00:00:00+07:00");
        Assert.Equal(35m, await reader.GetProgressAsOfAsync(activity.Id, asOfMidway));

        // asOf before any entry -> 0.
        var asOfBeforeAny = DateTimeOffset.Parse("2026-01-01T00:00:00+07:00");
        Assert.Equal(0m, await reader.GetProgressAsOfAsync(activity.Id, asOfBeforeAny));

        // asOf after all entries -> the latest one.
        var asOfLater = DateTimeOffset.Parse("2026-12-31T00:00:00+07:00");
        Assert.Equal(50m, await reader.GetProgressAsOfAsync(activity.Id, asOfLater));

        // asOf exactly on an entry's PeriodEndDate is inclusive (<=).
        Assert.Equal(20m, await reader.GetProgressAsOfAsync(
            activity.Id, DateTimeOffset.Parse("2026-06-07T00:00:00+07:00")));
    }

    [Fact]
    public async Task Ties_On_PeriodEndDate_Are_Broken_By_The_Greatest_RecordedAt()
    {
        var factory = new TestDbContextFactory(Guid.NewGuid());
        var (activity, _) = await SeedActivityAsync(factory);
        var userId = Guid.NewGuid();
        var sharedPeriodEnd = DateTimeOffset.Parse("2026-06-14T00:00:00+07:00");

        using (var writeContext = factory.CreateContext())
        {
            var tracked = await writeContext.Activities.SingleAsync(a => a.Id == activity.Id);

            var earlyRecorded = tracked.RecordProgress(
                sharedPeriodEnd, 30m, null, userId, ProgressSource.Manual,
                DateTimeOffset.Parse("2026-06-15T08:00:00+07:00"));
            var laterRecorded = tracked.RecordProgress(
                sharedPeriodEnd, 33m, null, userId, ProgressSource.Manual,
                DateTimeOffset.Parse("2026-06-15T17:00:00+07:00"));

            writeContext.ActivityProgressLogs.AddRange(earlyRecorded, laterRecorded);
            await writeContext.SaveChangesAsync();
        }

        using var context = factory.CreateContext();
        var reader = new ActivityProgressReader(context);

        var result = await reader.GetProgressAsOfAsync(activity.Id, sharedPeriodEnd);

        Assert.Equal(33m, result);
    }

    [Fact]
    public async Task Is_Scoped_To_The_Requested_Activity_Only()
    {
        var factory = new TestDbContextFactory(Guid.NewGuid());
        var (activityOne, tenantId) = await SeedActivityAsync(factory);
        var activityTwo = new Activity(
            tenantId, Guid.NewGuid(), "A-2", "Rebar",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), 30, 60_000m);

        using (var writeContext = factory.CreateContext())
        {
            writeContext.Activities.Add(activityTwo);

            var tracked = await writeContext.Activities.SingleAsync(a => a.Id == activityOne.Id);
            var entry = tracked.RecordProgress(
                DateTimeOffset.UtcNow, 77m, null, Guid.NewGuid(), ProgressSource.Manual, DateTimeOffset.UtcNow);
            writeContext.ActivityProgressLogs.Add(entry);

            await writeContext.SaveChangesAsync();
        }

        using var context = factory.CreateContext();
        var reader = new ActivityProgressReader(context);

        Assert.Equal(77m, await reader.GetProgressAsOfAsync(activityOne.Id, DateTimeOffset.UtcNow.AddDays(1)));
        Assert.Equal(0m, await reader.GetProgressAsOfAsync(activityTwo.Id, DateTimeOffset.UtcNow.AddDays(1)));
    }
}
