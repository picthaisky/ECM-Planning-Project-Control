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
/// S9 read-side gap closure, quorum-progress figure (security review sprint-09.md §9.5(ii)
/// informational): `GET /api/v1/payment-certificates/{id}` reports real "N of M signatures
/// collected" for the current step. Deliberately its own test class/database (a fresh
/// <see cref="CustomWebApplicationFactory"/> instance per <c>IClassFixture</c> semantics) rather
/// than a method added to <c>PaymentCertificateReadsControllerTests</c> - this test PUTs a
/// non-default (<c>QuorumCount</c> = 2) tenant policy, and xUnit does not guarantee intra-class test
/// method execution order, so sharing a database with tests that assert the *default* seeded chain
/// shape would make this class or that one flaky depending on run order. Isolating it removes the
/// question entirely.
/// </summary>
public class PaymentCertificateQuorumProgressControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record ApprovalStepResponse(int StepNo, string RequiredRole, int QuorumCount);

    private sealed record CertificateReadResponse(
        Guid Id, string Status, int CurrentStepNo, int TotalSteps, List<ApprovalStepResponse> ApprovalSteps,
        int? CurrentStepApprovalsCollected);

    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new DecimalAsStringJsonConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;

    public PaymentCertificateQuorumProgressControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    private async Task<LoginResponse> LoginAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = DevDataSeeder.DevSeedPassword }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private Guid SeedDraftCertificate(Guid tenantId, Guid projectId, decimal grossCertifiedAmount, Guid createdByUserId)
    {
        using var context = _factory.CreateDbContextForSeeding(tenantId);
        var certificate = new PaymentCertificate(tenantId, projectId, 1, "IPC 1", grossCertifiedAmount, 0m, createdByUserId);
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
    public async Task A_First_Signature_On_A_Quorum_Two_Step_Is_Visible_Via_GetById_As_One_Of_Two_Collected()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@siam-construction.dev");
        Authorize(client, admin.AccessToken);

        // Real, production PUT (same endpoint/handler TenantApprovalPoliciesControllerTests already
        // proves works) - reconfigures the tenant's single-step IPC policy to QuorumCount=2.
        var policyResponse = await client.PutAsJsonAsync(
            $"/api/v1/tenants/{admin.TenantId}/approval-policies/PaymentCertificate",
            new
            {
                AllowSelfApproval = false,
                CumulativeVoEscalationPct = (decimal?)null,
                CumulativeVoEscalationRole = (string?)null,
                Rules = new[]
                {
                    new { StepNo = 1, MinAmount = 0.00m, MaxAmount = (decimal?)null, RequiredRole = "QS", QuorumCount = 2 },
                },
            });
        Assert.Equal(HttpStatusCode.OK, policyResponse.StatusCode);

        var projectId = await GetSeededProjectIdAsync(admin.TenantId);
        var certificateId = SeedDraftCertificate(admin.TenantId, projectId, 2_000_000.00m, admin.UserId);
        var submitResponse = await client.PostAsync($"/api/v1/payment-certificates/{certificateId}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var firstQs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, firstQs.AccessToken);
        var approveResponse = await client.PostAsJsonAsync(
            $"/api/v1/payment-certificates/{certificateId}/approve", new { Comment = (string?)null });
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approveBody = await approveResponse.Content.ReadFromJsonAsync<CertificateReadResponse>(ResponseJsonOptions);
        // Quorum not yet satisfied (only 1 of 2 signatures) - status/step must NOT have advanced,
        // proving this scenario is a genuine "quorum pending" case and not an accidental full clear.
        Assert.Equal("PendingApproval", approveBody!.Status);
        Assert.Equal(1, approveBody.CurrentStepNo);

        Authorize(client, admin.AccessToken);
        var getResponse = await client.GetAsync($"/api/v1/payment-certificates/{certificateId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await getResponse.Content.ReadFromJsonAsync<CertificateReadResponse>(ResponseJsonOptions);

        Assert.Equal(2, getBody!.ApprovalSteps[0].QuorumCount);
        Assert.Equal(1, getBody.CurrentStepApprovalsCollected); // "1 of 2" - GetById supplies the real count
    }
}
