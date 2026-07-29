using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S3-BE-04 end-to-end over real HTTP: <c>POST /api/v1/projects/{id}/import/{format}</c> +
/// job history, through the real WebApi host (auth, routing, ProblemDetails, MediatR pipeline) -
/// not just the Application/Infrastructure layers unit-tested elsewhere in
/// <c>CMPlus.Integration.Tests.Parsers</c>.
/// </summary>
public class ImportControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private readonly CustomWebApplicationFactory _factory;

    public ImportControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    private async Task<LoginResponse> LoginAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = DevDataSeeder.DevSeedPassword }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    /// <summary>Resolves the seeded project id for a tenant directly against the running host's
    /// DbContext, bypassing the (request-scoped) tenant query filter via IgnoreQueryFilters - white-
    /// box test setup, not something the import pipeline itself does.</summary>
    private async Task<Guid> GetSeededProjectIdAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        var project = await dbContext.Projects.IgnoreQueryFilters().SingleAsync(p => p.TenantId == tenantId);
        return project.Id;
    }

    [Fact]
    public async Task An_Unauthenticated_Import_Request_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "schedule.xer" },
        };

        var response = await client.PostAsync($"/api/v1/projects/{Guid.NewGuid()}/import/xer", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Importing_A_Xer_File_For_An_Unknown_Project_Returns_404()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);

        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "schedule.xer" },
        };

        var response = await client.PostAsync($"/api/v1/projects/{Guid.NewGuid()}/import/xer", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_Unsupported_Format_Segment_Is_Rejected_With_400()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(pm.TenantId);

        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "schedule.txt" },
        };

        var response = await client.PostAsync($"/api/v1/projects/{projectId}/import/csv", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Importing_The_Golden_Xer_File_Over_Http_Succeeds_And_Then_Appears_In_Job_History()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(pm.TenantId);

        var xerBytes = await File.ReadAllBytesAsync(ResolveGoldenFixturePath());

        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(xerBytes), "file", "sample-schedule.xer" },
        };

        var importResponse = await client.PostAsync($"/api/v1/projects/{projectId}/import/xer", content);

        Assert.Equal(HttpStatusCode.Created, importResponse.StatusCode);
        var job = await importResponse.Content.ReadFromJsonAsync<JobResponse>();
        Assert.NotNull(job);
        Assert.Equal("Succeeded", job!.Status);
        Assert.Equal(7, job.RowsImported);

        var historyResponse = await client.GetAsync($"/api/v1/projects/{projectId}/import/jobs");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadFromJsonAsync<List<JobResponse>>();
        Assert.Contains(history!, j => j.Id == job.Id);

        var detailResponse = await client.GetAsync($"/api/v1/projects/{projectId}/import/jobs/{job.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
    }

    private static string ResolveGoldenFixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CMPlus.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "tests", "fixtures", "goldenfiles", "xer", "sample-schedule.xer");
    }

    // ------------------------------------------------------------------------------------
    // S3-SEC-01 DoD item 2 (magic-byte / content-type check) - end to end over real HTTP: an
    // XLSX-shaped upload posted to the xer route never reaches the parser, and is a modelled
    // Failed job (201 Created), never a 500.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Xlsx_Shaped_Upload_Posted_To_The_Xer_Route_Is_A_Failed_Job_Not_A_500()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(pm.TenantId);

        byte[] xlsxMagicBytes = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00];
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(xlsxMagicBytes), "file", "disguised.xer" },
        };

        var response = await client.PostAsync($"/api/v1/projects/{projectId}/import/xer", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<JobResponse>();
        Assert.NotNull(job);
        Assert.Equal("Failed", job!.Status);
        Assert.Contains("ImportFormatMismatch", job.ErrorJson);
    }

    // ------------------------------------------------------------------------------------
    // S3-SEC-01 M-04: role gate on the import routes. DevDataSeeder seeds one user per UserRole
    // per tenant, so every role can be exercised by logging in as "<role>@siam-construction.dev".
    // ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("site")]
    [InlineData("qs")]
    [InlineData("executive")]
    [InlineData("projectdirector")]
    public async Task A_Role_Excluded_From_Schedule_Import_Gets_403_On_The_Xer_Route(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "schedule.xer" },
        };

        var response = await client.PostAsync($"/api/v1/projects/{projectId}/import/xer", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("site")]
    [InlineData("qs")]
    [InlineData("executive")]
    [InlineData("projectdirector")]
    public async Task A_Role_Excluded_From_Schedule_Import_Gets_403_On_The_Mspdi_Route(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "schedule.xml" },
        };

        var response = await client.PostAsync($"/api/v1/projects/{projectId}/import/mspdi", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("executive")]
    [InlineData("projectdirector")]
    public async Task A_Role_Excluded_From_Progress_Import_Gets_403_On_The_Xlsx_Route(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "progress.xlsx" },
        };

        var response = await client.PostAsync($"/api/v1/projects/{projectId}/import/xlsx", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("pm")]
    [InlineData("planning")]
    [InlineData("admin")]
    public async Task A_Role_Allowed_For_Schedule_Import_Does_Not_Get_403_On_The_Xer_Route(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "schedule.xer" },
        };

        var response = await client.PostAsync($"/api/v1/projects/{projectId}/import/xer", content);

        // Not the RBAC gate under test here - [1,2,3] isn't valid XER content either, so this is
        // expected to still be rejected, just never with 403 (a modelled Failed job, 201 Created).
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("site")]
    [InlineData("qs")]
    public async Task A_Site_Or_Qs_Role_Does_Not_Get_403_On_The_Xlsx_Route(string roleEmailPrefix)
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, $"{roleEmailPrefix}@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "progress.xlsx" },
        };

        var response = await client.PostAsync($"/api/v1/projects/{projectId}/import/xlsx", content);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record JobResponse(Guid Id, Guid ProjectId, string FileName, string Format, string Status, int RowsImported, string? ErrorJson);
}
