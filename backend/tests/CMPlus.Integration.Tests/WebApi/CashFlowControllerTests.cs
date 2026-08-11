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
/// S8-BE-01 end-to-end over real HTTP: `GET .../cash-flow` through the real WebApi host (auth,
/// routing, ProblemDetails, MediatR pipeline, DI wiring for <c>IEvmSnapshotRepository</c> reused
/// from Sprint 7). Uses the dev-seeded project as-is (no activities/cost entries/closed periods) -
/// this suite proves wiring/contract/roles, not engine arithmetic (that is
/// <c>GetCashFlowQueryHandlerTests</c>' job against fakes, and
/// <c>EvmEngineTests</c>/<c>EacCalculatorTests</c> for the underlying formulas).
/// </summary>
public class CashFlowControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record ReceiptsResponse(bool IsAvailable, decimal? Cumulative, List<object> Periods, string? UnavailableReason);

    private sealed record PeriodResponse(
        DateTimeOffset? PeriodStart, DateTimeOffset PeriodEnd, bool IsClosed,
        decimal PvPeriod, decimal EvPeriod, decimal AcPeriod,
        decimal PvCumulative, decimal EvCumulative, decimal AcCumulative);

    private sealed record CashFlowResponse(
        Guid ProjectId, DateTimeOffset DataDate, decimal Bac,
        decimal PvCumulative, decimal EvCumulative, decimal AcCumulative, int ActualCostEntryCount,
        List<PeriodResponse> Periods, ReceiptsResponse Receipts, decimal? NetCashPosition, List<string> Warnings);

    // design.md §2: every decimal is wire-serialized as a JSON string (DecimalAsStringJsonConverter).
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new DecimalAsStringJsonConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;

    public CashFlowControllerTests(CustomWebApplicationFactory factory)
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

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/cash-flow");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Getting_Cash_Flow_For_An_Unknown_Project_Returns_404()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/cash-flow");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("site")]
    [InlineData("planning")]
    public async Task Reading_Cash_Flow_Is_Forbidden_For_Roles_Without_Cost_Visibility(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/cash-flow");

        // Same documented ADR-0013 gate/trade-off as EvmController - see that controller's tests.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("pm")]
    [InlineData("qs")]
    [InlineData("executive")]
    [InlineData("projectdirector")]
    [InlineData("admin")]
    public async Task Reading_Cash_Flow_Is_Allowed_For_Financially_Trusted_Roles(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/cash-flow");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_Seeded_Project_With_No_Cost_Entries_Or_Closed_Periods_Returns_An_Honest_Zero_Live_Period_And_Unavailable_Receipts()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(pm.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/cash-flow");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cashFlow = await response.Content.ReadFromJsonAsync<CashFlowResponse>(ResponseJsonOptions);

        Assert.NotNull(cashFlow);
        Assert.Equal(projectId, cashFlow!.ProjectId);
        Assert.Equal(0.00m, cashFlow.PvCumulative);
        Assert.Equal(0.00m, cashFlow.EvCumulative);
        Assert.Equal(0.00m, cashFlow.AcCumulative);

        var period = Assert.Single(cashFlow.Periods);
        Assert.Null(period.PeriodStart);
        Assert.False(period.IsClosed);
        Assert.Equal(0.00m, period.AcPeriod);

        Assert.False(cashFlow.Receipts.IsAvailable);
        Assert.Null(cashFlow.Receipts.Cumulative);
        Assert.Empty(cashFlow.Receipts.Periods);
        Assert.Equal("PaymentCertificatesNotYetImplemented", cashFlow.Receipts.UnavailableReason);
        Assert.Null(cashFlow.NetCashPosition);
    }

    [Fact]
    public async Task An_Inverted_From_Range_Returns_400_With_The_Specific_Problem_Type()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(pm.TenantId);

        var futureFrom = DateTimeOffset.UtcNow.AddYears(50).ToString("O");
        var response = await client.GetAsync($"/api/v1/projects/{projectId}/cash-flow?from={Uri.EscapeDataString(futureFrom)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/cash-flow-invalid-range", problem!.Type);
    }
}
