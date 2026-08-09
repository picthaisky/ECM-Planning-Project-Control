using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Seed;
using CMPlus.WebApi.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S7-BE-03/04/05 end-to-end over real HTTP: `GET/POST .../evm`, `PUT .../eac-default` - through the
/// real WebApi host (auth, routing, ProblemDetails, MediatR pipeline, DI wiring for the new
/// <c>IEvmDataReader</c>/<c>IActualCostReader</c>/<c>IEvmSnapshotRepository</c> registrations), not
/// just the Application-layer handler tests (fakes) elsewhere. Uses the dev-seeded project as-is
/// (no activities imported) - PV/EV/AC are all zero, so every variant is the `NotStarted` edge case;
/// this suite is about proving the wiring/contract/roles, not re-proving engine arithmetic (that is
/// exhaustively covered by <c>EvmEngineTests</c>/<c>EacCalculatorTests</c> against the fixture files).
/// </summary>
public class EvmControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record EacVariantResponse(
        string Variant, decimal? PerformanceFactor, decimal? Etc, decimal? Eac, decimal? Vac, bool Computable, string? Reason);

    private sealed record EvmResponse(
        Guid ProjectId, DateTimeOffset DataDate, decimal Bac, decimal Pv, decimal Ev, decimal Ac,
        decimal Sv, decimal Cv, decimal? Spi, decimal? Cpi, decimal? TcpiBac, decimal? TcpiEac,
        string SelectedVariant, List<EacVariantResponse> Variants, List<string> Warnings);

    private sealed record SnapshotResponse(Guid SnapshotId, Guid ProjectId, DateTimeOffset DataDate, string EacVariant);

    // design.md §2: every decimal is wire-serialized as a JSON string (DecimalAsStringJsonConverter) -
    // the default System.Net.Http.Json options (JsonSerializerDefaults.Web) don't know that converter.
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new DecimalAsStringJsonConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;

    public EvmControllerTests(CustomWebApplicationFactory factory)
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

    // ------------------------------------------------------------------------------------
    // GET .../evm
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Unauthenticated_Request_To_Get_Evm_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/evm");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Getting_Evm_For_An_Unknown_Project_Returns_404()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/evm");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Getting_Evm_For_The_Seeded_Project_Returns_Every_Variant_With_The_Projects_Own_Default_Selected()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(pm.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/evm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evm = await response.Content.ReadFromJsonAsync<EvmResponse>(ResponseJsonOptions);

        Assert.NotNull(evm);
        Assert.Equal(projectId, evm!.ProjectId);
        Assert.Equal("CpiBased", evm.SelectedVariant); // seeded default (ADR-0007, Project.EacVariantDefault).
        Assert.Equal(5, evm.Variants.Count);

        // No activities imported for the seeded project -> PV=EV=AC=0 -> NotStarted for every variant
        // (never a fabricated 0.00/EAC).
        Assert.Equal(0.00m, evm.Sv);
        Assert.Equal(0.00m, evm.Cv);
        Assert.Null(evm.Spi);
        Assert.Null(evm.Cpi);
        Assert.All(evm.Variants, v =>
        {
            Assert.False(v.Computable);
            Assert.Equal("NotStarted", v.Reason);
            Assert.Null(v.Eac);
        });
    }

    [Fact]
    public async Task Getting_Evm_With_An_Unrecognised_EacVariant_Returns_400_With_The_Specific_Problem_Type()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(pm.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/evm?eacVariant=NotARealVariant");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(problem);
        Assert.Equal("https://cmplus.dev/problems/invalid-eac-variant", problem!.Type);
        Assert.Contains("NotARealVariant", problem.Detail);
    }

    // ------------------------------------------------------------------------------------
    // Read/close gating (ADR-0013): the EVM response exposes AC/CV/CPI/EAC, which on a
    // contractor-side tenant is the contractor's own margin. This was a bare [Authorize] while AC
    // was pinned at 0 by the placeholder reader; the real ActualCostEntry ledger made it live data,
    // so the gate is now enforced. These two tests exist so that regressing the attribute fails
    // loudly rather than silently re-opening the exposure.
    // ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("site")]
    [InlineData("planning")]
    public async Task Reading_Evm_Is_Forbidden_For_Roles_Without_Cost_Visibility(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/evm");

        // `planning` is the deliberate, documented trade-off recorded on EvmController: the response
        // mixes cost with schedule metrics, so gating fail-closed also withholds the schedule half
        // that Planning legitimately needs. Asserted here as the *current intended* behaviour, not
        // as an endorsement - if a later sprint splits cost from schedule fields, this row is
        // expected to change and should be updated deliberately, not deleted to make a build pass.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("pm")]
    [InlineData("qs")]
    [InlineData("executive")]
    [InlineData("projectdirector")]
    [InlineData("admin")]
    public async Task Reading_Evm_Is_Allowed_For_Financially_Trusted_Roles(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/evm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ------------------------------------------------------------------------------------
    // PUT .../eac-default (ADR-0007(f): PM/QS/Executive only).
    // ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("site")]
    [InlineData("planning")]
    [InlineData("admin")]
    [InlineData("projectdirector")]
    public async Task Setting_The_Eac_Default_Is_Forbidden_For_Roles_Outside_Pm_Qs_Executive(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/projects/{projectId}/eac-default", new { Variant = "Atypical" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("pm")]
    [InlineData("qs")]
    [InlineData("executive")]
    public async Task Setting_The_Eac_Default_Succeeds_For_Pm_Qs_Or_Executive_And_A_Later_Get_Reflects_It(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var putResponse = await client.PutAsJsonAsync(
            $"/api/v1/projects/{projectId}/eac-default", new { Variant = "CpiSpiBased" });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/projects/{projectId}/evm");
        var evm = await getResponse.Content.ReadFromJsonAsync<EvmResponse>(ResponseJsonOptions);

        Assert.Equal("CpiSpiBased", evm!.SelectedVariant);

        // Reset back to the seeded default so other tests sharing this fixture/project are unaffected.
        await client.PutAsJsonAsync($"/api/v1/projects/{projectId}/eac-default", new { Variant = "CpiBased" });
    }

    // ------------------------------------------------------------------------------------
    // POST .../evm/snapshots (S7-BE-05: immutable, duplicate dataDate -> 409).
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Closing_An_Evm_Period_Succeeds_Then_Closing_The_Same_Data_Date_Again_Returns_409()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(pm.TenantId);

        // A data date unique to this test, so it cannot collide with any other test sharing this
        // class fixture's database.
        var dataDate = DateTimeOffset.Parse("2026-04-30T00:00:00+07:00");

        var firstClose = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/evm/snapshots", new { DataDate = dataDate });
        Assert.Equal(HttpStatusCode.Created, firstClose.StatusCode);
        var snapshot = await firstClose.Content.ReadFromJsonAsync<SnapshotResponse>(ResponseJsonOptions);
        Assert.NotNull(snapshot);
        Assert.Equal(projectId, snapshot!.ProjectId);

        var secondClose = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/evm/snapshots", new { DataDate = dataDate });
        Assert.Equal(HttpStatusCode.Conflict, secondClose.StatusCode);
        var problem = await secondClose.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/evm-snapshot-already-exists", problem!.Type);
    }
}
