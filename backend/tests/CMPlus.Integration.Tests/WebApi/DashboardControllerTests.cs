using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Seed;
using CMPlus.WebApi.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S8-BE-02 end-to-end over real HTTP: `GET .../dashboard` through the real WebApi host (auth,
/// routing, ProblemDetails, MediatR pipeline, DI wiring for the new
/// <c>IWbsProgressReader</c>/reused <c>IWbsTreeReader</c>). Uses the dev-seeded project as-is (no
/// WBS nodes/activities) - this suite proves wiring/contract/roles, not engine arithmetic (that is
/// <c>GetDashboardQueryHandlerTests</c>'/<c>WbsProgressRollupCalculatorTests</c>' job).
/// </summary>
public class DashboardControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record ProgressRollupResponse(decimal ProgressPercentage, List<object> WeightWarnings, List<Guid> MixedScopeWbsNodeIds);

    private sealed record DashboardResponse(
        Guid ProjectId, DateTimeOffset DataDate, decimal Bac, decimal Pv, decimal Ev, decimal Ac,
        decimal Sv, decimal Cv, decimal? Spi, decimal? Cpi, int ActualCostEntryCount,
        string EacVariant, decimal? PerformanceFactor, decimal? Etc, decimal? Eac, decimal? Vac,
        bool EacComputable, string? EacNullReason, ProgressRollupResponse ProgressRollup, List<string> Warnings);

    // design.md §2: every decimal is wire-serialized as a JSON string (DecimalAsStringJsonConverter).
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new DecimalAsStringJsonConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;

    public DashboardControllerTests(CustomWebApplicationFactory factory)
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
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        var project = await dbContext.Projects.IgnoreQueryFilters().SingleAsync(p => p.TenantId == tenantId);
        return project.Id;
    }

    [Fact]
    public async Task An_Unauthenticated_Request_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Getting_The_Dashboard_For_An_Unknown_Project_Returns_404()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/dashboard");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("site")]
    [InlineData("planning")]
    public async Task Reading_The_Dashboard_Is_Forbidden_For_Roles_Without_Cost_Visibility(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/dashboard");

        // Same documented ADR-0013 gate/trade-off as EvmController - see that controller's tests.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("pm")]
    [InlineData("qs")]
    [InlineData("executive")]
    [InlineData("projectdirector")]
    [InlineData("admin")]
    public async Task Reading_The_Dashboard_Is_Allowed_For_Financially_Trusted_Roles(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_Seeded_Project_With_No_Wbs_Or_Activities_Returns_A_Zero_Progress_Rollup_And_NotStarted_Eac()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(pm.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<DashboardResponse>(ResponseJsonOptions);

        Assert.NotNull(dashboard);
        Assert.Equal(projectId, dashboard!.ProjectId);
        Assert.Equal("CpiBased", dashboard.EacVariant); // seeded default (ADR-0007, Project.EacVariantDefault).
        Assert.False(dashboard.EacComputable);
        Assert.Equal("NotStarted", dashboard.EacNullReason); // no activities imported -> PV=EV=AC=0.
        Assert.Null(dashboard.Eac);

        Assert.Equal(0.00m, dashboard.ProgressRollup.ProgressPercentage);
        Assert.Empty(dashboard.ProgressRollup.WeightWarnings); // no WBS nodes at all -> nothing to warn about.
        Assert.Empty(dashboard.ProgressRollup.MixedScopeWbsNodeIds);
    }
}
