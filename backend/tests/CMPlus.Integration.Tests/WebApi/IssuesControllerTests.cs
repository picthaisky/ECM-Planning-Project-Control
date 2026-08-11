using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S11-BE-03 (US-11.2) end-to-end over real HTTP: <c>GET/POST /api/v1/projects/{id}/issues</c> and
/// <c>POST /api/v1/projects/{id}/issues/{issueId}/advance-status</c> (domain-rules.md weather-eot
/// §9.1 - nested under the project route, landed mid-implementation and superseding this task's
/// earlier flat-route judgement call) through the real WebApi host.
/// </summary>
public class IssuesControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record IssueResponse(
        Guid Id, Guid ProjectId, int? SequenceNo, string Title, string? Detail, string? Owner,
        DateTimeOffset? DueDate, string Status, DateTimeOffset? StartedAt, DateTimeOffset? ClosedAt,
        Guid CreatedByUserId, DateTimeOffset CreatedAt);

    private sealed record IssueStatusCountsResponse(int Open, int Doing, int Closed);

    private sealed record IssueListResponse(List<IssueResponse> Items, int TotalCount, IssueStatusCountsResponse StatusCounts);

    private readonly CustomWebApplicationFactory _factory;

    public IssuesControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    private async Task<LoginResponse> LoginAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = DevDataSeeder.DevSeedPassword }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static object DefaultIssuePayload(string title = "เหล็กเส้น DB25 ส่งช้า") => new
    {
        Title = title,
        Detail = "ซัพพลายเออร์แจ้งเลื่อน 5 วัน กระทบชั้น 9",
        Owner = "จัดซื้อ",
        DueDate = DateTimeOffset.Parse("2026-07-15T00:00:00+07:00"),
    };

    private async Task<Guid> GetSeededProjectIdAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        var project = await dbContext.Projects.IgnoreQueryFilters().SingleAsync(p => p.TenantId == tenantId);
        return project.Id;
    }

    private static string AdvanceStatusUrl(Guid projectId, Guid issueId) =>
        $"/api/v1/projects/{projectId}/issues/{issueId}/advance-status";

    private async Task<IssueListResponse> GetIssuesAsync(HttpClient client, Guid projectId)
    {
        var response = await client.GetAsync($"/api/v1/projects/{projectId}/issues");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IssueListResponse>())!;
    }

    // ------------------------------------------------------------------------------------
    // RBAC - mirrors ProjectWeatherLogsController's identical write-narrower-than-read reasoning.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Unauthenticated_Request_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{Guid.NewGuid()}/issues", DefaultIssuePayload());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_Site_User_Can_Create_An_Issue()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/issues", DefaultIssuePayload());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var issue = await response.Content.ReadFromJsonAsync<IssueResponse>();
        Assert.NotNull(issue);
        Assert.Equal("Open", issue!.Status);
        Assert.Null(issue.StartedAt);
        Assert.Null(issue.ClosedAt);
    }

    [Fact]
    public async Task An_Executive_Is_Forbidden_From_Creating_An_Issue_But_Can_Still_Read_The_Register()
    {
        using var client = _factory.CreateClient();
        var executive = await LoginAsync(client, "executive@siam-construction.dev");
        Authorize(client, executive.AccessToken);
        var projectId = await GetSeededProjectIdAsync(executive.TenantId);

        var createResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/issues", DefaultIssuePayload());
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);

        var readResponse = await client.GetAsync($"/api/v1/projects/{projectId}/issues");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
    }

    [Fact]
    public async Task Advancing_An_Unknown_Issue_Id_Returns_404()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var response = await client.PostAsync(AdvanceStatusUrl(projectId, Guid.NewGuid()), content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Advancing_An_Issue_Through_Another_Tenants_Project_Route_Returns_404_Not_A_Cross_Tenant_Mutation()
    {
        using var client = _factory.CreateClient();
        var siamSite = await LoginAsync(client, "site@siam-construction.dev");
        var siamProjectId = await GetSeededProjectIdAsync(siamSite.TenantId);
        Authorize(client, siamSite.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/v1/projects/{siamProjectId}/issues", DefaultIssuePayload()))
            .Content.ReadFromJsonAsync<IssueResponse>();

        using var otherClient = _factory.CreateClient();
        var bkkSite = await LoginAsync(otherClient, "site@bkk-infra.dev");
        var bkkProjectId = await GetSeededProjectIdAsync(bkkSite.TenantId);
        Authorize(otherClient, bkkSite.AccessToken);

        // Cross-tenant: the id does not even resolve under bkkSite's tenant filter at all.
        var crossTenantResponse = await otherClient.PostAsync(AdvanceStatusUrl(bkkProjectId, created!.Id), content: null);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);

        // Same-tenant, wrong-project: the issue resolves but belongs to a different project than
        // the one named in the route - also 404 (AdvanceIssueStatusCommand's own remarks).
        var wrongProjectResponse = await client.PostAsync(AdvanceStatusUrl(Guid.NewGuid(), created.Id), content: null);
        Assert.Equal(HttpStatusCode.NotFound, wrongProjectResponse.StatusCode);
    }

    // ------------------------------------------------------------------------------------
    // The state machine over real HTTP.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Issue_Advances_Open_To_Doing_To_Closed_Stamping_StartedAt_And_ClosedAt_On_The_Right_Steps()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var created = (await (await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/issues", DefaultIssuePayload()))
            .Content.ReadFromJsonAsync<IssueResponse>())!;
        Assert.Equal("Open", created.Status);

        var doingResponse = await client.PostAsync(AdvanceStatusUrl(projectId, created.Id), content: null);
        Assert.Equal(HttpStatusCode.OK, doingResponse.StatusCode);
        var doing = (await doingResponse.Content.ReadFromJsonAsync<IssueResponse>())!;
        Assert.Equal("Doing", doing.Status);
        Assert.NotNull(doing.StartedAt);
        Assert.Null(doing.ClosedAt); // not yet - only Closed stamps it.

        var closedResponse = await client.PostAsync(AdvanceStatusUrl(projectId, created.Id), content: null);
        Assert.Equal(HttpStatusCode.OK, closedResponse.StatusCode);
        var closed = (await closedResponse.Content.ReadFromJsonAsync<IssueResponse>())!;
        Assert.Equal("Closed", closed.Status);
        Assert.Equal(doing.StartedAt, closed.StartedAt); // unchanged from the earlier transition
        Assert.NotNull(closed.ClosedAt);

        // Terminal - a fourth advance is refused, not silently accepted.
        var refused = await client.PostAsync(AdvanceStatusUrl(projectId, created.Id), content: null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    // ------------------------------------------------------------------------------------
    // S11-BE-03 DoD / domain-rules.md §9.3, the one this task's brief said is not decoration: the
    // tile counter must match the real data, end to end over HTTP (not just at the handler-unit
    // level).
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Tile_Counts_In_The_List_Response_Exactly_Match_The_Statuses_Of_The_Items_In_The_Same_Response()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        // Baseline BEFORE this test's own writes - CustomWebApplicationFactory's database (and this
        // seeded project) is shared across every [Fact] in this class (IClassFixture), so other
        // tests' issues may already be sitting in it. This test asserts a STRUCTURAL invariant that
        // must hold regardless of that shared state, not an absolute "there are exactly 3" count.
        var before = await GetIssuesAsync(client, projectId);

        // Three issues: one left Open, one advanced to Doing, one advanced all the way to Closed.
        var openOne = (await (await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/issues", DefaultIssuePayload("stays open")))
            .Content.ReadFromJsonAsync<IssueResponse>())!;
        var doingOne = (await (await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/issues", DefaultIssuePayload("goes to doing")))
            .Content.ReadFromJsonAsync<IssueResponse>())!;
        await client.PostAsync(AdvanceStatusUrl(projectId, doingOne.Id), content: null);
        var closedOne = (await (await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/issues", DefaultIssuePayload("goes to closed")))
            .Content.ReadFromJsonAsync<IssueResponse>())!;
        await client.PostAsync(AdvanceStatusUrl(projectId, closedOne.Id), content: null);
        await client.PostAsync(AdvanceStatusUrl(projectId, closedOne.Id), content: null);

        var list = await GetIssuesAsync(client, projectId);

        Assert.Equal(before.Items.Count + 3, list.Items.Count);
        Assert.Equal(before.TotalCount + 3, list.TotalCount);

        // The structural proof, computed independently from list.Items by THIS TEST (not by
        // production code) - if the tile counters and the table rows had come from two different
        // queries, this is exactly the assertion that would catch them disagreeing. Holds
        // regardless of how many other issues pre-existed from other tests in this shared fixture.
        Assert.Equal(list.Items.Count(i => i.Status == "Open"), list.StatusCounts.Open);
        Assert.Equal(list.Items.Count(i => i.Status == "Doing"), list.StatusCounts.Doing);
        Assert.Equal(list.Items.Count(i => i.Status == "Closed"), list.StatusCounts.Closed);
        Assert.Equal(list.TotalCount, list.StatusCounts.Open + list.StatusCounts.Doing + list.StatusCounts.Closed);

        // This test's own three writes moved each bucket by exactly +1 relative to its own baseline.
        Assert.Equal(before.StatusCounts.Open + 1, list.StatusCounts.Open);
        Assert.Equal(before.StatusCounts.Doing + 1, list.StatusCounts.Doing);
        Assert.Equal(before.StatusCounts.Closed + 1, list.StatusCounts.Closed);
    }

    [Fact]
    public async Task Advancing_An_Issue_Then_Re_Listing_Moves_The_Tile_Counts_By_Exactly_One_In_Each_Direction()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var issue = (await (await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/issues", DefaultIssuePayload()))
            .Content.ReadFromJsonAsync<IssueResponse>())!;

        var before = await GetIssuesAsync(client, projectId);
        var openBefore = before.StatusCounts.Open;
        var doingBefore = before.StatusCounts.Doing;

        await client.PostAsync(AdvanceStatusUrl(projectId, issue.Id), content: null);

        var after = await GetIssuesAsync(client, projectId);
        Assert.Equal(openBefore - 1, after.StatusCounts.Open);
        Assert.Equal(doingBefore + 1, after.StatusCounts.Doing);
        Assert.Equal(before.TotalCount, after.TotalCount);
    }
}
