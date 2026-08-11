using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Auth;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S14-BE-03 end-to-end over real HTTP: `PUT /api/v1/projects/{id}/eac-advanced-inputs` (new) and
/// `PUT /api/v1/projects/{id}/eac-default`'s new guard (extends the existing Sprint 7 endpoint).
/// A dedicated file/isolated-tenant-per-test (mirrors <c>VariationOrdersControllerTests</c>'s exact
/// rationale) rather than reusing <c>ProjectsControllerTests</c>'s shared dev-seeded project:
/// several of these tests depend on the project's EAC inputs being genuinely unconfigured, which a
/// shared project mutated by other tests in an unspecified xUnit execution order cannot guarantee.
/// </summary>
public class EacAdvancedInputsControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record EacAdvancedInputsResponse(Guid ProjectId, decimal? EacManualEtc, decimal? EacCustomPerformanceFactor);

    private readonly CustomWebApplicationFactory _factory;

    public EacAdvancedInputsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    private async Task<LoginResponse> LoginAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = DevDataSeeder.DevSeedPassword }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    /// <summary>Isolated tenant + one user per requested role, mirroring
    /// <c>VariationOrdersControllerTests.SeedIsolatedTenant</c>'s exact rationale/shape.</summary>
    private (Guid TenantId, string EmailDomain) SeedIsolatedTenant(params UserRole[] roles)
    {
        var emailDomain = $"eac-http-fixture-{Guid.NewGuid():N}.dev";
        var tenant = new Tenant($"EAC HTTP Fixture Tenant {Guid.NewGuid():N}");

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

    private Guid SeedIsolatedProject(Guid tenantId)
    {
        using var context = _factory.CreateDbContextForSeeding(tenantId);
        var project = Project.Create(
            tenantId, "EAC HTTP Fixture Project", $"EACHTTP-{Guid.NewGuid():N}", "Owner",
            DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(1), 1_000_000.00m, DateTimeOffset.UtcNow);
        context.Projects.Add(project);
        context.SaveChanges();
        return project.Id;
    }

    private (Guid TenantId, string EmailDomain, Guid ProjectId) SeedIsolatedTenantAndProject(params UserRole[] roles)
    {
        var (tenantId, emailDomain) = SeedIsolatedTenant(roles);
        var projectId = SeedIsolatedProject(tenantId);
        return (tenantId, emailDomain, projectId);
    }

    private static string EacAdvancedInputsUrl(Guid projectId) => $"/api/v1/projects/{projectId}/eac-advanced-inputs";

    private static string EacDefaultUrl(Guid projectId) => $"/api/v1/projects/{projectId}/eac-default";

    [Fact]
    public async Task An_Unauthenticated_Request_To_Set_Eac_Advanced_Inputs_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            EacAdvancedInputsUrl(Guid.NewGuid()), new { EacManualEtc = 760_000.00m, EacCustomPerformanceFactor = (decimal?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Site)]
    [InlineData(UserRole.Planning)]
    [InlineData(UserRole.Admin)]
    public async Task A_Role_Outside_PM_QS_Executive_Is_Forbidden_From_Setting_Eac_Advanced_Inputs(UserRole role)
    {
        // Same gate as PUT .../eac-default (ADR-0007(f)) - Admin is deliberately excluded here too,
        // same as that sibling endpoint.
        using var client = _factory.CreateClient();
        var (_, emailDomain, projectId) = SeedIsolatedTenantAndProject(role);
        var user = await LoginAsync(client, $"{role.ToString().ToLowerInvariant()}@{emailDomain}");
        Authorize(client, user.AccessToken);

        var response = await client.PutAsJsonAsync(
            EacAdvancedInputsUrl(projectId), new { EacManualEtc = 760_000.00m, EacCustomPerformanceFactor = (decimal?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pm_Can_Set_Both_Eac_Advanced_Inputs()
    {
        using var client = _factory.CreateClient();
        var (_, emailDomain, projectId) = SeedIsolatedTenantAndProject(UserRole.PM);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        var response = await client.PutAsJsonAsync(
            EacAdvancedInputsUrl(projectId), new { EacManualEtc = 760_000.00m, EacCustomPerformanceFactor = 1.20m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EacAdvancedInputsResponse>();
        Assert.Equal(760_000.00m, body!.EacManualEtc);
        Assert.Equal(1.20m, body.EacCustomPerformanceFactor);
    }

    /// <summary>S14-BE-03 DoD: "validate PF_c > 0" - FluentValidation's client-visible rejection
    /// (defense in depth alongside <c>Project.SetEacCustomPerformanceFactor</c>'s own Domain guard).
    /// </summary>
    [Fact]
    public async Task Setting_A_Non_Positive_EacCustomPerformanceFactor_Returns_A_Validation_Error()
    {
        using var client = _factory.CreateClient();
        var (_, emailDomain, projectId) = SeedIsolatedTenantAndProject(UserRole.PM);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        var response = await client.PutAsJsonAsync(
            EacAdvancedInputsUrl(projectId), new { EacManualEtc = (decimal?)null, EacCustomPerformanceFactor = 0m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/validation-error", problem!.Type);
    }

    /// <summary>S14-BE-03 DoD's literal case: "ตั้ง variant เป็น BottomUpEtc/CustomPf โดยไม่มีค่าที่
    /// จำเป็น -> 400". This is the <c>SetEacVariantDefaultCommand</c> direction of the guard
    /// (selecting the variant while its input is still unset) - a freshly-seeded, never-touched
    /// project, so <c>EacManualEtc</c> is genuinely null. <c>SetEacAdvancedInputsCommandHandlerTests</c>
    /// (Application layer) proves the symmetric direction (clearing the input while its variant is
    /// already active).</summary>
    [Fact]
    public async Task Switching_To_BottomUpEtc_Before_EacManualEtc_Is_Configured_Returns_400()
    {
        using var client = _factory.CreateClient();
        var (_, emailDomain, projectId) = SeedIsolatedTenantAndProject(UserRole.PM);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        var response = await client.PutAsJsonAsync(EacDefaultUrl(projectId), new { Variant = "BottomUpEtc" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/eac-manual-etc-required-for-bottom-up-etc", problem!.Type);
    }

    [Fact]
    public async Task Switching_To_CustomPf_Before_EacCustomPerformanceFactor_Is_Configured_Returns_400()
    {
        using var client = _factory.CreateClient();
        var (_, emailDomain, projectId) = SeedIsolatedTenantAndProject(UserRole.PM);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        var response = await client.PutAsJsonAsync(EacDefaultUrl(projectId), new { Variant = "CustomPf" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/eac-custom-performance-factor-required-for-custom-pf", problem!.Type);
    }

    [Fact]
    public async Task Switching_To_BottomUpEtc_Succeeds_Once_EacManualEtc_Has_Been_Configured()
    {
        using var client = _factory.CreateClient();
        var (_, emailDomain, projectId) = SeedIsolatedTenantAndProject(UserRole.PM);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        var setInputResponse = await client.PutAsJsonAsync(
            EacAdvancedInputsUrl(projectId), new { EacManualEtc = 760_000.00m, EacCustomPerformanceFactor = (decimal?)null });
        Assert.Equal(HttpStatusCode.OK, setInputResponse.StatusCode);

        var switchResponse = await client.PutAsJsonAsync(EacDefaultUrl(projectId), new { Variant = "BottomUpEtc" });

        Assert.Equal(HttpStatusCode.OK, switchResponse.StatusCode);
    }

    [Fact]
    public async Task Clearing_EacManualEtc_While_BottomUpEtc_Is_Active_Returns_400()
    {
        using var client = _factory.CreateClient();
        var (_, emailDomain, projectId) = SeedIsolatedTenantAndProject(UserRole.PM);
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);

        await client.PutAsJsonAsync(
            EacAdvancedInputsUrl(projectId), new { EacManualEtc = 760_000.00m, EacCustomPerformanceFactor = (decimal?)null });
        await client.PutAsJsonAsync(EacDefaultUrl(projectId), new { Variant = "BottomUpEtc" });

        var response = await client.PutAsJsonAsync(
            EacAdvancedInputsUrl(projectId), new { EacManualEtc = (decimal?)null, EacCustomPerformanceFactor = (decimal?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/eac-manual-etc-required-for-bottom-up-etc", problem!.Type);
    }
}
