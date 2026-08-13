using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Auth;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S14-BE-01/02 end-to-end over real HTTP (auth, routing, RBAC, ProblemDetails, MediatR pipeline,
/// the real EF Core InMemory-backed <c>CmPlusDbContext</c> - not just the Application layer's
/// hand-written fakes). Mirrors <c>VariationOrdersControllerTests</c>'s isolated-tenant-per-test
/// pattern: <see cref="CustomWebApplicationFactory"/> is shared (<c>IClassFixture</c>) across every
/// test in this class, so any test that mutates Baseline state uses its own dedicated tenant/project
/// rather than the shared dev-seeded one, to avoid cross-test interference.
/// </summary>
public class BaselinesControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record BaselineResponse(Guid Id, Guid ProjectId, string Name, bool IsActive, decimal Bac, int ActivityCount);

    private sealed record ActivateBaselineResponse(Guid Id, Guid ProjectId, bool IsActive);

    private sealed record BaselineActivityDeltaResponse(
        Guid ActivityId, string? ActivityCode, bool IsRemoved, int? StartVarianceDays, int? FinishVarianceDays,
        int? DurationVarianceDays, decimal? BudgetVarianceAmount);

    private sealed record BaselineComparisonResponse(
        Guid ProjectId, Guid BaselineId, string BaselineName, int TotalActivityCount, int DriftedActivityCount,
        int? ProjectFinishVarianceDays, decimal CurrentBac, decimal BaselineBac, decimal BacVarianceAmount,
        List<BaselineActivityDeltaResponse> Activities);

    private readonly CustomWebApplicationFactory _factory;

    public BaselinesControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    private async Task<LoginResponse> LoginAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = DevDataSeeder.DevSeedPassword }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<Guid> GetSeededProjectIdAsync(Guid tenantId)
    {
        using var context = _factory.CreateDbContextForSeeding(tenantId);
        var project = await context.Projects.IgnoreQueryFilters().SingleAsync(p => p.TenantId == tenantId);
        return project.Id;
    }

    /// <summary>Isolated tenant + one user per requested role, mirroring
    /// <c>VariationOrdersControllerTests.SeedIsolatedTenant</c>'s exact rationale/shape.</summary>
    private (Guid TenantId, string EmailDomain) SeedIsolatedTenant(params UserRole[] roles)
    {
        var emailDomain = $"baseline-http-fixture-{Guid.NewGuid():N}.dev";
        var tenant = new Tenant($"Baseline HTTP Fixture Tenant {Guid.NewGuid():N}");

        using var context = _factory.CreateDbContextForSeeding(tenant.Id);
        context.Tenants.Add(tenant);

        var passwordHash = new Pbkdf2PasswordHasher().Hash(DevDataSeeder.DevSeedPassword);
        foreach (var role in roles)
        {
            context.Users.Add(new User(tenant.Id, $"{role.ToString().ToLowerInvariant()}@{emailDomain}", role, passwordHash));
        }

        context.SaveChanges();
        return (tenant.Id, emailDomain);
    }

    private Guid SeedIsolatedProject(Guid tenantId, decimal bac = 492_400_000.00m)
    {
        using var context = _factory.CreateDbContextForSeeding(tenantId);
        var project = Project.Create(
            tenantId, "Baseline HTTP Fixture Project", $"BLHTTP-{Guid.NewGuid():N}", "Owner",
            DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(1), bac, DateTimeOffset.UtcNow);
        context.Projects.Add(project);
        context.SaveChanges();
        return project.Id;
    }

    private Guid SeedActivity(Guid tenantId, Guid projectId, DateTimeOffset start, int durationDays, decimal budgetCost)
    {
        using var context = _factory.CreateDbContextForSeeding(tenantId);
        var node = new WBSNode(tenantId, projectId, $"C-{Guid.NewGuid():N}", "Node", 100m);
        context.WBSNodes.Add(node);
        var activity = new Activity(
            tenantId, node.Id, $"A-{Guid.NewGuid():N}", "Activity", start, start.AddDays(durationDays), durationDays, budgetCost);
        context.Activities.Add(activity);
        context.SaveChanges();
        return activity.Id;
    }

    private static string BaselinesUrl(Guid projectId) => $"/api/v1/projects/{projectId}/baselines";

    // ------------------------------------------------------------------------------------
    // AuthN/AuthZ
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Unauthenticated_Request_To_Capture_A_Baseline_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(BaselinesUrl(Guid.NewGuid()), new { Name = "BL-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("site")]
    [InlineData("qs")]
    [InlineData("executive")]
    [InlineData("projectdirector")]
    public async Task A_Role_Outside_PM_Planning_Admin_Is_Forbidden_From_Capturing_A_Baseline(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var response = await client.PostAsJsonAsync(BaselinesUrl(projectId), new { Name = "BL-1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("pm")]
    [InlineData("planning")]
    [InlineData("admin")]
    public async Task PM_Planning_Or_Admin_Can_Capture_A_Baseline(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var response = await client.PostAsJsonAsync(BaselinesUrl(projectId), new { Name = $"BL-{Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("site")]
    [InlineData("planning")]
    public async Task A_Role_Outside_The_Compare_Audience_Is_Forbidden_From_Comparing(string roleEmailPrefix)
    {
        // CashFlowController/EvmController's identical gate, inherited for the same reason (this
        // response carries BudgetCost/BAC figures) - see BaselinesController's own remarks on the
        // documented Planning trade-off.
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var response = await client.GetAsync($"{BaselinesUrl(projectId)}/compare");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task QS_Can_Read_The_Comparison_But_Cannot_Capture_Or_Activate()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);
        var projectId = await GetSeededProjectIdAsync(qs.TenantId);

        var captureResponse = await client.PostAsJsonAsync(BaselinesUrl(projectId), new { Name = "BL-1" });
        Assert.Equal(HttpStatusCode.Forbidden, captureResponse.StatusCode);

        var activateResponse = await client.PostAsync($"{BaselinesUrl(projectId)}/{Guid.NewGuid()}/activate", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, activateResponse.StatusCode);

        var compareResponse = await client.GetAsync($"{BaselinesUrl(projectId)}/compare");
        Assert.NotEqual(HttpStatusCode.Forbidden, compareResponse.StatusCode);
    }

    // ------------------------------------------------------------------------------------
    // Capture + activate + compare, full flow, over real HTTP + real (InMemory) persistence.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Capturing_A_Baseline_Snapshots_Every_Current_Activity_And_Is_Never_Active()
    {
        using var client = _factory.CreateClient();
        var (tenantId, emailDomain) = SeedIsolatedTenant(UserRole.PM);
        var projectId = SeedIsolatedProject(tenantId, bac: 485_000_000.00m);
        SeedActivity(tenantId, projectId, DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"), 14, 1_000_000.00m);
        SeedActivity(tenantId, projectId, DateTimeOffset.Parse("2026-01-15T00:00:00+07:00"), 10, 500_000.00m);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        var response = await client.PostAsJsonAsync(BaselinesUrl(projectId), new { Name = "Original Baseline (สัญญา)" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var baseline = await response.Content.ReadFromJsonAsync<BaselineResponse>();
        Assert.NotNull(baseline);
        Assert.False(baseline!.IsActive);
        Assert.Equal(2, baseline.ActivityCount);
        Assert.Equal(485_000_000.00m, baseline.Bac);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task Activating_An_Unknown_Baseline_Id_Returns_404()
    {
        using var client = _factory.CreateClient();
        var (tenantId, emailDomain) = SeedIsolatedTenant(UserRole.PM);
        var projectId = SeedIsolatedProject(tenantId);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        var response = await client.PostAsync($"{BaselinesUrl(projectId)}/{Guid.NewGuid()}/activate", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Activating_A_Baseline_That_Belongs_To_A_Different_Project_Returns_404()
    {
        using var client = _factory.CreateClient();
        var (tenantId, emailDomain) = SeedIsolatedTenant(UserRole.PM);
        var projectAId = SeedIsolatedProject(tenantId);
        var projectBId = SeedIsolatedProject(tenantId);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        var captureResponse = await client.PostAsJsonAsync(BaselinesUrl(projectAId), new { Name = "BL for A" });
        var baselineForA = (await captureResponse.Content.ReadFromJsonAsync<BaselineResponse>())!;

        // Well-formed baseline id, wrong project in the route - indistinguishable from "does not
        // exist", never a 403/422 that would confirm the id exists under a different project.
        var response = await client.PostAsync($"{BaselinesUrl(projectBId)}/{baselineForA.Id}/activate", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Comparing_Before_Any_Baseline_Is_Active_Returns_422_NoActiveBaseline()
    {
        using var client = _factory.CreateClient();
        var (tenantId, emailDomain) = SeedIsolatedTenant(UserRole.PM);
        var projectId = SeedIsolatedProject(tenantId);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        // Capture (but deliberately never activate) a baseline first, to prove the 422 is about
        // "no ACTIVE baseline", not "no baseline at all".
        await client.PostAsJsonAsync(BaselinesUrl(projectId), new { Name = "BL-0 (never activated)" });

        var response = await client.GetAsync($"{BaselinesUrl(projectId)}/compare");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/baseline-no-active-baseline", problem!.Type);
    }

    /// <summary>
    /// The mutation evidence this task explicitly asks for, run over the real HTTP + WebApi DI +
    /// EF Core InMemory-backed stack (the strongest evidence this environment can produce - see
    /// <c>ActivateBaselineCommandHandler</c>'s remarks on the DB-level filtered-unique-index half
    /// this specific provider still cannot prove): capturing two baselines and activating each in
    /// turn leaves exactly one active, verified both from the HTTP response and by re-reading the
    /// database directly.
    /// </summary>
    [Fact]
    public async Task Activating_A_Second_Baseline_Deactivates_The_First_Exactly_One_Stays_Active()
    {
        using var client = _factory.CreateClient();
        var (tenantId, emailDomain) = SeedIsolatedTenant(UserRole.PM);
        var projectId = SeedIsolatedProject(tenantId);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        var blOriginal = (await (await client.PostAsJsonAsync(BaselinesUrl(projectId), new { Name = "BL-0 (Original)" }))
            .Content.ReadFromJsonAsync<BaselineResponse>())!;
        var blRevised = (await (await client.PostAsJsonAsync(BaselinesUrl(projectId), new { Name = "BL-1 (Revised)" }))
            .Content.ReadFromJsonAsync<BaselineResponse>())!;

        var firstActivate = await client.PostAsync($"{BaselinesUrl(projectId)}/{blOriginal.Id}/activate", content: null);
        Assert.Equal(HttpStatusCode.OK, firstActivate.StatusCode);

        var secondActivate = await client.PostAsync($"{BaselinesUrl(projectId)}/{blRevised.Id}/activate", content: null);
        Assert.Equal(HttpStatusCode.OK, secondActivate.StatusCode);
        var secondActivateBody = await secondActivate.Content.ReadFromJsonAsync<ActivateBaselineResponse>();
        Assert.True(secondActivateBody!.IsActive);

        using var verifyContext = _factory.CreateDbContextForSeeding(tenantId);
        var allBaselines = await verifyContext.Baselines.Where(b => b.ProjectId == projectId).ToListAsync();
        Assert.Equal(2, allBaselines.Count);
        Assert.Single(allBaselines, b => b.IsActive); // exactly one, never zero, never two.
        Assert.True(allBaselines.Single(b => b.Id == blRevised.Id).IsActive);
        Assert.False(allBaselines.Single(b => b.Id == blOriginal.Id).IsActive);
    }

    [Fact]
    public async Task Comparing_After_Activation_Returns_The_Per_Activity_Delta_And_Aggregate_Tiles()
    {
        using var client = _factory.CreateClient();
        var (tenantId, emailDomain) = SeedIsolatedTenant(UserRole.PM, UserRole.QS);
        var projectId = SeedIsolatedProject(tenantId, bac: 485_000_000.00m);
        var start = DateTimeOffset.Parse("2026-01-30T00:00:00+07:00");
        SeedActivity(tenantId, projectId, start, 24, 1_000_000.00m);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        var baseline = (await (await client.PostAsJsonAsync(BaselinesUrl(projectId), new { Name = "BL-0" }))
            .Content.ReadFromJsonAsync<BaselineResponse>())!;
        await client.PostAsync($"{BaselinesUrl(projectId)}/{baseline.Id}/activate", content: null);

        // Grow the project's own BAC after the baseline was captured (VO-like), so the comparison
        // has a genuine variance to report on the money tile. The schedule itself is left untouched
        // deliberately (asserted as StartVarianceDays == 0 below) - this test's focus is the
        // BAC-variance tile and the response shape, not schedule drift (covered separately by the
        // handler-level Act214 fixture test).
        using (var mutateContext = _factory.CreateDbContextForSeeding(tenantId))
        {
            var project = await mutateContext.Projects.SingleAsync(p => p.Id == projectId);
            project.SetBac(492_400_000.00m);
            await mutateContext.SaveChangesAsync();
        }

        var qs = await LoginAsync(client, $"qs@{emailDomain}");
        using var qsClient = _factory.CreateClient();
        Authorize(qsClient, qs.AccessToken);

        var response = await qsClient.GetAsync($"{BaselinesUrl(projectId)}/compare");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var comparison = await response.Content.ReadFromJsonAsync<BaselineComparisonResponse>();
        Assert.NotNull(comparison);
        Assert.Equal(baseline.Id, comparison!.BaselineId);
        Assert.Equal(1, comparison.TotalActivityCount);
        Assert.Equal(492_400_000.00m, comparison.CurrentBac);
        Assert.Equal(485_000_000.00m, comparison.BaselineBac);
        Assert.Equal(7_400_000.00m, comparison.BacVarianceAmount);
        var row = Assert.Single(comparison.Activities);
        Assert.False(row.IsRemoved);
        Assert.Equal(0, row.StartVarianceDays); // schedule itself was not moved above
    }

    // ------------------------------------------------------------------------------------
    // List (US-14.1) — GET /projects/{id}/baselines
    // ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("site")]
    [InlineData("planning")]
    public async Task A_Role_Outside_The_Money_Audience_Is_Forbidden_From_Listing_Baselines(string roleEmailPrefix)
    {
        // The list carries each baseline's Bac, so it is gated exactly like Compare, not like
        // Capture - Planning can capture but, because the list exposes BAC, cannot read it here.
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var response = await client.GetAsync(BaselinesUrl(projectId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Listing_Returns_The_Projects_Baselines_Newest_First_With_Their_Activity_Counts()
    {
        using var client = _factory.CreateClient();
        var (tenantId, emailDomain) = SeedIsolatedTenant(UserRole.PM, UserRole.QS);
        var projectId = SeedIsolatedProject(tenantId, bac: 485_000_000.00m);
        SeedActivity(tenantId, projectId, DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"), 14, 1_000_000.00m);
        SeedActivity(tenantId, projectId, DateTimeOffset.Parse("2026-01-15T00:00:00+07:00"), 10, 500_000.00m);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        // Two captures over the real stack; the second is newer.
        var first = (await (await client.PostAsJsonAsync(BaselinesUrl(projectId), new { Name = "BL-0 (Original)" }))
            .Content.ReadFromJsonAsync<BaselineResponse>())!;
        var second = (await (await client.PostAsJsonAsync(BaselinesUrl(projectId), new { Name = "BL-1 (Revised)" }))
            .Content.ReadFromJsonAsync<BaselineResponse>())!;
        await client.PostAsync($"{BaselinesUrl(projectId)}/{first.Id}/activate", content: null);

        // Read the list as QS (a money-audience role that cannot capture) - proves the read gate is
        // wider than the write gate.
        var qs = await LoginAsync(client, $"qs@{emailDomain}");
        using var qsClient = _factory.CreateClient();
        Authorize(qsClient, qs.AccessToken);

        var response = await qsClient.GetAsync(BaselinesUrl(projectId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<BaselineResponse>>();
        Assert.NotNull(list);
        Assert.Collection(
            list!,
            newest =>
            {
                Assert.Equal(second.Id, newest.Id); // newest capture first
                Assert.Equal("BL-1 (Revised)", newest.Name);
                Assert.False(newest.IsActive);
                // The bug-catcher: ActivityCount is Ignore()'d/computed, so a parent-only load would
                // read 0 - the real query must project Snapshots.Count. Both baselines snapshotted the
                // same 2 seeded activities.
                Assert.Equal(2, newest.ActivityCount);
                Assert.Equal(485_000_000.00m, newest.Bac);
            },
            oldest =>
            {
                Assert.Equal(first.Id, oldest.Id);
                Assert.True(oldest.IsActive); // the one we activated
                Assert.Equal(2, oldest.ActivityCount);
            });
    }

    [Fact]
    public async Task Listing_Does_Not_Include_Another_Projects_Baselines()
    {
        using var client = _factory.CreateClient();
        var (tenantId, emailDomain) = SeedIsolatedTenant(UserRole.PM);
        var projectAId = SeedIsolatedProject(tenantId);
        var projectBId = SeedIsolatedProject(tenantId);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);
        var blForA = (await (await client.PostAsJsonAsync(BaselinesUrl(projectAId), new { Name = "BL for A" }))
            .Content.ReadFromJsonAsync<BaselineResponse>())!;

        var response = await client.GetAsync(BaselinesUrl(projectBId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<BaselineResponse>>();
        Assert.NotNull(list);
        Assert.DoesNotContain(list!, b => b.Id == blForA.Id);
        Assert.Empty(list!); // project B has none of its own
    }

    [Fact]
    public async Task Listing_A_Project_With_No_Baselines_Returns_An_Empty_200_Never_404s()
    {
        using var client = _factory.CreateClient();
        var (tenantId, emailDomain) = SeedIsolatedTenant(UserRole.PM);
        var projectId = SeedIsolatedProject(tenantId);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        var response = await client.GetAsync(BaselinesUrl(projectId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<BaselineResponse>>();
        Assert.NotNull(list);
        Assert.Empty(list!);
    }
}
