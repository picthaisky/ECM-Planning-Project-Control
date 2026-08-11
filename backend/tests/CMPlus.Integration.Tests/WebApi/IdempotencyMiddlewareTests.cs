using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Seed;
using CMPlus.WebApi.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S13-BE-01 (US-13.1, ADR-0005) end-to-end over real HTTP: <see cref="IdempotencyMiddleware"/>
/// wrapping the real <c>POST /api/v1/projects/{id}/photos</c> (multipart) and
/// <c>POST /api/v1/projects/{id}/weather-logs</c> (JSON) endpoints through the real WebApi host -
/// auth, RBAC, routing, the real <c>EfIdempotencyStore</c>/<c>IdempotencyKeyLock</c>, the real
/// <c>AuditSaveChangesInterceptor</c>. Closes security review sprint-12.md M-01: every test here
/// asserts on rows actually persisted in <see cref="CmPlusDbContext"/>, not merely on HTTP response
/// shape, mirroring <c>ProjectPhotosControllerTests</c>' own "prove it against the database" style.
///
/// <para><b>Row-counting note.</b> <see cref="CustomWebApplicationFactory"/> is an
/// <c>IClassFixture</c> - one shared instance (and one shared InMemory database/seeded project) for
/// every test method in this class, same as <c>ProjectPhotosControllerTests</c>. A blanket
/// "how many Photos does this project have" count would therefore also count every other test
/// method's photos. Every marker-based helper below instead embeds a fresh <see cref="Guid"/> into
/// the free-text field (<c>Caption</c>/<c>ConditionNote</c>) and counts only rows carrying that exact
/// per-test marker - real production captions are free text too, so this is the correlation
/// mechanism, not a special test-only field.</para>
/// </summary>
public class IdempotencyMiddlewareTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record PhotoResponse(Guid Id, Guid ProjectId, string? Caption, string ContentType);

    private readonly CustomWebApplicationFactory _factory;

    public IdempotencyMiddlewareTests(CustomWebApplicationFactory factory)
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

    private static MultipartFormDataContent BuildUploadContent(string caption, byte[]? fileBytes = null)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes ?? PhotoImageBytes.PlainJpeg());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        content.Add(fileContent, "file", "site.jpg");
        content.Add(new StringContent(caption), "caption");
        return content;
    }

    private static HttpRequestMessage BuildUploadRequest(string projectId, string idempotencyKey, string caption)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{projectId}/photos")
        {
            Content = BuildUploadContent(caption),
        };
        request.Headers.Add(IdempotencyMiddleware.HeaderName, idempotencyKey);
        return request;
    }

    /// <summary>Counts only Photos whose Caption carries this test's own unique marker - see this
    /// type's class remarks.</summary>
    private async Task<int> CountPhotosWithMarkerAsync(string marker)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        return await dbContext.Photos.IgnoreQueryFilters().CountAsync(p => p.Caption != null && p.Caption.Contains(marker));
    }

    private async Task<int> CountIdempotencyKeysAsync(string key)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        return await dbContext.IdempotencyKeys.IgnoreQueryFilters().CountAsync(k => k.Key == key);
    }

    // ------------------------------------------------------------------------------------
    // DoD: "ส่งซ้ำด้วย key เดิม -> คืน response เดิม ไม่สร้างข้อมูลซ้ำ" - replaying the same key
    // returns the ORIGINAL response and creates exactly one row. This is the concrete regression
    // test for security review sprint-12.md M-01's proven defect (a duplicate Photo row from a
    // lost-response outbox replay).
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Replaying_The_Same_Idempotency_Key_On_Photo_Upload_Returns_The_Original_Response_And_Creates_Exactly_One_Row()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);
        var key = $"replay-{Guid.NewGuid()}";
        var caption = $"marker:{key} หล่อเสา C4";

        var firstResponse = await client.SendAsync(BuildUploadRequest(projectId.ToString(), key, caption));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal("application/json", firstResponse.Content.Headers.ContentType?.MediaType);
        var first = (await firstResponse.Content.ReadFromJsonAsync<PhotoResponse>())!;

        // A genuine second HTTP request - not a retried HttpRequestMessage (those cannot be reused) -
        // simulating exactly outboxStore.reconcileInterruptedSyncs replaying a lost-response upload.
        var secondResponse = await client.SendAsync(BuildUploadRequest(projectId.ToString(), key, caption));

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.Equal("application/json", secondResponse.Content.Headers.ContentType?.MediaType);
        var second = (await secondResponse.Content.ReadFromJsonAsync<PhotoResponse>())!;
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Caption, second.Caption);

        Assert.Equal(1, await CountPhotosWithMarkerAsync(key));
        Assert.Equal(1, await CountIdempotencyKeysAsync(key));
    }

    [Fact]
    public async Task The_IdempotencyKey_Reservation_Itself_Is_Audited_Like_Any_Other_Mutation()
    {
        // CLAUDE.md/conventions.md: "every mutating domain operation writes an audit log entry".
        // IdempotencyMiddleware.cs's class remarks claim Reserve (Add) and Complete (Modified) are
        // NOT specially suppressed from AuditSaveChangesInterceptor - proven here directly against
        // the real interceptor, not merely asserted in a comment.
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);
        var key = $"idem-audit-{Guid.NewGuid()}";

        await client.SendAsync(BuildUploadRequest(projectId.ToString(), key, $"marker:{key}"));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        var idempotencyKeyRecord = await dbContext.IdempotencyKeys.IgnoreQueryFilters().SingleAsync(k => k.Key == key);
        var auditRows = await dbContext.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityName == nameof(IdempotencyKey) && a.EntityId == idempotencyKeyRecord.Id)
            .OrderBy(a => a.Timestamp)
            .ToListAsync();

        Assert.Equal(2, auditRows.Count); // Reserve (Created) then Complete (Updated).
        Assert.Equal(AuditAction.Created, auditRows[0].Action);
        Assert.Equal(AuditAction.Updated, auditRows[1].Action);
        Assert.All(auditRows, row => Assert.Equal(site.UserId, row.UserId));
    }

    [Fact]
    public async Task Replaying_The_Same_Idempotency_Key_Writes_No_Second_AuditLog_Row_For_The_Photo()
    {
        // Standing requirement: "A replay must not re-run side effects - no second audit row."
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);
        var key = $"replay-audit-{Guid.NewGuid()}";
        var caption = $"marker:{key}";

        var first = (await (await client.SendAsync(BuildUploadRequest(projectId.ToString(), key, caption)))
            .Content.ReadFromJsonAsync<PhotoResponse>())!;
        await client.SendAsync(BuildUploadRequest(projectId.ToString(), key, caption));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        var auditRows = await dbContext.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityName == nameof(Photo) && a.EntityId == first.Id)
            .ToListAsync();

        Assert.Single(auditRows);
    }

    // ------------------------------------------------------------------------------------
    // DoD: "key เดิมกับ payload ต่างกัน -> 409".
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Same_Idempotency_Key_With_A_Different_Caption_Returns_409_And_Creates_No_Second_Row()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);
        var key = $"mismatch-{Guid.NewGuid()}";

        var firstResponse = await client.SendAsync(BuildUploadRequest(projectId.ToString(), key, $"marker:{key} แคปชั่นแรก"));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await client.SendAsync(
            BuildUploadRequest(projectId.ToString(), key, $"marker:{key} แคปชั่นที่สอง - ไม่เหมือนเดิม"));

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var problem = await secondResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        Assert.Equal("idempotency-payload-mismatch", problem!.RootElement.GetProperty("type").GetString()!.Split('/').Last());

        Assert.Equal(1, await CountPhotosWithMarkerAsync(key));
    }

    // ------------------------------------------------------------------------------------
    // Testing requirement: "two concurrent same-key requests -> one execution".
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_Concurrent_Requests_With_The_Same_Idempotency_Key_Result_In_Exactly_One_Photo_Row()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);
        var key = $"concurrent-{Guid.NewGuid()}";
        var caption = $"marker:{key} พร้อมกัน";

        var task1 = client.SendAsync(BuildUploadRequest(projectId.ToString(), key, caption));
        var task2 = client.SendAsync(BuildUploadRequest(projectId.ToString(), key, caption));
        var responses = await Task.WhenAll(task1, task2);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        // Exactly one side effect happened, regardless of which of the two requests "won" - the
        // loser either 409s (still in flight when it checked) or - if it happened to check after the
        // winner had already committed - replays the winner's own 201, per IdempotencyMiddleware's
        // class remarks. Both are correct; two Created responses for two DIFFERENT photo ids would
        // not be.
        Assert.Equal(1, await CountPhotosWithMarkerAsync(key));
        Assert.All(statusCodes, code => Assert.True(code is HttpStatusCode.Created or HttpStatusCode.Conflict, $"Unexpected status {code}"));
        Assert.Contains(HttpStatusCode.Created, statusCodes);

        if (statusCodes.All(c => c == HttpStatusCode.Created))
        {
            var ids = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<PhotoResponse>())!.Id));
            Assert.Equal(ids[0], ids[1]);
        }
    }

    // ------------------------------------------------------------------------------------
    // Standing requirement: "cross-tenant keys must not collide or leak".
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Same_Idempotency_Key_Used_By_Two_Different_Tenants_Is_Independent()
    {
        using var client = _factory.CreateClient();
        var siamSite = await LoginAsync(client, "site@siam-construction.dev");
        var bkkSite = await LoginAsync(client, "site@bkk-infra.dev");
        var key = $"cross-tenant-{Guid.NewGuid()}";

        Authorize(client, siamSite.AccessToken);
        var siamProjectId = await GetSeededProjectIdAsync(siamSite.TenantId);
        var siamResponse = await client.SendAsync(BuildUploadRequest(siamProjectId.ToString(), key, $"marker:{key} Siam"));

        Authorize(client, bkkSite.AccessToken);
        var bkkProjectId = await GetSeededProjectIdAsync(bkkSite.TenantId);
        var bkkResponse = await client.SendAsync(BuildUploadRequest(bkkProjectId.ToString(), key, $"marker:{key} BKK"));

        Assert.Equal(HttpStatusCode.Created, siamResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, bkkResponse.StatusCode); // Not 409 - a different tenant's use of the identical key string never collides.

        var siamPhoto = (await siamResponse.Content.ReadFromJsonAsync<PhotoResponse>())!;
        var bkkPhoto = (await bkkResponse.Content.ReadFromJsonAsync<PhotoResponse>())!;
        Assert.NotEqual(siamPhoto.Id, bkkPhoto.Id);

        Assert.Equal(2, await CountPhotosWithMarkerAsync(key)); // one per tenant.
        Assert.Equal(2, await CountIdempotencyKeysAsync(key)); // one row per tenant, same key string.
    }

    // ------------------------------------------------------------------------------------
    // Opt-in, not mandatory (DoD wording is "รองรับ" - support - not "require"), and header hygiene.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Omitting_The_Idempotency_Key_Header_Behaves_Exactly_As_Before_This_Feature()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);
        var marker = $"no-key-{Guid.NewGuid()}";

        using var first = BuildUploadContent($"marker:{marker} ไม่มี idempotency key");
        var firstResponse = await client.PostAsync($"/api/v1/projects/{projectId}/photos", first);
        using var second = BuildUploadContent($"marker:{marker} ไม่มี idempotency key");
        var secondResponse = await client.PostAsync($"/api/v1/projects/{projectId}/photos", second);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        // No key supplied at all -> each POST is its own independent upload, exactly as before this
        // middleware existed.
        Assert.Equal(2, await CountPhotosWithMarkerAsync(marker));
    }

    [Fact]
    public async Task An_Empty_Idempotency_Key_Header_Is_Rejected_With_400_Rather_Than_Silently_Ignored()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);
        var marker = $"empty-key-{Guid.NewGuid()}";

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{projectId}/photos")
        {
            Content = BuildUploadContent($"marker:{marker}"),
        };
        request.Headers.Add(IdempotencyMiddleware.HeaderName, "   ");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountPhotosWithMarkerAsync(marker));
    }

    // ------------------------------------------------------------------------------------
    // The JSON write path (weather-logs) - proves the request-hash strategy is not multipart-only.
    // ------------------------------------------------------------------------------------

    private static object WeatherPayload(string conditionNote) => new
    {
        LogDate = DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"),
        Condition = "HeavyRain",
        ConditionNote = conditionNote,
        RainfallMm = 42.5m,
        Impact = "FullStoppage",
        ImpactNote = "หยุดเทคอนกรีตโซน B ครึ่งวัน",
        HoursLost = 8.00m,
        AffectedActivityIds = Array.Empty<Guid>(),
    };

    private async Task<int> CountWeatherLogsWithMarkerAsync(string marker)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        return await dbContext.DailyWeatherLogs.IgnoreQueryFilters()
            .CountAsync(w => w.ConditionNote != null && w.ConditionNote.Contains(marker));
    }

    [Fact]
    public async Task Replaying_The_Same_Idempotency_Key_On_A_Weather_Log_Post_Returns_The_Original_Response_And_Creates_Exactly_One_Row()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);
        var key = $"weather-replay-{Guid.NewGuid()}";
        var marker = $"marker:{key}";

        HttpRequestMessage BuildRequest()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{projectId}/weather-logs")
            {
                Content = JsonContent.Create(WeatherPayload($"{marker} ฝนตกหนัก")),
            };
            request.Headers.Add(IdempotencyMiddleware.HeaderName, key);
            return request;
        }

        var firstResponse = await client.SendAsync(BuildRequest());
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();

        var secondResponse = await client.SendAsync(BuildRequest());

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        Assert.Equal(firstBody, secondBody);

        Assert.Equal(1, await CountWeatherLogsWithMarkerAsync(marker));
    }

    [Fact]
    public async Task The_Same_Idempotency_Key_With_A_Different_Weather_Payload_Returns_409()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);
        var key = $"weather-mismatch-{Guid.NewGuid()}";
        var marker = $"marker:{key}";

        var firstRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{projectId}/weather-logs")
        {
            Content = JsonContent.Create(WeatherPayload($"{marker} ฝนตกหนัก")),
        };
        firstRequest.Headers.Add(IdempotencyMiddleware.HeaderName, key);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(firstRequest)).StatusCode);

        var secondRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{projectId}/weather-logs")
        {
            Content = JsonContent.Create(WeatherPayload($"{marker} ฝนตกหนักมาก ต่างจากครั้งแรก")),
        };
        secondRequest.Headers.Add(IdempotencyMiddleware.HeaderName, key);
        var secondResponse = await client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal(1, await CountWeatherLogsWithMarkerAsync(marker));
    }
}
