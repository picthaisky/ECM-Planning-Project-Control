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

    private async Task<Guid> GetSeededProjectIdAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        var project = await dbContext.Projects.IgnoreQueryFilters().FirstAsync(p => p.TenantId == tenantId);
        return project.Id;
    }

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
        // ADR-0015 (human-confirmed 2026-08-10: "Threshold default 10.00%") / sprint-10 security
        // review M-03: CumulativeVoEscalationPct is seeded 10.00, not NULL. domain-rules.md §4.5's
        // "PENDING - no default set" predates the human's answer and is superseded by the ADR.
        // Seeding NULL past that point left cumulative-VO escalation switched OFF in every tenant with
        // no signal distinguishing "deliberately disabled" from "provisioning never turned it on" -
        // execution-verified in the review: a project 41.9% varied approved another VO with no
        // Executive signature anywhere.
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

    // ------------------------------------------------------------------------------------
    // S9-BE-06: PUT .../approval-policies/{documentType} - write API, Admin-only, creates
    // Version+1 and deactivates the previous version.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Admin_Put_Creates_Version_2_And_The_Subsequent_Get_Reflects_It()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@siam-construction.dev");
        Authorize(client, admin.AccessToken);

        var putResponse = await client.PutAsJsonAsync(
            $"/api/v1/tenants/{admin.TenantId}/approval-policies/VariationOrder",
            new
            {
                AllowSelfApproval = true,
                CumulativeVoEscalationPct = 15.00m,
                CumulativeVoEscalationRole = "Executive",
                Rules = new[] { new { StepNo = 1, MinAmount = 0.00m, MaxAmount = (decimal?)null, RequiredRole = "PM", QuorumCount = 1 } },
            });

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var putBody = await putResponse.Content.ReadFromJsonAsync<ApprovalPolicyResponse>(ResponseJsonOptions);
        Assert.Equal(2, putBody!.Version); // TH-Default-VO seeded as v1 - this edit is v2, never an in-place edit
        Assert.True(putBody.AllowSelfApproval);

        var getResponse = await client.GetAsync($"/api/v1/tenants/{admin.TenantId}/approval-policies?documentType=VariationOrder");
        var getBody = await getResponse.Content.ReadFromJsonAsync<ApprovalPolicyResponse>(ResponseJsonOptions);
        Assert.Equal(2, getBody!.Version);
        Assert.True(getBody.IsActive);
        Assert.Single(getBody.Rules);
    }

    [Fact]
    public async Task NonAdmin_Role_Is_Forbidden_From_Writing_A_Policy()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/tenants/{pm.TenantId}/approval-policies/VariationOrder",
            new { AllowSelfApproval = false, Rules = new[] { new { StepNo = 1, MinAmount = 0.00m, MaxAmount = (decimal?)null, RequiredRole = "PM" } } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Put_With_A_Gapped_Band_Returns_400_Identifying_The_Offending_StepNo()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@bkk-infra.dev");
        Authorize(client, admin.AccessToken);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/tenants/{admin.TenantId}/approval-policies/VariationOrder",
            new
            {
                AllowSelfApproval = false,
                Rules = new[]
                {
                    new { StepNo = 1, MinAmount = 0.00m, MaxAmount = (decimal?)500_000.00m, RequiredRole = "PM" },
                    new { StepNo = 1, MinAmount = 500_000.00m, MaxAmount = (decimal?)5_000_000.00m, RequiredRole = "PM" },
                    new { StepNo = 3, MinAmount = 500_000.00m, MaxAmount = (decimal?)5_000_000.00m, RequiredRole = "Executive" },
                },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"invalidStepNo\":2", body);
        Assert.Contains("\"problem\":\"BandGap\"", body);
    }

    [Fact]
    public async Task Cross_Tenant_Put_Returns_A_Bare_404()
    {
        using var client = _factory.CreateClient();
        var adminOfTenantA = await LoginAsync(client, "admin@siam-construction.dev");
        var adminOfTenantB = await LoginAsync(client, "admin@bkk-infra.dev");
        Authorize(client, adminOfTenantA.AccessToken);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/tenants/{adminOfTenantB.TenantId}/approval-policies/VariationOrder",
            new { AllowSelfApproval = false, Rules = new[] { new { StepNo = 1, MinAmount = 0.00m, MaxAmount = (decimal?)null, RequiredRole = "PM" } } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------------------------
    // S15-BE-01: POST .../approval-policies/{documentType}/simulate - the routing simulator.
    // Wiring/contract/roles only (real routing arithmetic is covered at the Application/
    // Integration-harness level - CMPlus.Application.Tests.Features.Approval.
    // SimulateApprovalRoutingQueryHandlerTests, CMPlus.Integration.Tests.Vo.
    // ApprovalRoutingSimulatorIntegrationTests).
    // ------------------------------------------------------------------------------------

    private sealed record SimulatedStepResponse(int StepNo, string RequiredRole, int QuorumCount);

    private sealed record AmbiguousPolicyResponse(Guid ApprovalPolicyId, int Version);

    private sealed record SimulationResponse(
        string DocumentType, Guid ProjectId, decimal InputAmount, decimal RoutingAmount,
        Guid ApprovalPolicyId, int ApprovalPolicyVersion, bool UsedFallbackChain,
        List<SimulatedStepResponse> Steps, bool EscalationApplied, bool AllowSelfApproval,
        bool MultipleActivePoliciesDetected, List<AmbiguousPolicyResponse> AmbiguousActivePolicies);

    /// <summary>
    /// Reads the currently-active policy via the existing <c>GET</c> endpoint FIRST, as ground truth,
    /// rather than assuming "version 1" - this test class shares one <see cref="CustomWebApplicationFactory"/>
    /// (hence one database) across every test method in the file (e.g.
    /// <see cref="Admin_Put_Creates_Version_2_And_The_Subsequent_Get_Reflects_It"/> above edits this
    /// exact tenant's VO policy), so hard-coding the seeded version would make this test's pass/fail
    /// depend on unrelated test execution order - a real bug class, not a hypothetical one.
    /// </summary>
    [Fact]
    public async Task Admin_Can_Simulate_Vo_Routing_Against_A_Real_Project_Without_Creating_Anything()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@siam-construction.dev");
        Authorize(client, admin.AccessToken);
        var projectId = await GetSeededProjectIdAsync(admin.TenantId);

        var currentPolicy = await (await client.GetAsync(
                $"/api/v1/tenants/{admin.TenantId}/approval-policies?documentType=VariationOrder"))
            .Content.ReadFromJsonAsync<ApprovalPolicyResponse>(ResponseJsonOptions);
        Assert.NotNull(currentPolicy);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{admin.TenantId}/approval-policies/VariationOrder/simulate",
            new { ProjectId = projectId, Amount = 2_400_000.00m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var simulation = await response.Content.ReadFromJsonAsync<SimulationResponse>(ResponseJsonOptions);

        Assert.NotNull(simulation);
        Assert.Equal("VariationOrder", simulation!.DocumentType);
        Assert.Equal(projectId, simulation.ProjectId);
        Assert.Equal(2_400_000.00m, simulation.InputAmount);
        Assert.Equal(2_400_000.00m, simulation.RoutingAmount); // Add VO: abs() is a no-op here
        Assert.Equal(currentPolicy!.Version, simulation.ApprovalPolicyVersion); // resolves against the LIVE version
        Assert.NotEmpty(simulation.Steps);
        Assert.False(simulation.UsedFallbackChain);
        Assert.False(simulation.MultipleActivePoliciesDetected);
    }

    [Fact]
    public async Task NonAdmin_Role_Is_Forbidden_From_Simulating_Routing()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var projectId = await GetSeededProjectIdAsync(pm.TenantId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{pm.TenantId}/approval-policies/VariationOrder/simulate",
            new { ProjectId = projectId, Amount = 2_400_000.00m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cross_Tenant_Simulate_Returns_A_Bare_404_Never_Leaking_The_Other_Tenants_Chain()
    {
        using var client = _factory.CreateClient();
        var adminOfTenantA = await LoginAsync(client, "admin@siam-construction.dev");
        var adminOfTenantB = await LoginAsync(client, "admin@bkk-infra.dev");
        Authorize(client, adminOfTenantA.AccessToken);
        var tenantBProjectId = await GetSeededProjectIdAsync(adminOfTenantB.TenantId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{adminOfTenantB.TenantId}/approval-policies/VariationOrder/simulate",
            new { ProjectId = tenantBProjectId, Amount = 2_400_000.00m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Simulating_With_A_Project_From_Another_Tenant_Returns_404_Not_A_Chain()
    {
        using var client = _factory.CreateClient();
        var adminOfTenantA = await LoginAsync(client, "admin@siam-construction.dev");
        var adminOfTenantB = await LoginAsync(client, "admin@bkk-infra.dev");
        Authorize(client, adminOfTenantA.AccessToken);
        var tenantBProjectId = await GetSeededProjectIdAsync(adminOfTenantB.TenantId);

        // Route tenantId is tenant A's own (passes the controller's own-tenant gate) but the
        // ProjectId inside the body belongs to tenant B - must still 404, never resolve a chain
        // against another tenant's project.
        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{adminOfTenantA.TenantId}/approval-policies/VariationOrder/simulate",
            new { ProjectId = tenantBProjectId, Amount = 2_400_000.00m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------------------------
    // S15-BE-01: GET .../approval-policies/{documentType}/history - the version-history read.
    // ------------------------------------------------------------------------------------

    private sealed record HistoryEntryResponse(
        Guid ApprovalPolicyId, int Version, bool IsActive, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
        bool AllowSelfApproval, decimal? CumulativeVoEscalationPct, string? CumulativeVoEscalationRole, int RuleCount,
        Guid? CreatedByUserId, DateTimeOffset? CreatedAt, Guid? LastModifiedByUserId, DateTimeOffset? LastModifiedAt);

    /// <summary>Cross-checked against the live <c>GET</c> endpoint rather than a hard-coded version -
    /// see <see cref="Admin_Can_Simulate_Vo_Routing_Against_A_Real_Project_Without_Creating_Anything"/>'s
    /// own remarks on why this shared-fixture test class cannot assume "version 1".</summary>
    [Fact]
    public async Task Admin_Can_Read_The_Seeded_Policys_Version_History()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@siam-construction.dev");
        Authorize(client, admin.AccessToken);

        var currentPolicy = await (await client.GetAsync(
                $"/api/v1/tenants/{admin.TenantId}/approval-policies?documentType=VariationOrder"))
            .Content.ReadFromJsonAsync<ApprovalPolicyResponse>(ResponseJsonOptions);
        Assert.NotNull(currentPolicy);

        var response = await client.GetAsync($"/api/v1/tenants/{admin.TenantId}/approval-policies/VariationOrder/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = await response.Content.ReadFromJsonAsync<List<HistoryEntryResponse>>(ResponseJsonOptions);

        Assert.NotNull(history);
        Assert.NotEmpty(history!);
        // Every version ever created is still present (never deleted, approval-workflow.md §5.2) -
        // exactly ONE of them is IsActive, and it must be the same version/rule count the ordinary
        // read endpoint reports right now.
        var activeEntry = Assert.Single(history!, h => h.IsActive);
        Assert.Equal(currentPolicy!.Version, activeEntry.Version);
        Assert.Equal(currentPolicy.Rules.Count, activeEntry.RuleCount);
    }

    [Fact]
    public async Task NonAdmin_Role_Is_Forbidden_From_Reading_History()
    {
        using var client = _factory.CreateClient();
        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);

        var response = await client.GetAsync($"/api/v1/tenants/{pm.TenantId}/approval-policies/VariationOrder/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cross_Tenant_History_Request_Returns_A_Bare_404_Never_Leaking_The_Other_Tenants_History()
    {
        using var client = _factory.CreateClient();
        var adminOfTenantA = await LoginAsync(client, "admin@siam-construction.dev");
        var adminOfTenantB = await LoginAsync(client, "admin@bkk-infra.dev");
        Authorize(client, adminOfTenantA.AccessToken);

        var response = await client.GetAsync($"/api/v1/tenants/{adminOfTenantB.TenantId}/approval-policies/VariationOrder/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
