using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CMPlus.Domain.Entities;
using CMPlus.Infrastructure.Persistence.Seed;
using CMPlus.WebApi.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S9-BE-05 end to end over real HTTP: JWT auth, the <c>QS/PM/ProjectDirector/Admin</c> RBAC gate
/// on <c>submit</c>/<c>record-payment</c> (the "certificate CRUD surface"), and - the more
/// important proof - that <c>approve</c>/<c>return-for-revision</c>/<c>reject</c> carry <b>no</b>
/// static role gate at all: an authenticated user outside that CRUD list still reaches the command
/// handler (never a bare framework 403) and is refused there, by the resolved chain, with a real
/// <c>ProblemDetails</c> body - never a shortcut for any role name.
/// </summary>
public class PaymentCertificatesControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record PaymentCertificateResponse(
        Guid Id, Guid ProjectId, string Status, int RevisionNo, int CurrentStepNo, int TotalSteps,
        Guid? ApprovalPolicyId, int? ApprovalPolicyVersion, string? PaymentReference);

    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new DecimalAsStringJsonConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;

    public PaymentCertificatesControllerTests(CustomWebApplicationFactory factory)
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

    // ------------------------------------------------------------------------------------
    // submit: the "certificate CRUD surface" role gate (QS/PM/ProjectDirector/Admin).
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Unauthenticated_Submit_Request_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/v1/payment-certificates/{Guid.NewGuid()}/submit", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_Site_User_Is_Forbidden_From_Submitting_A_Certificate()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);

        var response = await client.PostAsync($"/api/v1/payment-certificates/{Guid.NewGuid()}/submit", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_Qs_User_Can_Submit_A_Draft_Certificate_And_The_Real_Seeded_Policy_Resolves_The_Chain()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);
        var projectId = await GetSeededProjectIdAsync(qs.TenantId);
        // Below the 10,000,000.00 ProjectDirector threshold (TH-Default-IPC, approval-workflow.md §8).
        var certificateId = SeedDraftCertificate(qs.TenantId, projectId, 5_000_000.00m, qs.UserId);

        var response = await client.PostAsync($"/api/v1/payment-certificates/{certificateId}/submit", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaymentCertificateResponse>(ResponseJsonOptions);
        Assert.Equal("PendingApproval", body!.Status);
        Assert.Equal(2, body.TotalSteps);
        Assert.Equal(1, body.CurrentStepNo);
        Assert.Equal(1, body.ApprovalPolicyVersion);
    }

    [Fact]
    public async Task RecordPayment_Is_Role_Gated_The_Same_Way_As_Submit()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@bkk-infra.dev");
        Authorize(client, site.AccessToken);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/payment-certificates/{Guid.NewGuid()}/record-payment",
            new { Reference = "CHQ-1", PaidAt = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ------------------------------------------------------------------------------------
    // approve/return-for-revision/reject: NO static role gate - the resolved chain decides.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Approve_Has_No_Static_Role_Gate_A_Site_User_Reaches_The_Handler_And_Is_Refused_By_The_Resolved_Chain()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@siam-construction.dev");
        Authorize(client, admin.AccessToken);
        var projectId = await GetSeededProjectIdAsync(admin.TenantId);
        var certificateId = SeedDraftCertificate(admin.TenantId, projectId, 5_000_000.00m, admin.UserId);
        var submitResponse = await client.PostAsync($"/api/v1/payment-certificates/{certificateId}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode); // step 1 requires QS

        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);

        var response = await client.PostAsJsonAsync($"/api/v1/payment-certificates/{certificateId}/approve", new { Comment = (string?)null });

        // 403, but NOT the bare framework challenge a Roles= attribute would produce - this is the
        // command handler's own ProblemDetails, proving Site genuinely reached the handler and was
        // refused by the resolved chain, not blocked at the route/attribute level.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/not-current-step", problem!.Type);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", problem.Detail);
    }

    [Fact]
    public async Task ReturnForRevision_Without_A_Comment_Returns_A_Validation_Error()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@bkk-infra.dev");
        Authorize(client, admin.AccessToken);
        var projectId = await GetSeededProjectIdAsync(admin.TenantId);
        var certificateId = SeedDraftCertificate(admin.TenantId, projectId, 5_000_000.00m, admin.UserId);
        await client.PostAsync($"/api/v1/payment-certificates/{certificateId}/submit", content: null);

        var qs = await LoginAsync(client, "qs@bkk-infra.dev");
        Authorize(client, qs.AccessToken);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/payment-certificates/{certificateId}/return-for-revision", new { Comment = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/validation-error", problem!.Type);
    }

    [Fact]
    public async Task Full_Chain_Two_Different_Real_Users_Certify_A_Certificate_Then_Record_Payment()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@siam-construction.dev");
        Authorize(client, admin.AccessToken);
        var projectId = await GetSeededProjectIdAsync(admin.TenantId);
        // Admin both creates and submits - neither QS nor PM below is the creator/submitter, so
        // neither trips the self-approval guard.
        var certificateId = SeedDraftCertificate(admin.TenantId, projectId, 5_000_000.00m, admin.UserId);

        var submitResponse = await client.PostAsync($"/api/v1/payment-certificates/{certificateId}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);
        var afterQs = await client.PostAsJsonAsync($"/api/v1/payment-certificates/{certificateId}/approve", new { Comment = "Quantities verified." });
        Assert.Equal(HttpStatusCode.OK, afterQs.StatusCode);
        var afterQsBody = await afterQs.Content.ReadFromJsonAsync<PaymentCertificateResponse>(ResponseJsonOptions);
        Assert.Equal("PendingApproval", afterQsBody!.Status);
        Assert.Equal(2, afterQsBody.CurrentStepNo);

        var pm = await LoginAsync(client, "pm@siam-construction.dev");
        Authorize(client, pm.AccessToken);
        var afterPm = await client.PostAsJsonAsync($"/api/v1/payment-certificates/{certificateId}/approve", new { Comment = (string?)null });
        Assert.Equal(HttpStatusCode.OK, afterPm.StatusCode);
        var afterPmBody = await afterPm.Content.ReadFromJsonAsync<PaymentCertificateResponse>(ResponseJsonOptions);
        Assert.Equal("Certified", afterPmBody!.Status);

        Authorize(client, admin.AccessToken);
        var recordPaymentResponse = await client.PostAsJsonAsync(
            $"/api/v1/payment-certificates/{certificateId}/record-payment",
            new { Reference = "CHQ-000999", PaidAt = DateTimeOffset.Parse("2026-08-20T00:00:00+07:00") });

        Assert.Equal(HttpStatusCode.OK, recordPaymentResponse.StatusCode);
        var paidBody = await recordPaymentResponse.Content.ReadFromJsonAsync<PaymentCertificateResponse>(ResponseJsonOptions);
        Assert.Equal("Paid", paidBody!.Status);
        Assert.Equal("CHQ-000999", paidBody.PaymentReference);
    }
}
