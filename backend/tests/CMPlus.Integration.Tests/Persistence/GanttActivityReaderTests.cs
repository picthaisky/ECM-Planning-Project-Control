using CMPlus.Domain.Entities;
using CMPlus.Infrastructure.Persistence;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S6-BE-01: <see cref="GanttActivityReader"/> against a real <see cref="CmPlusDbContext"/> - proves
/// the global tenant query filter (ADR-0002) scopes <c>GET /api/v1/projects/{projectId}/gantt</c> to
/// the caller's own tenant, that a project's activities never leak another project's rows in the
/// same tenant, and that the optional date-range window filters by planned-span overlap as
/// documented on <see cref="CMPlus.Application.Abstractions.IGanttActivityReader"/>.
/// </summary>
public class GanttActivityReaderTests
{
    private static Project CreateProject(Guid tenantId, string code) => Project.Create(
        tenantId, $"Project {code}", code, "Owner",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 100_000_000m, dataDate: DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetActivitiesAsync_Only_Returns_The_Current_Tenants_Activities()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantA);

        var projectA = CreateProject(tenantA, "P-A");
        var nodeA = new WBSNode(tenantA, projectA.Id, "1", "Structure", 100m);
        var activityA = new Activity(
            tenantA, nodeA.Id, "A-001", "Excavation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 1_000_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(projectA);
            seedContext.WBSNodes.Add(nodeA);
            seedContext.Activities.Add(activityA);
            await seedContext.SaveChangesAsync();
        }

        var projectB = CreateProject(tenantB, "P-B");
        var nodeB = new WBSNode(tenantB, projectB.Id, "1", "Structure", 100m);
        var activityB = new Activity(
            tenantB, nodeB.Id, "B-001", "Piling",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 2_000_000m);

        factory.TenantProvider.TenantId = tenantB;
        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(projectB);
            seedContext.WBSNodes.Add(nodeB);
            seedContext.Activities.Add(activityB);
            await seedContext.SaveChangesAsync();
        }

        // Query as tenant A for tenant A's own project - tenant B's activity must never appear,
        // even though both projects share the same route shape.
        factory.TenantProvider.TenantId = tenantA;
        using var readContext = factory.CreateContext();
        var reader = new GanttActivityReader(readContext);

        var activities = await reader.GetActivitiesAsync(projectA.Id, from: null, to: null);

        var activity = Assert.Single(activities);
        Assert.Equal("A-001", activity.ActivityCode);
    }

    [Fact]
    public async Task GetActivitiesAsync_Excludes_Activities_Belonging_To_A_Different_Project_In_The_Same_Tenant()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);

        var projectOne = CreateProject(tenantId, "P-ONE");
        var projectTwo = CreateProject(tenantId, "P-TWO");
        var nodeUnderProjectOne = new WBSNode(tenantId, projectOne.Id, "1", "Structure", 100m);
        var nodeUnderProjectTwo = new WBSNode(tenantId, projectTwo.Id, "1", "Structure", 100m);
        var activityOne = new Activity(
            tenantId, nodeUnderProjectOne.Id, "ONE-001", "Foundation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 1_000_000m);
        var activityTwo = new Activity(
            tenantId, nodeUnderProjectTwo.Id, "TWO-001", "Foundation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 1_000_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.AddRange(projectOne, projectTwo);
            seedContext.WBSNodes.AddRange(nodeUnderProjectOne, nodeUnderProjectTwo);
            seedContext.Activities.AddRange(activityOne, activityTwo);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new GanttActivityReader(readContext);

        var activities = await reader.GetActivitiesAsync(projectOne.Id, from: null, to: null);

        var activity = Assert.Single(activities);
        Assert.Equal("ONE-001", activity.ActivityCode);
    }

    [Fact]
    public async Task GetActivitiesAsync_With_No_Date_Range_Returns_Every_Activity()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);

        var project = CreateProject(tenantId, "P");
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);
        var earlyActivity = new Activity(
            tenantId, node.Id, "A-001", "Early",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 1_000_000m);
        var lateActivity = new Activity(
            tenantId, node.Id, "A-002", "Late",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"), DateTimeOffset.Parse("2026-06-15T00:00:00Z"), 14, 1_000_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.AddRange(earlyActivity, lateActivity);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new GanttActivityReader(readContext);

        var activities = await reader.GetActivitiesAsync(project.Id, from: null, to: null);

        Assert.Equal(2, activities.Count);
    }

    [Fact]
    public async Task GetActivitiesAsync_Windows_By_Planned_Span_Overlap_With_The_Requested_Range()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);

        var project = CreateProject(tenantId, "P");
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);

        // Entirely before the window.
        var beforeActivity = new Activity(
            tenantId, node.Id, "A-BEFORE", "Before",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-10T00:00:00Z"), 9, 1_000_000m);
        // Overlaps the start of the window.
        var overlappingStartActivity = new Activity(
            tenantId, node.Id, "A-OVERLAP-START", "OverlapStart",
            DateTimeOffset.Parse("2026-01-25T00:00:00Z"), DateTimeOffset.Parse("2026-02-05T00:00:00Z"), 11, 1_000_000m);
        // Fully inside the window.
        var insideActivity = new Activity(
            tenantId, node.Id, "A-INSIDE", "Inside",
            DateTimeOffset.Parse("2026-02-10T00:00:00Z"), DateTimeOffset.Parse("2026-02-15T00:00:00Z"), 5, 1_000_000m);
        // Entirely after the window.
        var afterActivity = new Activity(
            tenantId, node.Id, "A-AFTER", "After",
            DateTimeOffset.Parse("2026-03-01T00:00:00Z"), DateTimeOffset.Parse("2026-03-10T00:00:00Z"), 9, 1_000_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.AddRange(beforeActivity, overlappingStartActivity, insideActivity, afterActivity);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new GanttActivityReader(readContext);

        var from = DateTimeOffset.Parse("2026-02-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-02-28T00:00:00Z");
        var activities = await reader.GetActivitiesAsync(project.Id, from, to);

        Assert.Equal(
            new[] { "A-INSIDE", "A-OVERLAP-START" },
            activities.Select(a => a.ActivityCode).OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetProjectDataDateAsync_Returns_The_Real_Projects_DataDate()
    {
        // Gap closure (post-S6-FE-01): the Gantt's vertical data-date line had no source at all
        // until this method existed - proves it reads the real Project.DataDate, tenant-scoped like
        // every other read here, not a hardcoded/placeholder value.
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var expectedDataDate = DateTimeOffset.Parse("2026-03-15T00:00:00+07:00");

        var project = Project.Create(
            tenantId, "Project P", "P", "Owner",
            DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"), DateTimeOffset.Parse("2027-01-01T00:00:00+07:00"),
            bac: 100_000_000m, dataDate: expectedDataDate);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new GanttActivityReader(readContext);

        var dataDate = await reader.GetProjectDataDateAsync(project.Id);

        Assert.Equal(expectedDataDate, dataDate);
    }
}
