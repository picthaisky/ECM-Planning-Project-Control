using CMPlus.Application.Services.Manpower;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Manpower;

/// <summary>
/// S12-BE-02: <see cref="ProductivityIndexReader"/> against a real (InMemory) <see cref="CmPlusDbContext"/> -
/// proves the DB-shaped work (WBS subtree resolution, ADR-0009 progress reads, §4.5 half-open
/// bucketing, §5.5's reporting-cadence gate, ADR-0002 tenant scoping) actually works end to end, not
/// only the pure <see cref="ProductivityIndexCalculator"/> arithmetic already covered in
/// <c>CMPlus.Application.Tests.Manpower</c>. Fixtures per domain-rules.md (manpower-equipment) §10.0's
/// shared setup: project P-MEQ, N1/A-STR carrying BudgetManHours 12,000.00.
/// </summary>
public class ProductivityIndexReaderIntegrationTests
{
    private static async Task<(Guid ProjectId, Guid WbsNodeId, Guid ActivityId, Guid WorkCategoryId, string DatabaseName, Guid TenantId)>
        SeedBaseFixtureAsync()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var tenantProvider = new FakeTenantProvider(tenantId);

        Guid projectId, wbsNodeId, activityId, workCategoryId;
        await using (var context = new CmPlusDbContext(
            new DbContextOptionsBuilder<CmPlusDbContext>().UseInMemoryDatabase(databaseName).Options, tenantProvider))
        {
            var project = Project.Create(
                tenantId, "P-MEQ", "P-MEQ", "Owner", DateTimeOffset.Parse("2026-07-01T00:00:00+07:00"),
                DateTimeOffset.Parse("2027-01-01T00:00:00+07:00"), bac: 3_600_000.00m,
                dataDate: DateTimeOffset.Parse("2026-07-10T00:00:00+07:00"));
            context.Projects.Add(project);
            projectId = project.Id;

            var node = new WBSNode(tenantId, projectId, "01", "งานโครงสร้าง", 60m);
            context.WBSNodes.Add(node);
            wbsNodeId = node.Id;

            var activity = new Activity(
                tenantId, wbsNodeId, "A-STR", "งานโครงสร้าง",
                DateTimeOffset.Parse("2026-07-01T00:00:00+07:00"), DateTimeOffset.Parse("2026-12-01T00:00:00+07:00"),
                durationDays: 150, budgetCost: 3_600_000.00m);
            activity.SetBudgetManHours(12_000.00m);
            context.Activities.Add(activity);
            activityId = activity.Id;

            var category = new WorkCategory(tenantId, null, "C-STR", "งานโครงสร้าง", "Structure", 1);
            context.WorkCategories.Add(category);
            workCategoryId = category.Id;

            await context.SaveChangesAsync();
        }

        return (projectId, wbsNodeId, activityId, workCategoryId, databaseName, tenantId);
    }

    private static async Task RecordProgressAsync(
        string databaseName, FakeTenantProvider tenantProvider, Guid activityId, DateTimeOffset periodEndDate, decimal progressPercentage)
    {
        await using var context = new CmPlusDbContext(
            new DbContextOptionsBuilder<CmPlusDbContext>().UseInMemoryDatabase(databaseName).Options, tenantProvider);
        var activity = await context.Activities.SingleAsync(a => a.Id == activityId);
        var entry = activity.RecordProgress(periodEndDate, progressPercentage, null, Guid.NewGuid(), ProgressSource.Manual, periodEndDate);
        context.ActivityProgressLogs.Add(entry);
        await context.SaveChangesAsync();
    }

    private static async Task RecordManpowerLogAsync(
        string databaseName, FakeTenantProvider tenantProvider, Guid projectId, Guid wbsNodeId, Guid workCategoryId,
        DateTimeOffset logDate, int workerCount, decimal manHours)
    {
        await using var context = new CmPlusDbContext(
            new DbContextOptionsBuilder<CmPlusDbContext>().UseInMemoryDatabase(databaseName).Options, tenantProvider);
        var log = ManpowerEquipmentLog.CreateOriginal(
            tenantProvider.TenantId, projectId, logDate, Shift.Day, workCategoryId, wbsNodeId, null,
            LabourType.OwnDirect, null, workerCount, manHours, 0m, false, 0, 0m, 0m, null, null,
            Guid.NewGuid(), logDate, allowDuplicateOverride: false);
        context.ManpowerEquipmentLogs.Add(log);
        await context.SaveChangesAsync();
    }

    private static CmPlusDbContext CreateReadContext(string databaseName, FakeTenantProvider tenantProvider) =>
        new(new DbContextOptionsBuilder<CmPlusDbContext>().UseInMemoryDatabase(databaseName).Options, tenantProvider);

    // ---- M-01, resolved end to end through the real reader ----

    [Fact]
    public async Task M01_Base_Case_Resolves_090_Through_The_Real_Reader()
    {
        var (projectId, wbsNodeId, activityId, workCategoryId, databaseName, tenantId) = await SeedBaseFixtureAsync();
        var tenantProvider = new FakeTenantProvider(tenantId);

        await RecordProgressAsync(databaseName, tenantProvider, activityId, DateTimeOffset.Parse("2026-07-06T00:00:00+07:00"), 30.00m);
        await RecordProgressAsync(databaseName, tenantProvider, activityId, DateTimeOffset.Parse("2026-07-07T00:00:00+07:00"), 31.50m);
        await RecordManpowerLogAsync(
            databaseName, tenantProvider, projectId, wbsNodeId, workCategoryId,
            DateTimeOffset.Parse("2026-07-07T00:00:00+07:00"), workerCount: 25, manHours: 200.00m);

        var reader = new ProductivityIndexReader(CreateReadContext(databaseName, tenantProvider));
        var aggregate = await reader.GetAggregateAsync(
            projectId, wbsNodeId, null,
            DateTimeOffset.Parse("2026-07-06T00:00:00+07:00"), DateTimeOffset.Parse("2026-07-07T00:00:00+07:00"));

        Assert.Equal(180.00m, aggregate.EarnedManHours);
        Assert.Equal(200.00m, aggregate.ActualManHoursInScope);
        Assert.Equal(200.00m, aggregate.ActualManHoursTotal);
        Assert.Equal(1, aggregate.LogEntryCount);
        Assert.True(aggregate.AnyActivityInScope);
        Assert.True(aggregate.AnyBudgetedActivityInScope);
        Assert.True(aggregate.HasProgressObservationInPeriod);

        var result = ProductivityIndexCalculator.Compute(
            aggregate.EarnedManHours, aggregate.ActualManHoursInScope, aggregate.ActualManHoursTotal,
            aggregate.LogEntryCount, aggregate.AnyActivityInScope, aggregate.AnyBudgetedActivityInScope,
            aggregate.HasProgressObservationInPeriod);
        Assert.Equal(0.90m, result.Value);
    }

    // ---- M-09: half-open bucket boundary, through the real reader ----

    [Fact]
    public async Task M09_Half_Open_Bucket_Excludes_The_Lower_Bound_Date_Through_The_Real_Reader()
    {
        var (projectId, wbsNodeId, activityId, workCategoryId, databaseName, tenantId) = await SeedBaseFixtureAsync();
        var tenantProvider = new FakeTenantProvider(tenantId);

        await RecordProgressAsync(databaseName, tenantProvider, activityId, DateTimeOffset.Parse("2026-05-31T00:00:00+07:00"), 10.00m);
        await RecordProgressAsync(databaseName, tenantProvider, activityId, DateTimeOffset.Parse("2026-06-30T00:00:00+07:00"), 25.00m);
        await RecordProgressAsync(databaseName, tenantProvider, activityId, DateTimeOffset.Parse("2026-07-31T00:00:00+07:00"), 40.00m);

        // This 300.00h row sits exactly ON the lower (exclusive) bound - it must NOT be pulled into
        // the July bucket (the naive-inclusive-both-ends defect M-09 exists to catch).
        await RecordManpowerLogAsync(
            databaseName, tenantProvider, projectId, wbsNodeId, workCategoryId,
            DateTimeOffset.Parse("2026-06-30T00:00:00+07:00"), workerCount: 40, manHours: 300.00m);
        await RecordManpowerLogAsync(
            databaseName, tenantProvider, projectId, wbsNodeId, workCategoryId,
            DateTimeOffset.Parse("2026-07-31T00:00:00+07:00"), workerCount: 250, manHours: 2_000.00m);

        var reader = new ProductivityIndexReader(CreateReadContext(databaseName, tenantProvider));
        var aggregate = await reader.GetAggregateAsync(
            projectId, wbsNodeId, null,
            DateTimeOffset.Parse("2026-06-30T00:00:00+07:00"), DateTimeOffset.Parse("2026-07-31T00:00:00+07:00"));

        // Correct: EMH = 12,000 x 15.00% = 1,800.00; AMH = 2,000.00 (July only).
        Assert.Equal(1_800.00m, aggregate.EarnedManHours);
        Assert.Equal(2_000.00m, aggregate.ActualManHoursInScope);
        Assert.Equal(2_000.00m, aggregate.ActualManHoursTotal);
        Assert.Equal(1, aggregate.LogEntryCount); // NOT 2 - the June 30 row is excluded.

        var result = ProductivityIndexCalculator.Compute(
            aggregate.EarnedManHours, aggregate.ActualManHoursInScope, aggregate.ActualManHoursTotal,
            aggregate.LogEntryCount, aggregate.AnyActivityInScope, aggregate.AnyBudgetedActivityInScope,
            aggregate.HasProgressObservationInPeriod);
        Assert.Equal(0.90m, result.Value);
        // Negative assertion: an inclusive [a,b] implementation would give 1,800/2,300 = 0.78.
        Assert.NotEqual(0.78m, result.Value);
    }

    // ---- M-08 ★: the reporting-cadence trap, through the real reader ----

    [Fact]
    public async Task M08_A_Daily_Bucket_With_No_Progress_Observation_Is_Null_NoProgressInPeriod_Through_The_Real_Reader()
    {
        var (projectId, wbsNodeId, activityId, workCategoryId, databaseName, tenantId) = await SeedBaseFixtureAsync();
        var tenantProvider = new FakeTenantProvider(tenantId);

        // Only one progress observation, at week's end.
        await RecordProgressAsync(databaseName, tenantProvider, activityId, DateTimeOffset.Parse("2026-07-05T00:00:00+07:00"), 30.00m);
        await RecordProgressAsync(databaseName, tenantProvider, activityId, DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"), 38.00m);

        await RecordManpowerLogAsync(
            databaseName, tenantProvider, projectId, wbsNodeId, workCategoryId,
            DateTimeOffset.Parse("2026-07-06T00:00:00+07:00"), workerCount: 25, manHours: 200.00m);

        var reader = new ProductivityIndexReader(CreateReadContext(databaseName, tenantProvider));

        // A daily bucket (Mon only) contains hours but no progress observation of its own.
        var dailyAggregate = await reader.GetAggregateAsync(
            projectId, wbsNodeId, null,
            DateTimeOffset.Parse("2026-07-05T00:00:00+07:00"), DateTimeOffset.Parse("2026-07-06T00:00:00+07:00"));

        Assert.False(dailyAggregate.HasProgressObservationInPeriod);
        var dailyResult = ProductivityIndexCalculator.Compute(
            dailyAggregate.EarnedManHours, dailyAggregate.ActualManHoursInScope, dailyAggregate.ActualManHoursTotal,
            dailyAggregate.LogEntryCount, dailyAggregate.AnyActivityInScope, dailyAggregate.AnyBudgetedActivityInScope,
            dailyAggregate.HasProgressObservationInPeriod);
        Assert.Null(dailyResult.Value);
        Assert.Equal(PiNullReason.NoProgressInPeriod, dailyResult.Reason);

        // The full week's bucket DOES contain the progress observation (at its upper bound).
        var weekAggregate = await reader.GetAggregateAsync(
            projectId, wbsNodeId, null,
            DateTimeOffset.Parse("2026-07-05T00:00:00+07:00"), DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"));
        Assert.True(weekAggregate.HasProgressObservationInPeriod);
    }

    // ---- M-14: tenant isolation for the PI read, through the real reader (ADR-0002) ----

    [Fact]
    public async Task M14_A_Different_Tenants_Reader_Sees_No_Activity_No_Wbs_Node_And_No_Log_Rows()
    {
        var (projectId, wbsNodeId, _, _, databaseName, _) = await SeedBaseFixtureAsync();
        var otherTenantProvider = new FakeTenantProvider(Guid.NewGuid());

        var reader = new ProductivityIndexReader(CreateReadContext(databaseName, otherTenantProvider));

        Assert.False(await reader.ProjectExistsAsync(projectId));
        Assert.False(await reader.WbsNodeExistsInProjectAsync(projectId, wbsNodeId));

        var aggregate = await reader.GetAggregateAsync(
            projectId, null, null, DateTimeOffset.MinValue, DateTimeOffset.Parse("2026-12-31T00:00:00+07:00"));

        // Tenant B's reader must see nothing at all for tenant A's project - not an error, just
        // empty, exactly as if the project did not exist (the global query filter, ADR-0002).
        Assert.False(aggregate.AnyActivityInScope);
        Assert.Equal(0, aggregate.LogEntryCount);
        Assert.Equal(0.00m, aggregate.ActualManHoursTotal);
    }
}
