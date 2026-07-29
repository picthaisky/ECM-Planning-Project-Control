using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S4-BE-05 (gap closure) end-to-end over real HTTP:
/// <c>GET /api/v1/projects/{projectId}/wbs-nodes/{wbsNodeId}/activities</c> - the endpoint
/// frontend-developer's batch-progress grid already calls (`web/src/features/wbs/api.ts`'s
/// <c>getNodeActivities</c>) but that did not exist on the real backend before this gap closure.
/// Seeds real WBSNode/Activity rows via the golden XER import (the only write path today that can
/// create them, matching <c>ImportControllerTests</c>'s fixture) rather than writing directly
/// through a DI-resolved <c>CmPlusDbContext</c> outside a request - <c>HttpContextTenantProvider</c>
/// throws when there is no authenticated HTTP request to read a tenantId claim from, so TenantId
/// stamping on a direct out-of-request insert would fail.
///
/// Deliberately does <b>not</b> use <c>IClassFixture&lt;CustomWebApplicationFactory&gt;</c> (unlike
/// <c>ImportControllerTests</c>/<c>ProjectsControllerTests</c>) - a fresh factory (and therefore a
/// fresh, uniquely-named InMemory database) per test method, since the XER import is not
/// idempotent (unlike <c>DevDataSeeder</c>, it does not check-before-insert): two tests both
/// importing the golden fixture into the same seeded project against a *shared* factory would each
/// create their own "CIVIL" WBS node, and a later-added <c>.Single()</c> lookup by code would then
/// find more than one - this was caught by the very tenant-isolation tests below failing
/// non-deterministically depending on test execution order before this was fixed.
/// </summary>
public class WbsNodesControllerTests
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record ActivityForProgressResponse(Guid Id, string ActivityCode, string Name, decimal CurrentProgressPercentage);

    private static async Task<CustomWebApplicationFactory> CreateSeededFactoryAsync()
    {
        var factory = new CustomWebApplicationFactory();
        await factory.SeedAsync();
        return factory;
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = DevDataSeeder.DevSeedPassword }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<Guid> GetSeededProjectIdAsync(CustomWebApplicationFactory factory, Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        var project = await dbContext.Projects.IgnoreQueryFilters().SingleAsync(p => p.TenantId == tenantId);
        return project.Id;
    }

    /// <summary>The golden XER fixture's "CIVIL" WBS node (2 activities: A1010/A1020) - see
    /// `ImportControllerTests`'s identical fixture. Read via <c>IgnoreQueryFilters</c> purely as
    /// white-box test setup (resolving what the import just created), never how the feature itself
    /// reads data.</summary>
    private static async Task<Guid> GetCivilWbsNodeIdAsync(CustomWebApplicationFactory factory, Guid projectId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        var node = await dbContext.WBSNodes.IgnoreQueryFilters()
            .SingleAsync(n => n.ProjectId == projectId && n.Code == "CIVIL");
        return node.Id;
    }

    private static async Task<Guid> ImportGoldenScheduleAsync(CustomWebApplicationFactory factory, HttpClient client, Guid projectId)
    {
        var xerBytes = await File.ReadAllBytesAsync(ResolveGoldenFixturePath());
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(xerBytes), "file", "sample-schedule.xer" },
        };

        var response = await client.PostAsync($"/api/v1/projects/{projectId}/import/xer", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await GetCivilWbsNodeIdAsync(factory, projectId);
    }

    private static string ResolveGoldenFixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CMPlus.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "tests", "fixtures", "goldenfiles", "xer", "sample-schedule.xer");
    }

    [Fact]
    public async Task An_Unauthenticated_Request_Is_Rejected_With_401()
    {
        await using var factory = await CreateSeededFactoryAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/wbs-nodes/{Guid.NewGuid()}/activities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Requesting_An_Unknown_WbsNode_Returns_404()
    {
        await using var factory = await CreateSeededFactoryAsync();
        using var client = factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(factory, pm.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/wbs-nodes/{Guid.NewGuid()}/activities");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Requesting_The_Real_Nodes_Activities_Returns_Both_Its_Activities_With_Progress()
    {
        await using var factory = await CreateSeededFactoryAsync();
        using var client = factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(factory, pm.TenantId);
        var civilNodeId = await ImportGoldenScheduleAsync(factory, client, projectId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/wbs-nodes/{civilNodeId}/activities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var activities = await response.Content.ReadFromJsonAsync<List<ActivityForProgressResponse>>();

        Assert.NotNull(activities);
        Assert.Equal(2, activities!.Count);
        Assert.Contains(activities, a => a.ActivityCode == "A1010" && a.Name == "Excavation");
        Assert.Contains(activities, a => a.ActivityCode == "A1020" && a.Name == "Foundation");
        Assert.All(activities, a => Assert.Equal(0.00m, a.CurrentProgressPercentage));
    }

    // ------------------------------------------------------------------------------------
    // Tenant isolation (ADR-0002): a WBS node id that is real, but belongs to a *different*
    // tenant's project, must be indistinguishable from a node that never existed at all.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Node_Belonging_To_Another_Tenant_Is_Not_Found_Even_With_Its_Real_Ids()
    {
        await using var factory = await CreateSeededFactoryAsync();

        using var clientA = factory.CreateClient();
        var pmA = await LoginAsync(clientA, "pm@siam-construction.dev");
        Authorize(clientA, pmA.AccessToken);
        var projectAId = await GetSeededProjectIdAsync(factory, pmA.TenantId);
        var civilNodeAId = await ImportGoldenScheduleAsync(factory, clientA, projectAId);

        using var clientB = factory.CreateClient();
        var pmB = await LoginAsync(clientB, "pm@bkk-infra.dev");
        Authorize(clientB, pmB.AccessToken);
        var projectBId = await GetSeededProjectIdAsync(factory, pmB.TenantId);
        await ImportGoldenScheduleAsync(factory, clientB, projectBId);

        // Tenant B requests tenant A's real project id + real node id - the global query filter
        // must hide it completely, not merely reject a mismatched project/node pairing.
        var crossTenantResponse = await clientB.GetAsync(
            $"/api/v1/projects/{projectAId}/wbs-nodes/{civilNodeAId}/activities");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);

        // Sanity check: tenant A itself can still reach its own node - the filter hides tenant B's
        // view of it, it does not delete or corrupt the data.
        var ownTenantResponse = await clientA.GetAsync(
            $"/api/v1/projects/{projectAId}/wbs-nodes/{civilNodeAId}/activities");
        Assert.Equal(HttpStatusCode.OK, ownTenantResponse.StatusCode);
    }
}
