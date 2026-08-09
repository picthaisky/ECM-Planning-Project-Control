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
/// S8 (actual-cost.md §9/§10.4/§12, ADR-0013) end-to-end over real HTTP: `POST .../actual-costs`
/// through the real WebApi host (auth, RBAC, routing, ProblemDetails, MediatR pipeline, DI wiring
/// for the real <c>IActualCostReader</c>/<c>IActualCostRepository</c>), plus the cross-controller
/// proof this task exists for - that a posted <c>ActualCostEntry</c> genuinely flows into
/// <c>GET .../evm</c>'s <c>Ac</c>/<c>Cv</c>/EAC-variant output, not just into a fake in an
/// Application-layer unit test. Uses the dev-seeded project as-is (no activities imported, so
/// PV/EV stay 0 throughout) - this suite is about proving AC really reaches the EVM response, not
/// about re-proving EVM/EAC arithmetic (exhaustively covered elsewhere against fixture files).
/// </summary>
public class ActualCostsControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record EacVariantResponse(
        string Variant, decimal? PerformanceFactor, decimal? Etc, decimal? Eac, decimal? Vac, bool Computable, string? Reason);

    private sealed record EvmResponse(
        Guid ProjectId, DateTimeOffset DataDate, decimal Bac, decimal Pv, decimal Ev, decimal Ac,
        decimal Sv, decimal Cv, decimal? Spi, decimal? Cpi, decimal? TcpiBac, decimal? TcpiEac,
        string SelectedVariant, List<EacVariantResponse> Variants, List<string> Warnings);

    private sealed record ActualCostEntryResponse(Guid Id, Guid ProjectId, decimal Amount, DateTimeOffset IncurredDate, string Source);

    // design.md §2: every decimal is wire-serialized as a JSON string (DecimalAsStringJsonConverter).
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new DecimalAsStringJsonConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;

    public ActualCostsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    private async Task<LoginResponse> LoginAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = DevDataSeeder.DevSeedPassword }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<EvmResponse> GetEvmAsync(HttpClient client, Guid projectId)
    {
        var response = await client.GetAsync($"/api/v1/projects/{projectId}/evm");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EvmResponse>(ResponseJsonOptions))!;
    }

    // ------------------------------------------------------------------------------------
    // RBAC (actual-cost.md §10.4): QS/PM/ProjectDirector/Admin may post; Site must not.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Unauthenticated_Request_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/actual-costs",
            new { CostCategory = "Material", EntryType = "Actual", Amount = 1000.00m, IncurredDate = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_Site_User_Is_Forbidden_From_Posting_Cost_Data()
    {
        // actual-cost.md §10.4: cost data is commercially sensitive - site staff must not see or
        // post margin-bearing data.
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/actual-costs",
            new { CostCategory = "Material", EntryType = "Actual", Amount = 1000.00m, IncurredDate = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_Qs_User_Posting_A_Zero_Amount_Gets_A_Validation_Error()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);
        var projectId = await GetSeededProjectIdAsync(qs.TenantId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/actual-costs",
            new { CostCategory = "Material", EntryType = "Actual", Amount = 0.00m, IncurredDate = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/validation-error", problem!.Type);
    }

    [Fact]
    public async Task Posting_To_An_Unknown_Project_Returns_404()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/actual-costs",
            new { CostCategory = "Material", EntryType = "Actual", Amount = 1000.00m, IncurredDate = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------------------------
    // The core proof this task exists for: a posted ActualCostEntry flows into GET .../evm.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Posted_Cost_Entry_Flows_Into_The_Next_Get_Evm_Response()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(pm.TenantId);

        // Baseline: the seeded project has no activities and (before this test) no cost entries ->
        // PV=EV=AC=0 -> NotStarted for every variant (same baseline EvmControllerTests asserts).
        var before = await GetEvmAsync(client, projectId);
        Assert.Equal(0.00m, before.Ac);
        Assert.All(before.Variants, v => Assert.Equal("NotStarted", v.Reason));

        var postResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/actual-costs",
            new
            {
                CostCategory = "Subcontract",
                EntryType = "Actual",
                Amount = 350_000.00m,
                IncurredDate = before.DataDate,
                DocumentReference = "INV-2026-0001",
            });

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var posted = await postResponse.Content.ReadFromJsonAsync<ActualCostEntryResponse>(ResponseJsonOptions);
        Assert.NotNull(posted);
        Assert.Equal(350_000.00m, posted!.Amount);
        Assert.Equal("ManualEntry", posted.Source); // never trusted from the request - see RecordActualCostCommand's remarks.

        // The proof: GET .../evm now reflects the real posted amount, not the S7 literal-0.
        var after = await GetEvmAsync(client, projectId);
        Assert.Equal(350_000.00m, after.Ac);
        Assert.Equal(0.00m, after.Ev); // still no activities - only AC changed.
        Assert.Equal(-350_000.00m, after.Cv); // Cv = Ev - Ac, exactly.

        // CpiBased/CpiSpiBased: AC > 0 but EV = 0 -> a defined, computed CPI = 0 -> ZeroCpi (a
        // materially different, and correct, reason than the pre-AC NotStarted above - proof the
        // branch genuinely changed because of the posted entry, not a coincidence).
        var cpiBased = after.Variants.Single(v => v.Variant == "CpiBased");
        Assert.False(cpiBased.Computable);
        Assert.Equal("ZeroCpi", cpiBased.Reason);

        // Atypical has no CPI/SPI dependency at all - it is now genuinely computable with a real,
        // AC-derived EAC (= Ac + (Bac - Ev), PF = 1) instead of NotStarted.
        var atypical = after.Variants.Single(v => v.Variant == "Atypical");
        Assert.True(atypical.Computable);
        Assert.Equal(350_000.00m + after.Bac, atypical.Eac);
    }

    private async Task<Guid> GetSeededProjectIdAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        var project = await dbContext.Projects.IgnoreQueryFilters()
            .SingleAsync(p => p.TenantId == tenantId);
        return project.Id;
    }
}
