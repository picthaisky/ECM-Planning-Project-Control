using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S4-BE-04/06 (gap closure) end-to-end over real HTTP: <c>GET /api/v1/projects</c> (the tenant
/// project list) and the <c>validation-error</c> typed <see cref="ProblemDetails"/> a
/// <c>PUT /api/v1/projects/{id}</c> validation failure now produces - through the real WebApi host
/// (auth, routing, ProblemDetails, MediatR pipeline), not just the Application/Infrastructure
/// layers unit-tested elsewhere.
/// </summary>
public class ProjectsControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record ProjectListItemResponse(Guid Id, string Name, string Code, string Owner);

    private readonly CustomWebApplicationFactory _factory;

    public ProjectsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    private async Task<LoginResponse> LoginAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = DevDataSeeder.DevSeedPassword }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<CMPlus.Domain.Entities.Project> GetSeededProjectAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        return await dbContext.Projects.IgnoreQueryFilters().SingleAsync(p => p.TenantId == tenantId);
    }

    // ------------------------------------------------------------------------------------
    // Gap 1: GET /api/v1/projects (S4-BE-04).
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Unauthenticated_Request_To_List_Projects_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listing_Projects_Returns_Only_The_Callers_Own_Tenants_Project_Never_The_Other_Tenants()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);

        var response = await client.GetAsync("/api/v1/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var projects = await response.Content.ReadFromJsonAsync<List<ProjectListItemResponse>>();

        Assert.NotNull(projects);
        var project = Assert.Single(projects!);
        Assert.Equal("RCT-B", project.Code);
        Assert.DoesNotContain(projects!, p => p.Code == "SEE-P3");
    }

    [Fact]
    public async Task Listing_Projects_As_The_Other_Tenant_Returns_Only_That_Tenants_Project()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@bkk-infra.dev");
        Authorize(client, pm.AccessToken);

        var response = await client.GetAsync("/api/v1/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var projects = await response.Content.ReadFromJsonAsync<List<ProjectListItemResponse>>();

        Assert.NotNull(projects);
        var project = Assert.Single(projects!);
        Assert.Equal("SEE-P3", project.Code);
    }

    // ------------------------------------------------------------------------------------
    // Gap 1 role gate: deliberately broader than the edit endpoint - every role can list
    // projects (US-4.2 discovery), unlike PUT (PM/QS/Executive/Admin only).
    // ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("site")]
    [InlineData("planning")]
    [InlineData("pm")]
    [InlineData("qs")]
    [InlineData("executive")]
    [InlineData("admin")]
    [InlineData("projectdirector")]
    public async Task Every_Role_Can_List_Projects_Never_403(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);

        var response = await client.GetAsync("/api/v1/projects");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ------------------------------------------------------------------------------------
    // Gap 3: PUT /api/v1/projects/{id} validation failures now map to a distinct
    // "validation-error" ProblemDetails type instead of the generic "bad-request" bucket
    // (S4-BE-06).
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Validator_Rejected_Update_Returns_The_Distinct_Validation_Error_Problem_Type()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var project = await GetSeededProjectAsync(pm.TenantId);

        // Deliberately invalid: ContractFinish before ContractStart - UpdateProjectCommandValidator
        // rejects this (client-visible layer), never reaching the handler.
        var invalidPayload = new
        {
            project.Name,
            project.Code,
            project.Owner,
            ContractStart = DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            ContractFinish = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            Bac = project.BAC,
            project.ContractValue,
            project.RetentionRate,
            project.AdvanceRate,
            project.RetentionCapPercentage,
            project.RetentionRelease1Percentage,
            project.DefectsLiabilityMonths,
            project.AdvanceAmountPaid,
            AdvanceRecoveryMethod = project.AdvanceRecoveryMethod.ToString(),
            project.AdvanceRecoveryStartPct,
            project.AdvanceRecoveryRatePct,
            project.AdvanceRecoveryEndPct,
        };

        var response = await client.PutAsJsonAsync($"/api/v1/projects/{project.Id}", invalidPayload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(problem);
        Assert.Equal("https://cmplus.dev/problems/validation-error", problem!.Type);
        // Never the old, indistinguishable-from-anything-else generic bucket.
        Assert.NotEqual("https://cmplus.dev/problems/bad-request", problem.Type);
        Assert.Contains("ContractFinish cannot be earlier than ContractStart", problem.Detail);
    }
}
