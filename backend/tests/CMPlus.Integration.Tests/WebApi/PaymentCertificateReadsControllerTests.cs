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
/// S9 read-side gap closure (finding L-04) end to end over real HTTP:
/// `GET /api/v1/payment-certificates/{id}` and `GET /api/v1/payment-certificates/{id}/approval-actions`.
/// Every test here uses the tenant's default seeded `TH-Default-IPC` policy unchanged - the one test
/// that needs a non-default (`QuorumCount` &gt; 1) policy lives in its own isolated class/database
/// (<c>PaymentCertificateQuorumProgressControllerTests</c>) specifically so a policy mutation here
/// can never make a different test's chain-shape assertion flaky depending on xUnit's (unspecified)
/// intra-class execution order.
/// </summary>
public class PaymentCertificateReadsControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record ApprovalStepResponse(int StepNo, string RequiredRole, int QuorumCount);

    private sealed record CertificateReadResponse(
        Guid Id, Guid ProjectId, string Status, int CurrentStepNo, int TotalSteps,
        bool AllowSelfApproval, List<ApprovalStepResponse> ApprovalSteps, int? CurrentStepApprovalsCollected,
        Guid CreatedByUserId, Guid? SubmittedByUserId);

    private sealed record ApprovalActionResponse(
        Guid Id, string DocumentType, Guid DocumentId, int RevisionNo, int StepNo,
        Guid ActorUserId, string ActorRoleAtTime, string Action, string? Comment, DateTimeOffset ActedAt);

    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new DecimalAsStringJsonConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;

    public PaymentCertificateReadsControllerTests(CustomWebApplicationFactory factory)
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
    // GET /payment-certificates/{id}
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Unauthenticated_GetById_Request_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/payment-certificates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_Site_User_Is_Forbidden_From_Reading_A_Certificate_By_Id()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);

        var response = await client.GetAsync($"/api/v1/payment-certificates/{Guid.NewGuid()}");

        // Unlike approve/return-for-revision/reject, GetById is a static QS/PM/ProjectDirector/Admin
        // gate (this task's explicit instruction to match the certificate CRUD role set) - Site is
        // refused at the attribute level, never reaching a handler.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Getting_An_Unknown_Certificate_By_Id_Returns_404()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);

        var response = await client.GetAsync($"/api/v1/payment-certificates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/not-found", problem!.Type);
        Assert.Equal("PaymentCertificateNotFound", problem.Detail);
    }

    [Fact]
    public async Task Cross_Tenant_GetById_Returns_A_Bare_404_Never_Leaking_The_Other_Tenants_Certificate()
    {
        using var client = _factory.CreateClient();
        var adminOfTenantA = await LoginAsync(client, "admin@siam-construction.dev");
        var adminOfTenantB = await LoginAsync(client, "admin@bkk-infra.dev");
        Assert.NotEqual(adminOfTenantA.TenantId, adminOfTenantB.TenantId);

        var projectIdB = await GetSeededProjectIdAsync(adminOfTenantB.TenantId);
        // A real, seeded tenant B, with a real certificate carrying identifiable data - a
        // route-trusting implementation would leak it here.
        var certificateOfTenantB = SeedDraftCertificate(adminOfTenantB.TenantId, projectIdB, 7_654_321.00m, adminOfTenantB.UserId);

        Authorize(client, adminOfTenantA.AccessToken);
        var response = await client.GetAsync($"/api/v1/payment-certificates/{certificateOfTenantB}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("7654321", body, StringComparison.Ordinal); // tenant B's actual amount must never appear

        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, ResponseJsonOptions);
        Assert.Equal("https://cmplus.dev/problems/not-found", problem!.Type); // identical shape to "truly does not exist"
    }

    [Fact]
    public async Task A_Qs_User_Can_Read_A_Submitted_Certificate_And_Sees_The_Real_Chain_And_Self_Approval_Flag()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@siam-construction.dev");
        Authorize(client, admin.AccessToken);
        var projectId = await GetSeededProjectIdAsync(admin.TenantId);
        var certificateId = SeedDraftCertificate(admin.TenantId, projectId, 5_000_000.00m, admin.UserId);
        var submitResponse = await client.PostAsync($"/api/v1/payment-certificates/{certificateId}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var response = await client.GetAsync($"/api/v1/payment-certificates/{certificateId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CertificateReadResponse>(ResponseJsonOptions);
        Assert.NotNull(body);
        Assert.Equal("PendingApproval", body!.Status);
        Assert.False(body.AllowSelfApproval); // TH-Default-IPC's seeded default (approval-workflow.md §5.2)
        Assert.Equal(2, body.ApprovalSteps.Count); // 5,000,000 -> [QS, PM], below the 10,000,000 ProjectDirector band
        Assert.Equal("QS", body.ApprovalSteps[0].RequiredRole);
        Assert.Equal(1, body.ApprovalSteps[0].QuorumCount);
        Assert.Equal("PM", body.ApprovalSteps[1].RequiredRole);
        // A chain IS attached (TotalSteps=2) and genuinely nobody has approved step 1 yet - a real 0.
        Assert.Equal(0, body.CurrentStepApprovalsCollected);
    }

    [Fact]
    public async Task Reading_A_Certificate_Not_Yet_Submitted_Has_No_Attached_Chain_And_Null_Quorum_Progress()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@bkk-infra.dev");
        Authorize(client, admin.AccessToken);
        var projectId = await GetSeededProjectIdAsync(admin.TenantId);
        var certificateId = SeedDraftCertificate(admin.TenantId, projectId, 1_200_000.00m, admin.UserId);

        var response = await client.GetAsync($"/api/v1/payment-certificates/{certificateId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CertificateReadResponse>(ResponseJsonOptions);
        Assert.Equal("Draft", body!.Status);
        Assert.Equal(0, body.TotalSteps);
        Assert.Empty(body.ApprovalSteps);
        Assert.Null(body.CurrentStepApprovalsCollected); // "not applicable", never a fabricated 0
    }

    // ------------------------------------------------------------------------------------
    // GET /payment-certificates/{id}/approval-actions
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Unauthenticated_GetApprovalActions_Request_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/payment-certificates/{Guid.NewGuid()}/approval-actions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_Site_User_Is_Forbidden_From_Reading_Approval_Actions()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@bkk-infra.dev");
        Authorize(client, site.AccessToken);

        var response = await client.GetAsync($"/api/v1/payment-certificates/{Guid.NewGuid()}/approval-actions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetApprovalActions_On_An_Unknown_Certificate_Returns_404()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);

        var response = await client.GetAsync($"/api/v1/payment-certificates/{Guid.NewGuid()}/approval-actions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cross_Tenant_GetApprovalActions_Returns_A_Bare_404_Never_A_200_With_An_Empty_Array()
    {
        using var client = _factory.CreateClient();
        var adminOfTenantA = await LoginAsync(client, "admin@siam-construction.dev");
        var adminOfTenantB = await LoginAsync(client, "admin@bkk-infra.dev");

        var projectIdB = await GetSeededProjectIdAsync(adminOfTenantB.TenantId);
        var certificateOfTenantB = SeedDraftCertificate(adminOfTenantB.TenantId, projectIdB, 3_000_000.00m, adminOfTenantB.UserId);

        Authorize(client, adminOfTenantA.AccessToken);
        var response = await client.GetAsync($"/api/v1/payment-certificates/{certificateOfTenantB}/approval-actions");

        // Must be 404, not 200+[] - a 200 with an empty array would be indistinguishable from "this
        // certificate is real, in my own tenant, and simply has no history yet" (e.g. still Draft),
        // which is a different, meaningful state the caller is entitled to be told apart from
        // "this id is not yours".
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetApprovalActions_Returns_An_Empty_Array_For_A_Real_Certificate_Never_Submitted()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@siam-construction.dev");
        Authorize(client, admin.AccessToken);
        var projectId = await GetSeededProjectIdAsync(admin.TenantId);
        var certificateId = SeedDraftCertificate(admin.TenantId, projectId, 900_000.00m, admin.UserId);

        var response = await client.GetAsync($"/api/v1/payment-certificates/{certificateId}/approval-actions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = await response.Content.ReadFromJsonAsync<List<ApprovalActionResponse>>(ResponseJsonOptions);
        Assert.NotNull(history);
        Assert.Empty(history!);
    }

    [Fact]
    public async Task GetApprovalActions_Returns_The_Full_Ordered_History_After_Submit_And_Approve()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@bkk-infra.dev");
        Authorize(client, admin.AccessToken);
        var projectId = await GetSeededProjectIdAsync(admin.TenantId);
        var certificateId = SeedDraftCertificate(admin.TenantId, projectId, 5_000_000.00m, admin.UserId);
        var submitResponse = await client.PostAsync($"/api/v1/payment-certificates/{certificateId}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var qs = await LoginAsync(client, "qs@bkk-infra.dev");
        Authorize(client, qs.AccessToken);
        var approveResponse = await client.PostAsJsonAsync(
            $"/api/v1/payment-certificates/{certificateId}/approve", new { Comment = "Verified against site records." });
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        Authorize(client, admin.AccessToken);
        var response = await client.GetAsync($"/api/v1/payment-certificates/{certificateId}/approval-actions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = await response.Content.ReadFromJsonAsync<List<ApprovalActionResponse>>(ResponseJsonOptions);
        Assert.NotNull(history);
        Assert.Equal(2, history!.Count);

        Assert.Equal("Submit", history[0].Action);
        Assert.Equal(0, history[0].StepNo);
        Assert.Equal("PaymentCertificate", history[0].DocumentType);
        Assert.Equal(certificateId, history[0].DocumentId);

        Assert.Equal("Approve", history[1].Action);
        Assert.Equal(1, history[1].StepNo);
        Assert.Equal("Verified against site records.", history[1].Comment);
        Assert.Equal(qs.UserId, history[1].ActorUserId);
        Assert.Equal("QS", history[1].ActorRoleAtTime);
        Assert.True(history[1].ActedAt >= history[0].ActedAt); // oldest-first
    }
}
