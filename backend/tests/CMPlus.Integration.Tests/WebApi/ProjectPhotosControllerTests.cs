using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S12-BE-01 (US-12.1) end-to-end over real HTTP: <c>POST/GET /api/v1/projects/{id}/photos</c>
/// through the real WebApi host (auth, RBAC, routing, ProblemDetails, MediatR pipeline, and the
/// REAL <c>LocalDiskFileStorage</c> adapter writing to a throwaway disk directory - not a fake, see
/// <c>CustomWebApplicationFactory.PhotoStorageRootPath</c>). Several tests here read the file
/// actually written to disk directly, bypassing the API entirely, to prove EXIF stripping against
/// what is truly stored - not merely against what the serve endpoint chooses to return.
/// </summary>
public class ProjectPhotosControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record PhotoResponse(
        Guid Id, Guid ProjectId, Guid? ActivityId, string? Caption, string ContentType,
        long FileSizeBytes, Guid UploadedByUserId, DateTimeOffset UploadedAt, DateTimeOffset CapturedAt);

    private readonly CustomWebApplicationFactory _factory;

    public ProjectPhotosControllerTests(CustomWebApplicationFactory factory)
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

    private async Task<Guid> CreateActivityAsync(Guid tenantId, Guid projectId)
    {
        await using var context = _factory.CreateDbContextForSeeding(tenantId);
        var node = new WBSNode(tenantId, projectId, "Z-B", "โซน B", 10m);
        context.WBSNodes.Add(node);
        var activity = new Activity(
            tenantId, node.Id, "A-100", "เทคอนกรีตพื้น",
            DateTimeOffset.Parse("2026-08-01T00:00:00+07:00"), DateTimeOffset.Parse("2026-08-05T00:00:00+07:00"), 5, 100_000m);
        context.Activities.Add(activity);
        await context.SaveChangesAsync();
        return activity.Id;
    }

    private static MultipartFormDataContent BuildUploadContent(
        byte[] fileBytes, string fileName = "site.jpg", string contentType = "image/jpeg",
        Guid? activityId = null, string? caption = null)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(fileContent, "file", fileName);

        if (activityId is { } id)
        {
            content.Add(new StringContent(id.ToString()), "activityId");
        }

        if (caption is not null)
        {
            content.Add(new StringContent(caption), "caption");
        }

        return content;
    }

    // ------------------------------------------------------------------------------------
    // RBAC: Site takes photos in practice (this task's brief). Write is narrower than read - a
    // photo is project documentation, not commercially sensitive (mirrors ProjectWeatherLogsController).
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Unauthenticated_Upload_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();
        using var content = BuildUploadContent([0xFF, 0xD8, 0xFF, 0xD9]);

        var response = await client.PostAsync($"/api/v1/projects/{Guid.NewGuid()}/photos", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_Unauthenticated_Get_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/photos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_Site_User_Can_Upload_A_Photo_The_Role_This_Task_Was_Told_Must_Be_Able_To()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        using var content = BuildUploadContent(PhotoImageBytes.PlainJpeg(), caption: "หล่อเสา C4");
        var response = await client.PostAsync($"/api/v1/projects/{projectId}/photos", content);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected 201, got {response.StatusCode}: {body}");
        var photo = await response.Content.ReadFromJsonAsync<PhotoResponse>();
        Assert.NotNull(photo);
        Assert.Equal("image/jpeg", photo!.ContentType);
        Assert.Equal(site.UserId, photo.UploadedByUserId);
        Assert.Equal("หล่อเสา C4", photo.Caption);
    }

    [Fact]
    public async Task An_Executive_Is_Forbidden_From_Uploading_A_Photo()
    {
        using var client = _factory.CreateClient();
        var executive = await LoginAsync(client, "executive@siam-construction.dev");
        Authorize(client, executive.AccessToken);
        var projectId = await GetSeededProjectIdAsync(executive.TenantId);

        using var content = BuildUploadContent(PhotoImageBytes.PlainJpeg());
        var response = await client.PostAsync($"/api/v1/projects/{projectId}/photos", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_Executive_Can_Still_Fetch_A_Photo_Even_Though_They_Cannot_Upload_It()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        using var uploadContent = BuildUploadContent(PhotoImageBytes.PlainJpeg());
        var uploadResponse = await client.PostAsync($"/api/v1/projects/{projectId}/photos", uploadContent);
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<PhotoResponse>())!;

        var executive = await LoginAsync(client, "executive@siam-construction.dev");
        Authorize(client, executive.AccessToken);
        var getResponse = await client.GetAsync($"/api/v1/projects/{projectId}/photos/{uploaded.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("image/jpeg", getResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_Successful_Upload_Writes_Exactly_One_Created_AuditLog_Row_For_The_Photo()
    {
        // CLAUDE.md: "Every mutating domain operation writes an audit log entry" - proven directly
        // against the real AuditSaveChangesInterceptor wired into the real host, not assumed from
        // "every other entity uses the same interceptor".
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        using var content = BuildUploadContent(PhotoImageBytes.PlainJpeg());
        var uploaded = (await (await client.PostAsync($"/api/v1/projects/{projectId}/photos", content))
            .Content.ReadFromJsonAsync<PhotoResponse>())!;

        // AuditLog is itself ITenantOwned, and this verification runs outside any authenticated
        // HTTP request (no ambient ITenantProvider to satisfy the global query filter) - IgnoreQueryFilters
        // here is test-setup/verification only, the same established pattern GetSeededProjectIdAsync
        // above already uses, never something the production audit-read path does.
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        var auditRows = await dbContext.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.EntityName == nameof(Photo) && a.EntityId == uploaded.Id)
            .ToListAsync();

        var row = Assert.Single(auditRows);
        Assert.Equal(AuditAction.Created, row.Action);
        Assert.Equal(site.UserId, row.UserId);
    }

    [Fact]
    public async Task Uploading_To_An_Unknown_Project_Returns_404()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);

        using var content = BuildUploadContent(PhotoImageBytes.PlainJpeg());
        var response = await client.PostAsync($"/api/v1/projects/{Guid.NewGuid()}/photos", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_With_An_Unresolvable_ActivityId_Returns_400()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        using var content = BuildUploadContent(PhotoImageBytes.PlainJpeg(), activityId: Guid.NewGuid());
        var response = await client.PostAsync($"/api/v1/projects/{projectId}/photos", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_Tagged_To_A_Real_Activity_Persists_The_Tag()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);
        var activityId = await CreateActivityAsync(site.TenantId, projectId);

        using var content = BuildUploadContent(PhotoImageBytes.PlainJpeg(), activityId: activityId);
        var response = await client.PostAsync($"/api/v1/projects/{projectId}/photos", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var photo = await response.Content.ReadFromJsonAsync<PhotoResponse>();
        Assert.Equal(activityId, photo!.ActivityId);
    }

    // ------------------------------------------------------------------------------------
    // Cross-tenant isolation: an id belonging to another tenant, or a real id from a different
    // project in the SAME tenant, is indistinguishable from an id that does not exist at all - both
    // a bare 404 (S12-SEC-01: "photo URLs must not be guessable in a way that permits cross-tenant
    // access").
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Fetching_Another_Tenants_Photo_By_Id_Returns_404_Not_A_Cross_Tenant_Read()
    {
        using var client = _factory.CreateClient();
        var siamSite = await LoginAsync(client, "site@siam-construction.dev");
        var bkkSite = await LoginAsync(client, "site@bkk-infra.dev");

        Authorize(client, siamSite.AccessToken);
        var siamProjectId = await GetSeededProjectIdAsync(siamSite.TenantId);
        using var uploadContent = BuildUploadContent(PhotoImageBytes.PlainJpeg());
        var uploaded = (await (await client.PostAsync($"/api/v1/projects/{siamProjectId}/photos", uploadContent))
            .Content.ReadFromJsonAsync<PhotoResponse>())!;

        // The bkk tenant user, even guessing the exact photo id AND the exact (wrong-tenant)
        // project id correctly, gets the identical bare 404 an unknown id would.
        Authorize(client, bkkSite.AccessToken);
        var response = await client.GetAsync($"/api/v1/projects/{siamProjectId}/photos/{uploaded.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Fetching_A_Real_Photo_Through_A_Different_Same_Tenant_Project_Route_Returns_404()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        using var uploadContent = BuildUploadContent(PhotoImageBytes.PlainJpeg());
        var uploaded = (await (await client.PostAsync($"/api/v1/projects/{projectId}/photos", uploadContent))
            .Content.ReadFromJsonAsync<PhotoResponse>())!;

        // A real, same-tenant, different-project id in the route - must not be treated as
        // authorization to read a photo scoped to a project this route never named.
        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/photos/{uploaded.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Fetching_An_Unknown_PhotoId_Returns_The_Identical_404()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/photos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------------------------
    // Mutation-evidence case: a file whose magic bytes disagree with its extension/Content-Type -
    // over real HTTP, with a real multipart Content-Type header claiming image/jpeg.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Html_Payload_Disguised_As_A_Jpeg_With_A_Matching_Filename_And_Content_Type_Is_Rejected_With_400()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var htmlBytes = System.Text.Encoding.UTF8.GetBytes("<html><body><script>alert(document.cookie)</script></body></html>");
        using var content = BuildUploadContent(htmlBytes, fileName: "totally-a-photo.jpg", contentType: "image/jpeg");

        var response = await client.PostAsync($"/api/v1/projects/{projectId}/photos", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("photo-unsupported-format", problem!.RootElement.GetProperty("type").GetString()!.Split('/').Last());
    }

    [Fact]
    public async Task Uploading_An_Oversized_File_Over_Real_Http_Is_Rejected_With_400()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        // Comfortably over the 10 MiB default Photos:MaxFileSizeBytes.
        var oversized = new byte[11 * 1024 * 1024];
        PhotoImageBytes.PlainJpeg().CopyTo(oversized, 0);
        using var content = BuildUploadContent(oversized);

        var response = await client.PostAsync($"/api/v1/projects/{projectId}/photos", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("photo-file-too-large", problem!.RootElement.GetProperty("type").GetString()!.Split('/').Last());
    }

    // ------------------------------------------------------------------------------------
    // Mutation-evidence case (this task's brief, verbatim): "EXIF GPS is genuinely gone from what
    // was stored - assert on the stored bytes, not on the code path." This reads the REAL file
    // LocalDiskFileStorage actually wrote to a throwaway disk directory, entirely independent of the
    // GET endpoint's own response.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_File_Actually_Written_To_Disk_Contains_No_Gps_Or_Device_Exif_Even_Though_The_Upload_Did()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var original = PhotoImageBytes.JpegWithGpsExifAndDeviceInfo(
            out var gpsMarker, out var deviceMarker, out var commentMarker);

        using var content = BuildUploadContent(original);
        var uploadResponse = await client.PostAsync($"/api/v1/projects/{projectId}/photos", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<PhotoResponse>())!;

        // Read the literal file on disk - never through IFileStorage, never through the API, so
        // there is no code path left that could be faking this result.
        var diskFiles = Directory.GetFiles(_factory.PhotoStorageRootPath, $"{uploaded.Id:N}*", SearchOption.AllDirectories);
        var storedFile = Assert.Single(diskFiles);
        var storedBytes = await File.ReadAllBytesAsync(storedFile);
        var storedText = System.Text.Encoding.ASCII.GetString(storedBytes);

        Assert.DoesNotContain(gpsMarker, storedText, StringComparison.Ordinal);
        Assert.DoesNotContain(deviceMarker, storedText, StringComparison.Ordinal);
        Assert.DoesNotContain(commentMarker, storedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Exif", storedText, StringComparison.Ordinal);
        Assert.True(storedBytes.Length < original.Length, "Stored file should be smaller than the original once metadata is stripped.");

        // And the served bytes match the stored (scrubbed) bytes exactly - the served copy is not a
        // second, independently-scrubbed path that could silently diverge from what is on disk.
        var getResponse = await client.GetAsync($"/api/v1/projects/{projectId}/photos/{uploaded.Id}");
        var servedBytes = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(storedBytes, servedBytes);

        // Content-Disposition forces a download rather than inline rendering (stored-XSS defence)
        // and the nosniff header is present.
        Assert.Equal("attachment", getResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("nosniff", getResponse.Headers.GetValues("X-Content-Type-Options"));
    }

    // ------------------------------------------------------------------------------------
    // H-01 regression (security review sprint-12.md): the pre-fix ExifScrubber only ever stripped
    // metadata BEFORE the first SOS - the test above put every marker there and so passed while the
    // bug was live ("the existing tests all passed precisely because they asserted the wrong
    // thing"). These place GPS-carrying EXIF in the region the pre-fix code copied to storage
    // verbatim, and - critically - read the literal bytes LocalDiskFileStorage wrote to disk, never
    // Strip's return value and never the GET endpoint's response.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_File_Actually_Written_To_Disk_Has_No_Gps_Exif_That_Was_Placed_After_The_Sos()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var original = PhotoImageBytes.JpegWithGpsExifAfterSos(out var gpsMarker);

        using var content = BuildUploadContent(original);
        var uploadResponse = await client.PostAsync($"/api/v1/projects/{projectId}/photos", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<PhotoResponse>())!;

        // Read the literal file on disk - never through IFileStorage, never through the API.
        var diskFiles = Directory.GetFiles(_factory.PhotoStorageRootPath, $"{uploaded.Id:N}*", SearchOption.AllDirectories);
        var storedFile = Assert.Single(diskFiles);
        var storedBytes = await File.ReadAllBytesAsync(storedFile);
        var storedText = System.Text.Encoding.ASCII.GetString(storedBytes);

        Assert.DoesNotContain(gpsMarker, storedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Exif", storedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_File_Actually_Written_To_Disk_Discards_A_Second_Jpeg_With_Gps_Appended_After_Eoi()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);
        var projectId = await GetSeededProjectIdAsync(site.TenantId);

        var original = PhotoImageBytes.JpegWithSecondJpegAfterEoi(out var gpsMarker);

        using var content = BuildUploadContent(original);
        var uploadResponse = await client.PostAsync($"/api/v1/projects/{projectId}/photos", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<PhotoResponse>())!;

        var diskFiles = Directory.GetFiles(_factory.PhotoStorageRootPath, $"{uploaded.Id:N}*", SearchOption.AllDirectories);
        var storedFile = Assert.Single(diskFiles);
        var storedBytes = await File.ReadAllBytesAsync(storedFile);
        var storedText = System.Text.Encoding.ASCII.GetString(storedBytes);

        Assert.DoesNotContain(gpsMarker, storedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Exif", storedText, StringComparison.Ordinal);
        // The stored file ends at the primary image's own EOI - the secondary image is gone entirely.
        Assert.Equal(0xFF, storedBytes[^2]);
        Assert.Equal(0xD9, storedBytes[^1]);
    }
}
