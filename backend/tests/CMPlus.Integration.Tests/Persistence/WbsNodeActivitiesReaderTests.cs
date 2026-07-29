using CMPlus.Domain.Entities;
using CMPlus.Infrastructure.Persistence;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S4-BE-05 (gap closure): <see cref="WbsNodeActivitiesReader"/> against a real
/// <see cref="CmPlusDbContext"/> - proves the global tenant query filter (ADR-0002) scopes
/// <c>GET /api/v1/projects/{projectId}/wbs-nodes/{wbsNodeId}/activities</c> to the caller's own
/// tenant, and that a node from a different tenant is treated as not found rather than leaking its
/// activities.
/// </summary>
public class WbsNodeActivitiesReaderTests
{
    [Fact]
    public async Task NodeExistsInProjectAsync_Is_False_For_Another_Tenants_Node()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantA);

        var projectB = Project.Create(
            tenantB, "Sukhumvit Expressway Extension", "SEE-P3", "Owner B",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(36), bac: 2_400_000_000m, dataDate: DateTimeOffset.UtcNow);
        var nodeB = new WBSNode(tenantB, projectB.Id, "1", "Structure", 100m);

        factory.TenantProvider.TenantId = tenantB;
        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(projectB);
            seedContext.WBSNodes.Add(nodeB);
            await seedContext.SaveChangesAsync();
        }

        // Query as tenant A for tenant B's real node/project ids: the global query filter must
        // hide them completely - indistinguishable from a node that never existed at all.
        factory.TenantProvider.TenantId = tenantA;
        using var readContext = factory.CreateContext();
        var reader = new WbsNodeActivitiesReader(readContext);

        var exists = await reader.NodeExistsInProjectAsync(projectB.Id, nodeB.Id);

        Assert.False(exists);
    }

    [Fact]
    public async Task NodeExistsInProjectAsync_Is_False_When_The_Node_Belongs_To_A_Different_Project_In_The_Same_Tenant()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);

        var projectOne = Project.Create(
            tenantId, "Project One", "P-ONE", "Owner",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 100_000_000m, dataDate: DateTimeOffset.UtcNow);
        var projectTwo = Project.Create(
            tenantId, "Project Two", "P-TWO", "Owner",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 100_000_000m, dataDate: DateTimeOffset.UtcNow);
        var nodeUnderProjectOne = new WBSNode(tenantId, projectOne.Id, "1", "Structure", 100m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.AddRange(projectOne, projectTwo);
            seedContext.WBSNodes.Add(nodeUnderProjectOne);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new WbsNodeActivitiesReader(readContext);

        // Same tenant, real node id - but requested against the *other* project's id.
        var exists = await reader.NodeExistsInProjectAsync(projectTwo.Id, nodeUnderProjectOne.Id);

        Assert.False(exists);
    }

    [Fact]
    public async Task GetActivitiesForNodeAsync_Only_Returns_The_Current_Tenants_Activities_For_That_Node()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantA);

        var projectA = Project.Create(
            tenantA, "Project A", "P-A", "Owner A",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 100_000_000m, dataDate: DateTimeOffset.UtcNow);
        var nodeA = new WBSNode(tenantA, projectA.Id, "1", "Structure", 100m);
        var activityA = new Activity(
            tenantA, nodeA.Id, "A-001", "Excavation",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 1_000_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(projectA);
            seedContext.WBSNodes.Add(nodeA);
            seedContext.Activities.Add(activityA);
            await seedContext.SaveChangesAsync();
        }

        var projectB = Project.Create(
            tenantB, "Project B", "P-B", "Owner B",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 200_000_000m, dataDate: DateTimeOffset.UtcNow);
        var nodeB = new WBSNode(tenantB, projectB.Id, "1", "Structure", 100m);
        var activityB = new Activity(
            tenantB, nodeB.Id, "B-001", "Piling",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 2_000_000m);

        factory.TenantProvider.TenantId = tenantB;
        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(projectB);
            seedContext.WBSNodes.Add(nodeB);
            seedContext.Activities.Add(activityB);
            await seedContext.SaveChangesAsync();
        }

        factory.TenantProvider.TenantId = tenantA;
        using var readContext = factory.CreateContext();
        var reader = new WbsNodeActivitiesReader(readContext);

        var exists = await reader.NodeExistsInProjectAsync(projectA.Id, nodeA.Id);
        Assert.True(exists);

        var activities = await reader.GetActivitiesForNodeAsync(nodeA.Id);

        var activity = Assert.Single(activities);
        Assert.Equal("A-001", activity.ActivityCode);
        Assert.Equal(0.00m, activity.CurrentProgressPercentage);
    }
}
