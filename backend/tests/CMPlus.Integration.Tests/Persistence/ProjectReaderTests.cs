using CMPlus.Domain.Entities;
using CMPlus.Infrastructure.Persistence;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S4-BE-04 (gap closure): <see cref="ProjectReader"/> against a real <see cref="CmPlusDbContext"/>
/// - proves the global tenant query filter (ADR-0002), not merely a fake, is what scopes
/// <c>GET /api/v1/projects</c> to the caller's own tenant.
/// </summary>
public class ProjectReaderTests
{
    [Fact]
    public async Task GetAllAsync_Only_Returns_The_Current_Tenants_Own_Projects()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantA);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(Project.Create(
                tenantA, "Riverside Condominium Tower B", "RCT-B", "Owner A",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(18), bac: 850_000_000m, dataDate: DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        factory.TenantProvider.TenantId = tenantB;
        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(Project.Create(
                tenantB, "Sukhumvit Expressway Extension", "SEE-P3", "Owner B",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(36), bac: 2_400_000_000m, dataDate: DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        // Query as tenant A: only tenant A's project is visible, automatically - never tenant B's,
        // and never both.
        factory.TenantProvider.TenantId = tenantA;
        using (var readContext = factory.CreateContext())
        {
            var reader = new ProjectReader(readContext);
            var projects = await reader.GetAllAsync();

            var project = Assert.Single(projects);
            Assert.Equal("RCT-B", project.Code);
        }

        // Query as tenant B: only tenant B's project is visible.
        factory.TenantProvider.TenantId = tenantB;
        using (var readContext = factory.CreateContext())
        {
            var reader = new ProjectReader(readContext);
            var projects = await reader.GetAllAsync();

            var project = Assert.Single(projects);
            Assert.Equal("SEE-P3", project.Code);
        }
    }

    [Fact]
    public async Task GetAllAsync_Returns_An_Empty_List_For_A_Tenant_With_No_Projects()
    {
        var factory = new TestDbContextFactory(Guid.NewGuid());

        using var readContext = factory.CreateContext();
        var reader = new ProjectReader(readContext);

        var projects = await reader.GetAllAsync();

        Assert.Empty(projects);
    }
}
