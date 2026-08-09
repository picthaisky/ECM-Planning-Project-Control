using CMPlus.Domain.Entities;
using CMPlus.Infrastructure.Persistence;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S8-BE-02: <see cref="WbsProgressReader"/> against a real <see cref="CmPlusDbContext"/> - proves
/// the tenant/project join shape (same "WBSNodes.Any(...)" pattern as <see cref="EvmDataReader"/>)
/// and that another tenant's/project's activities never leak into the rollup.
/// </summary>
public class WbsProgressReaderTests
{
    [Fact]
    public async Task GetActivityProgressByNodeAsync_Returns_Every_Activitys_Budget_And_Current_Progress_Grouped_By_Node()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);

        var project = Project.Create(
            tenantId, "Project One", "P-ONE", "Owner",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 1_000_000m, dataDate: DateTimeOffset.UtcNow);
        var nodeA = new WBSNode(tenantId, project.Id, "1", "Structure", 60m);
        var nodeB = new WBSNode(tenantId, project.Id, "2", "Architectural", 40m);
        var activityA1 = new Activity(tenantId, nodeA.Id, "A-001", "Excavation", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 100_000m);
        var activityA2 = new Activity(tenantId, nodeA.Id, "A-002", "Piling", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 300_000m);
        var activityB1 = new Activity(tenantId, nodeB.Id, "B-001", "Finishes", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 200_000m);
        activityA1.RecordProgress(DateTimeOffset.UtcNow, 50m, null, Guid.NewGuid(), CMPlus.Domain.Enums.ProgressSource.Manual, DateTimeOffset.UtcNow);
        activityA2.RecordProgress(DateTimeOffset.UtcNow, 90m, null, Guid.NewGuid(), CMPlus.Domain.Enums.ProgressSource.Manual, DateTimeOffset.UtcNow);
        activityB1.RecordProgress(DateTimeOffset.UtcNow, 20m, null, Guid.NewGuid(), CMPlus.Domain.Enums.ProgressSource.Manual, DateTimeOffset.UtcNow);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.AddRange(nodeA, nodeB);
            seedContext.Activities.AddRange(activityA1, activityA2, activityB1);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new WbsProgressReader(readContext);

        var rows = await reader.GetActivityProgressByNodeAsync(project.Id);

        Assert.Equal(3, rows.Count);
        Assert.Equal(2, rows.Count(r => r.WbsNodeId == nodeA.Id));
        var nodeBRow = Assert.Single(rows, r => r.WbsNodeId == nodeB.Id);
        Assert.Equal(200_000m, nodeBRow.BudgetCost);
        Assert.Equal(20m, nodeBRow.ProgressPercentage);
    }

    [Fact]
    public async Task GetActivityProgressByNodeAsync_Never_Returns_Another_Tenants_Or_Projects_Activities()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantA);

        var projectA = Project.Create(
            tenantA, "Project A", "P-A", "Owner A",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 100_000_000m, dataDate: DateTimeOffset.UtcNow);
        var projectA2 = Project.Create(
            tenantA, "Project A2", "P-A2", "Owner A",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 100_000_000m, dataDate: DateTimeOffset.UtcNow);
        var nodeA = new WBSNode(tenantA, projectA.Id, "1", "Structure", 100m);
        var nodeA2 = new WBSNode(tenantA, projectA2.Id, "1", "Structure", 100m);
        var activityA = new Activity(tenantA, nodeA.Id, "A-001", "Excavation", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 1_000_000m);
        var activityOtherProject = new Activity(tenantA, nodeA2.Id, "X-001", "Other project's work", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 9_999_999m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.AddRange(projectA, projectA2);
            seedContext.WBSNodes.AddRange(nodeA, nodeA2);
            seedContext.Activities.AddRange(activityA, activityOtherProject);
            await seedContext.SaveChangesAsync();
        }

        var projectB = Project.Create(
            tenantB, "Project B", "P-B", "Owner B",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 200_000_000m, dataDate: DateTimeOffset.UtcNow);
        var nodeB = new WBSNode(tenantB, projectB.Id, "1", "Structure", 100m);
        var activityB = new Activity(tenantB, nodeB.Id, "B-001", "Piling", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 2_000_000m);

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
        var reader = new WbsProgressReader(readContext);

        var rows = await reader.GetActivityProgressByNodeAsync(projectA.Id);

        var row = Assert.Single(rows);
        Assert.Equal(nodeA.Id, row.WbsNodeId);
        Assert.Equal(1_000_000m, row.BudgetCost);
    }

    [Fact]
    public async Task GetActivityProgressByNodeAsync_Returns_An_Empty_List_For_A_Project_With_No_Activities()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = Project.Create(
            tenantId, "Empty Project", "P-EMPTY", "Owner",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 1_000_000m, dataDate: DateTimeOffset.UtcNow);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new WbsProgressReader(readContext);

        var rows = await reader.GetActivityProgressByNodeAsync(project.Id);

        Assert.Empty(rows);
    }
}
