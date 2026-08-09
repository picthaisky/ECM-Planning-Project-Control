using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S7-BE-01/03: <see cref="EvmDataReader"/> against a real <see cref="CmPlusDbContext"/> - proves the
/// project-wide reader reuses the identical ADR-0009 step-function rule
/// <see cref="ActivityProgressReaderTests"/> already proves for the single-activity reader (same
/// "latest PeriodEndDate &lt;= asOf, ties broken by RecordedAt, 0 if none" behaviour, just batched
/// project-wide), scoped to the requested project (never another project's activities in the same
/// tenant, mirroring <see cref="GanttActivityReaderTests"/>'s own isolation proof).
/// </summary>
public class EvmDataReaderTests
{
    private static Project CreateProject(Guid tenantId, string code, EacVariant eacVariant = EacVariant.CpiBased) =>
        Project.Create(
            tenantId, $"Project {code}", code, "Owner",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 1_000_000m, dataDate: DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetProjectSettingsAsync_Returns_Null_When_The_Project_Does_Not_Exist()
    {
        var factory = new TestDbContextFactory(Guid.NewGuid());
        using var context = factory.CreateContext();
        var reader = new EvmDataReader(context);

        var settings = await reader.GetProjectSettingsAsync(Guid.NewGuid());

        Assert.Null(settings);
    }

    [Fact]
    public async Task GetProjectSettingsAsync_Returns_The_Projects_Bac_DataDate_And_Eac_Fields()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var expectedDataDate = DateTimeOffset.Parse("2026-06-30T00:00:00+07:00");

        var project = Project.Create(
            tenantId, "Project P", "P", "Owner",
            DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"), DateTimeOffset.Parse("2027-01-01T00:00:00+07:00"),
            bac: 2_500_000m, dataDate: expectedDataDate);
        project.SetEacVariantDefault(EacVariant.CpiSpiBased);
        project.SetEacCustomPerformanceFactor(1.20m);
        project.SetEacManualEtc(900_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        var settings = await reader.GetProjectSettingsAsync(project.Id);

        Assert.NotNull(settings);
        Assert.Equal(2_500_000m, settings!.Bac);
        Assert.Equal(expectedDataDate, settings.ProjectDataDate);
        Assert.Equal(EacVariant.CpiSpiBased, settings.EacVariantDefault);
        Assert.Equal(1.20m, settings.EacCustomPerformanceFactor);
        Assert.Equal(900_000m, settings.EacManualEtc);
    }

    [Fact]
    public async Task GetActivityInputsAsync_Returns_The_StepFunction_Progress_For_Every_Activity_Including_One_With_No_Entry()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = CreateProject(tenantId, "P");
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);

        var activityWithProgress = new Activity(
            tenantId, node.Id, "A-1", "Excavation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 1_000_000m);
        var activityWithoutProgress = new Activity(
            tenantId, node.Id, "A-2", "Piling",
            DateTimeOffset.Parse("2026-01-16T00:00:00Z"), DateTimeOffset.Parse("2026-01-30T00:00:00Z"), 14, 500_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.AddRange(activityWithProgress, activityWithoutProgress);
            await seedContext.SaveChangesAsync();
        }

        using (var writeContext = factory.CreateContext())
        {
            var tracked = await writeContext.Activities.SingleAsync(a => a.Id == activityWithProgress.Id);
            var entry = tracked.RecordProgress(
                DateTimeOffset.Parse("2026-01-10T00:00:00Z"), 40m, null, Guid.NewGuid(),
                ProgressSource.Manual, DateTimeOffset.Parse("2026-01-11T00:00:00Z"));
            writeContext.ActivityProgressLogs.Add(entry);
            await writeContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        var inputs = await reader.GetActivityInputsAsync(project.Id, DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

        Assert.Equal(2, inputs.Count);
        var withProgress = inputs.Single(i => i.ActivityId == activityWithProgress.Id);
        Assert.Equal(1_000_000m, withProgress.BudgetCost);
        Assert.Equal(40m, withProgress.ProgressPercentageAsOf);

        var withoutProgress = inputs.Single(i => i.ActivityId == activityWithoutProgress.Id);
        Assert.Equal(500_000m, withoutProgress.BudgetCost);
        Assert.Equal(0m, withoutProgress.ProgressPercentageAsOf); // no entry yet -> 0, not null/error.
    }

    [Fact]
    public async Task GetActivityInputsAsync_Reads_The_Latest_Entry_As_Of_The_Requested_Date_Not_The_Cache()
    {
        // ADR-0009: a historical read at t must reflect the step function at t, which can differ from
        // Activity.ProgressPercentage's own "latest overall" cache once a later period is recorded.
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = CreateProject(tenantId, "P");
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);
        var activity = new Activity(
            tenantId, node.Id, "A-1", "Excavation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-02-01T00:00:00Z"), 31, 1_000_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.Add(activity);
            await seedContext.SaveChangesAsync();
        }

        using (var writeContext = factory.CreateContext())
        {
            var tracked = await writeContext.Activities.SingleAsync(a => a.Id == activity.Id);
            var week1 = tracked.RecordProgress(
                DateTimeOffset.Parse("2026-01-07T00:00:00Z"), 20m, null, Guid.NewGuid(),
                ProgressSource.Manual, DateTimeOffset.Parse("2026-01-08T00:00:00Z"));
            var week2 = tracked.RecordProgress(
                DateTimeOffset.Parse("2026-01-14T00:00:00Z"), 45m, null, Guid.NewGuid(),
                ProgressSource.Manual, DateTimeOffset.Parse("2026-01-15T00:00:00Z"));
            writeContext.ActivityProgressLogs.AddRange(week1, week2);
            await writeContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        // Historical read as of a date between the two entries -> the earlier (week 1) value, even
        // though Activity.ProgressPercentage's cache has already moved to 45.
        var historicalInputs = await reader.GetActivityInputsAsync(project.Id, DateTimeOffset.Parse("2026-01-10T00:00:00Z"));
        Assert.Equal(20m, Assert.Single(historicalInputs).ProgressPercentageAsOf);

        var currentInputs = await reader.GetActivityInputsAsync(project.Id, DateTimeOffset.Parse("2026-01-20T00:00:00Z"));
        Assert.Equal(45m, Assert.Single(currentInputs).ProgressPercentageAsOf);
    }

    [Fact]
    public async Task GetActivityInputsAsync_Excludes_Activities_Belonging_To_A_Different_Project_In_The_Same_Tenant()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var projectOne = CreateProject(tenantId, "P-ONE");
        var projectTwo = CreateProject(tenantId, "P-TWO");
        var nodeOne = new WBSNode(tenantId, projectOne.Id, "1", "Structure", 100m);
        var nodeTwo = new WBSNode(tenantId, projectTwo.Id, "1", "Structure", 100m);
        var activityOne = new Activity(
            tenantId, nodeOne.Id, "ONE-001", "Foundation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 1_000_000m);
        var activityTwo = new Activity(
            tenantId, nodeTwo.Id, "TWO-001", "Foundation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 1_000_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.AddRange(projectOne, projectTwo);
            seedContext.WBSNodes.AddRange(nodeOne, nodeTwo);
            seedContext.Activities.AddRange(activityOne, activityTwo);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        var inputs = await reader.GetActivityInputsAsync(projectOne.Id, DateTimeOffset.UtcNow);

        var input = Assert.Single(inputs);
        Assert.Equal(activityOne.Id, input.ActivityId);
    }
}
