using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CMPlus.Domain.Entities;
using CMPlus.Infrastructure.Persistence.Seed;
using CMPlus.WebApi.Json;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S9 read-side gap closure (finding L-04) end to end over real HTTP:
/// `GET /api/v1/projects/{projectId}/payment-certificates` - the milestone/certificate table the
/// Payment Certificate screen needs.
///
/// <para>Every test below other than <see cref="A_Project_With_No_Certificates_Yields_An_Empty_List_Not_An_Error"/>
/// seeds certificates under a fresh, test-local <see cref="Guid.NewGuid"/> "project id" rather than
/// the one real project the dev seeder creates per tenant - deliberately, because all test methods in
/// this class share one <see cref="CustomWebApplicationFactory"/> instance/database
/// (<c>IClassFixture</c> semantics) and xUnit does not guarantee intra-class execution order.
/// <see cref="PaymentCertificate.ProjectId"/> is not FK-enforced (`PaymentCertificateConfiguration`'s
/// own remarks - project existence is checked at the Application layer, and this list read
/// deliberately does not check it either, matching the established list-read precedent), so a
/// synthetic project id exercises the exact same query path as a genuine one without risking two
/// tests colliding on the single shared seeded project.</para>
/// </summary>
public class ProjectPaymentCertificatesControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record CertificateListItemResponse(Guid Id, Guid ProjectId, int MilestoneNo, string Status);

    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new DecimalAsStringJsonConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;

    public ProjectPaymentCertificatesControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    private async Task<LoginResponse> LoginAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = DevDataSeeder.DevSeedPassword }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private Guid SeedCertificate(Guid tenantId, Guid projectId, int milestoneNo, decimal grossCertifiedAmount, Guid createdByUserId)
    {
        using var context = _factory.CreateDbContextForSeeding(tenantId);
        var certificate = new PaymentCertificate(tenantId, projectId, milestoneNo, $"IPC {milestoneNo}", grossCertifiedAmount, 0m, createdByUserId);
        certificate.SetPeriodClaim(100m, null, null, grossCertifiedAmount, 0m, 0m, grossCertifiedAmount);
        context.PaymentCertificates.Add(certificate);
        context.SaveChanges();
        return certificate.Id;
    }

    private async Task<Guid> GetSeededProjectIdAsync(Guid tenantId)
    {
        using var context = _factory.CreateDbContextForSeeding(tenantId);
        var project = await context.Projects.IgnoreQueryFilters().SingleAsync(p => p.TenantId == tenantId);
        return project.Id;
    }

    [Fact]
    public async Task An_Unauthenticated_Request_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/payment-certificates");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_Site_User_Is_Forbidden_From_Listing_Certificates()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/payment-certificates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>The only test in this class that reads the real, dev-seeded per-tenant project -
    /// deliberately the only one, and it never seeds a certificate into it, so it stays reliably
    /// empty regardless of which order xUnit runs this class's other (synthetic-project-id) tests
    /// in.</summary>
    [Fact]
    public async Task A_Project_With_No_Certificates_Yields_An_Empty_List_Not_An_Error()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);
        var projectId = await GetSeededProjectIdAsync(qs.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/payment-certificates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<CertificateListItemResponse>>(ResponseJsonOptions);
        Assert.NotNull(body);
        Assert.Empty(body!);
    }

    [Fact]
    public async Task An_Unknown_Or_Cross_Tenant_Project_Id_Yields_An_Empty_List_Never_A_403_Or_A_Leak()
    {
        using var client = _factory.CreateClient();
        var adminOfTenantA = await LoginAsync(client, "admin@siam-construction.dev");
        var adminOfTenantB = await LoginAsync(client, "admin@bkk-infra.dev");
        var projectIdB = Guid.NewGuid(); // tenant B, a project id this test alone owns
        SeedCertificate(adminOfTenantB.TenantId, projectIdB, 1, 9_999_999.00m, adminOfTenantB.UserId);

        Authorize(client, adminOfTenantA.AccessToken);
        var response = await client.GetAsync($"/api/v1/projects/{projectIdB}/payment-certificates");

        // Established precedent for list reads in this codebase (ListEvmSnapshotsQueryHandler,
        // GetJobHistoryAsync): the global tenant query filter makes tenant B's project id resolve to
        // "nothing visible", so the list is empty rather than a 403/404 that would confirm the id
        // belongs to someone else.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("9999999", body, StringComparison.Ordinal);
        var parsed = JsonSerializer.Deserialize<List<CertificateListItemResponse>>(body, ResponseJsonOptions);
        Assert.Empty(parsed!);
    }

    [Fact]
    public async Task Listing_Excludes_Certificates_Belonging_To_A_Different_Project_In_The_Same_Tenant()
    {
        // M-02 (security review sprint-09.md): this aggregate's transition routes are flat with no
        // project to cross-check; the list route carries a real projectId and this test proves the
        // query actually uses it, not just the ambient tenant filter.
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);
        var ownProjectId = Guid.NewGuid(); // this test's own, unshared "project"
        var otherProjectId = Guid.NewGuid(); // same tenant, a different project

        var inOwnProject = SeedCertificate(qs.TenantId, ownProjectId, 1, 1_000_000.00m, qs.UserId);
        SeedCertificate(qs.TenantId, otherProjectId, 1, 2_000_000.00m, qs.UserId);

        var response = await client.GetAsync($"/api/v1/projects/{ownProjectId}/payment-certificates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<CertificateListItemResponse>>(ResponseJsonOptions);
        var only = Assert.Single(body!);
        Assert.Equal(inOwnProject, only.Id);
        Assert.Equal(ownProjectId, only.ProjectId);
    }

    /// <summary>
    /// Guid.CreateVersion7() only has millisecond resolution and, empirically verified (see this
    /// task's handoff report), no monotonic counter for two ids minted within the same millisecond -
    /// its sub-millisecond bits are plain random per RFC 9562. The short sleeps between seeds force
    /// each certificate into a distinct millisecond so this test is deterministic; they are not
    /// working around a bug in the ordering *comparison* itself (both
    /// <c>PaymentCertificateRepository.ListByProjectAsync</c> and its test fake order by the
    /// RFC-order-preserving ordinal hex string, not a bare <c>Guid</c> comparison) but around
    /// <c>Guid.CreateVersion7()</c>'s own documented sub-millisecond randomness, which no comparison
    /// strategy can fix without a dedicated timestamp column - out of scope for this read-side gap
    /// closure since there is today no production path that creates a <c>PaymentCertificate</c> at
    /// all (`grep "new PaymentCertificate(" backend/src` still returns zero hits), so two
    /// certificates racing within the same millisecond cannot happen in production yet either.
    /// </summary>
    [Fact]
    public async Task Listing_Returns_Newest_First()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@bkk-infra.dev");
        Authorize(client, qs.AccessToken);
        var projectId = Guid.NewGuid(); // this test's own, unshared "project"

        var first = SeedCertificate(qs.TenantId, projectId, 1, 1_000_000.00m, qs.UserId);
        Thread.Sleep(5);
        var second = SeedCertificate(qs.TenantId, projectId, 2, 1_500_000.00m, qs.UserId);
        Thread.Sleep(5);
        var third = SeedCertificate(qs.TenantId, projectId, 3, 2_000_000.00m, qs.UserId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/payment-certificates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<CertificateListItemResponse>>(ResponseJsonOptions);
        Assert.Equal(3, body!.Count);
        Assert.Equal([third, second, first], body.Select(c => c.Id));
    }
}
