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
/// S11-BE-01 (US-11.1) end-to-end over real HTTP: <c>GET/POST /api/v1/projects/{id}/weather-logs</c>
/// and <c>POST .../weather-logs/{logId}/corrections</c> through the real WebApi host (auth, RBAC,
/// routing, ProblemDetails, MediatR pipeline, DI wiring for the real
/// <c>IDailyWeatherLogRepository</c>) - shaped by domain-rules.md weather-eot §8, which landed
/// mid-implementation.
/// </summary>
public class ProjectWeatherLogsControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record WeatherLogResponse(
        Guid Id, Guid ProjectId, DateTimeOffset LogDate, string Condition, string? ConditionNote,
        decimal? RainfallMm, string Impact, string? ImpactNote, decimal? HoursLost, bool WorkStoppage,
        string EntryKind, Guid? CorrectsWeatherLogId, string? CorrectionReason, List<Guid> AffectedActivityIds,
        Guid RecordedByUserId, DateTimeOffset RecordedAt);

    // design.md §2: every decimal is wire-serialized as a JSON string (DecimalAsStringJsonConverter).
    // Enums serialize as strings too (JsonStringEnumConverter, Program.cs) - both are configured on
    // the real WebApi host's MVC JSON options, unlike AuditSaveChangesInterceptor's raw internal
    // serialization call.
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new DecimalAsStringJsonConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;

    public ProjectWeatherLogsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    private async Task<LoginResponse> LoginAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = DevDataSeeder.DevSeedPassword }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static object DefaultWeatherPayload(IEnumerable<Guid>? affectedActivityIds = null) => new
    {
        LogDate = DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"),
        Condition = "HeavyRain",
        ConditionNote = "ฝนตกหนัก",
        RainfallMm = 42.5m,
        Impact = "FullStoppage",
        ImpactNote = "หยุดเทคอนกรีตโซน B ครึ่งวัน",
        HoursLost = 8.00m,
        AffectedActivityIds = affectedActivityIds ?? [],
    };

    private static object CorrectionPayload(
        string entryKind, string correctionReason, DateTimeOffset logDate, decimal? rainfallMm, decimal? hoursLost) => new
    {
        EntryKind = entryKind,
        CorrectionReason = correctionReason,
        LogDate = logDate,
        Condition = "HeavyRain",
        ConditionNote = (string?)null,
        RainfallMm = rainfallMm,
        Impact = hoursLost is null ? "NoImpact" : "PartialStoppage",
        ImpactNote = (string?)null,
        HoursLost = hoursLost,
        AffectedActivityIds = Array.Empty<Guid>(),
    };

    private async Task<Guid> GetSeededProjectIdAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        var project = await dbContext.Projects.IgnoreQueryFilters().SingleAsync(p => p.TenantId == tenantId);
        return project.Id;
    }

    // ------------------------------------------------------------------------------------
    // RBAC: Site records field weather in practice (this task's brief). Write is narrower than
    // read - weather data is not commercially sensitive, so read is open to any authenticated role.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Unauthenticated_Post_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/weather-logs", DefaultWeatherPayload());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_Unauthenticated_Get_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/weather-logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_Site_User_Can_Record_A_Weather_Log_The_Role_This_Task_Was_Told_Must_Be_Able_To()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/weather-logs", DefaultWeatherPayload());

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected 201, got {response.StatusCode}: {body}");
        var posted = await response.Content.ReadFromJsonAsync<WeatherLogResponse>(ResponseJsonOptions);
        Assert.NotNull(posted);
        Assert.Equal(42.5m, posted!.RainfallMm);
        Assert.True(posted.WorkStoppage);
        Assert.Equal("Original", posted.EntryKind);
        Assert.Equal(site.UserId, posted.RecordedByUserId);
    }

    [Fact]
    public async Task An_Executive_Is_Forbidden_From_Posting_A_Weather_Log()
    {
        // Executive is an oversight-only role in this codebase (mirrors ActualCostsController's own
        // precedent) - not one of ProjectWeatherLogsController.WriteRoles.
        using var client = _factory.CreateClient();
        var executive = await LoginAsync(client, "executive@siam-construction.dev");
        Authorize(client, executive.AccessToken);
        var projectId = await GetSeededProjectIdAsync(executive.TenantId);

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/weather-logs", DefaultWeatherPayload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_Executive_Can_Still_Read_The_Weather_Log_Even_Though_They_Cannot_Write_It()
    {
        // The deliberate write-narrower-than-read divergence this task's brief asked for reasoning
        // about - proven from both directions in this file.
        using var client = _factory.CreateClient();
        var executive = await LoginAsync(client, "executive@siam-construction.dev");
        Authorize(client, executive.AccessToken);
        var projectId = await GetSeededProjectIdAsync(executive.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/weather-logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Posting_To_An_Unknown_Project_Returns_404()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{Guid.NewGuid()}/weather-logs", DefaultWeatherPayload());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------------------------
    // Cross-tenant isolation: an id belonging to another tenant is indistinguishable from an id
    // that does not exist at all - both a bare 404 (this task's brief; ADR-0002).
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Posting_A_Correction_Against_Another_Tenants_Project_Returns_404_Not_A_Cross_Tenant_Write()
    {
        using var client = _factory.CreateClient();
        var siamSite = await LoginAsync(client, "site@siam-construction.dev");
        var bkkSite = await LoginAsync(client, "site@bkk-infra.dev");
        var bkkProjectId = await GetSeededProjectIdAsync(bkkSite.TenantId);

        Authorize(client, siamSite.AccessToken);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{bkkProjectId}/weather-logs/{Guid.NewGuid()}/corrections",
            CorrectionPayload("Correction", "reason", DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"), 42.5m, 8.00m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------------------------
    // domain-rules.md §8.3: there is no PUT/PATCH/DELETE - each is a deliberate 405.
    // ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Attempting_To_Update_Or_Delete_A_Weather_Log_Is_Rejected_With_A_Deliberate_405(string method)
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/v1/projects/{projectId}/weather-logs/{Guid.NewGuid()}");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("weather-log-is-immutable", problem!.RootElement.GetProperty("type").GetString()!.Split('/').Last());
    }

    // ------------------------------------------------------------------------------------
    // The functional core: immutable append, corrections, and the list read.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Correction_Is_A_New_Entry_Referencing_The_Original_Both_Remain_In_The_List()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var originalResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/weather-logs", DefaultWeatherPayload());
        Assert.Equal(HttpStatusCode.Created, originalResponse.StatusCode);
        var original = (await originalResponse.Content.ReadFromJsonAsync<WeatherLogResponse>(ResponseJsonOptions))!;

        var correctionResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/weather-logs/{original.Id}/corrections",
            CorrectionPayload("Correction", "ตรวจใบบันทึกกะแล้ว หยุดจริงทั้งวัน", original.LogDate, 61.00m, 8.00m));
        Assert.Equal(HttpStatusCode.Created, correctionResponse.StatusCode);
        var correction = (await correctionResponse.Content.ReadFromJsonAsync<WeatherLogResponse>(ResponseJsonOptions))!;
        Assert.Equal(original.Id, correction.CorrectsWeatherLogId);
        Assert.Equal("Correction", correction.EntryKind);

        var listResponse = await client.GetAsync($"/api/v1/projects/{projectId}/weather-logs");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<WeatherLogResponse>>(ResponseJsonOptions);

        Assert.NotNull(list);
        Assert.Contains(list!, w => w.Id == original.Id && w.CorrectsWeatherLogId is null && w.RainfallMm == 42.5m);
        Assert.Contains(list, w => w.Id == correction.Id && w.CorrectsWeatherLogId == original.Id && w.RainfallMm == 61.00m);
    }

    [Fact]
    public async Task A_Second_Correction_Against_An_Already_Superseded_Entry_Is_Rejected_With_409()
    {
        // domain-rules.md §8.2 rule 2, the load-bearing one: a correction must target the current
        // chain tail, never an entry someone already corrected.
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var original = (await (await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/weather-logs", DefaultWeatherPayload()))
            .Content.ReadFromJsonAsync<WeatherLogResponse>(ResponseJsonOptions))!;
        var firstCorrectionResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/weather-logs/{original.Id}/corrections",
            CorrectionPayload("Correction", "first correction", original.LogDate, 61.00m, 8.00m));
        Assert.Equal(HttpStatusCode.Created, firstCorrectionResponse.StatusCode);

        var secondCorrectionResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/weather-logs/{original.Id}/corrections",
            CorrectionPayload("Correction", "second correction, still targeting the original", original.LogDate, 30.00m, 4.00m));

        Assert.Equal(HttpStatusCode.Conflict, secondCorrectionResponse.StatusCode);
    }

    [Fact]
    public async Task Recording_With_An_Unresolvable_Activity_Id_Is_Rejected_With_400()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/weather-logs",
            DefaultWeatherPayload(affectedActivityIds: [Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
