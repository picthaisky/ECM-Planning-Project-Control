using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CMPlus.Infrastructure.Persistence.Seed;
using CMPlus.WebApi.Json;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S2-BE-06/07 end-to-end: the seeded <c>TH-Default-VO</c>/<c>TH-Default-IPC</c> policies are
/// readable by an Admin of their own tenant, Admin-only is enforced, and a cross-tenant request
/// returns a bare 404 - never 403, never a body that would let a caller confirm another tenant's
/// existence or leak its policy data. The cross-tenant case doubles as the S2-BE-01 DoD proof that
/// the *JWT's* tenantId claim - not the route's <c>{tenantId}</c> segment - is what actually scopes
/// the query: tenant B is a real, seeded tenant with its own real active policy, so a route-trusting
/// implementation would return 200 with tenant B's data here instead of 404.
/// </summary>
public class TenantApprovalPoliciesControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record ApprovalPolicyRuleResponse(int StepNo, decimal MinAmount, decimal? MaxAmount, string RequiredRole, int QuorumCount);

    private sealed record ApprovalPolicyResponse(
        string DocumentType, int Version, bool IsActive, bool AllowSelfApproval,
        decimal? CumulativeVoEscalationPct, string? CumulativeVoEscalationRole,
        IReadOnlyList<ApprovalPolicyRuleResponse> Rules);

    // design.md §2: every decimal is wire-serialized as a JSON string (DecimalAsStringJsonConverter) -
    // the default System.Net.Http.Json options (JsonSerializerDefaults.Web) don't know that converter.
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new DecimalAsStringJsonConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;

    public TenantApprovalPoliciesControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    private async Task<LoginResponse> LoginAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = DevDataSeeder.DevSeedPassword }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Admin_Can_Read_Their_Own_Tenants_Seeded_TH_Default_VO_Policy()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@siam-construction.dev");
        Authorize(client, admin.AccessToken);

        var response = await client.GetAsync($"/api/v1/tenants/{admin.TenantId}/approval-policies?documentType=VariationOrder");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var policy = await response.Content.ReadFromJsonAsync<ApprovalPolicyResponse>(ResponseJsonOptions);

        Assert.NotNull(policy);
        Assert.Equal(1, policy!.Version);
        Assert.True(policy.IsActive);
        Assert.Equal(10.00m, policy.CumulativeVoEscalationPct);
        Assert.Equal("Executive", policy.CumulativeVoEscalationRole);
        Assert.Equal(6, policy.Rules.Count);
        Assert.Contains(policy.Rules, r => r.StepNo == 3 && r.RequiredRole == "Executive" && r.MinAmount == 5_000_000.00m);
    }

    [Fact]
    public async Task Admin_Can_Read_Their_Own_Tenants_Seeded_TH_Default_IPC_Policy()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@bkk-infra.dev");
        Authorize(client, admin.AccessToken);

        var response = await client.GetAsync($"/api/v1/tenants/{admin.TenantId}/approval-policies?documentType=PaymentCertificate");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var policy = await response.Content.ReadFromJsonAsync<ApprovalPolicyResponse>(ResponseJsonOptions);

        Assert.NotNull(policy);
        Assert.Equal(3, policy!.Rules.Count);
        Assert.Contains(policy.Rules, r => r.StepNo == 3 && r.RequiredRole == "ProjectDirector" && r.MinAmount == 10_000_000.00m);
    }

    [Fact]
    public async Task Cross_Tenant_Request_Returns_A_Bare_404_Never_Leaking_The_Other_Tenants_Data()
    {
        using var client = _factory.CreateClient();
        var adminOfTenantA = await LoginAsync(client, "admin@siam-construction.dev");
        var adminOfTenantB = await LoginAsync(client, "admin@bkk-infra.dev");
        Assert.NotEqual(adminOfTenantA.TenantId, adminOfTenantB.TenantId);

        Authorize(client, adminOfTenantA.AccessToken);

        // Tenant A's admin, authenticated correctly, requests tenant B's (a real, seeded tenant's)
        // policy via the route segment.
        var response = await client.GetAsync($"/api/v1/tenants/{adminOfTenantB.TenantId}/approval-policies?documentType=VariationOrder");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Executive", body, StringComparison.Ordinal); // tenant B's actual rule content must never appear
        Assert.DoesNotContain("cumulativeVoEscalationPct", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonAdmin_Role_Is_Forbidden()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);

        var response = await client.GetAsync($"/api/v1/tenants/{pm.TenantId}/approval-policies?documentType=VariationOrder");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
