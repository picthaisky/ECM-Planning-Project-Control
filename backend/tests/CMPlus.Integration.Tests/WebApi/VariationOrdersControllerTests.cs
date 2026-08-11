using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Auth;
using CMPlus.Infrastructure.Persistence.Seed;
using CMPlus.WebApi.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S10-BE-01/02/03 end to end over real HTTP - the WebApi controllers, DI wiring, JWT auth and RBAC
/// gates, exercised for the first time (they carry no unit-level coverage of their own, by design -
/// the routing/state-machine logic itself is proven in
/// <c>CMPlus.Integration.Tests.Vo.VariationOrderApprovalRoutingFixtureTests</c> against the real
/// handlers directly). Mirrors <see cref="PaymentCertificatesControllerTests"/>'s shape: the
/// "certificate/VO CRUD surface" (create/submit/list/get) IS role-gated, but
/// <c>approve</c>/<c>return-for-revision</c>/<c>reject</c> carry NO static role gate - authority is
/// resolved entirely from the document's version-pinned approval chain.
/// </summary>
public class VariationOrdersControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record VoStepResponse(string RequiredRole);

    private sealed record VoResponse(
        Guid Id, Guid ProjectId, string VoNumber, string Status, decimal Amount, string Type,
        int RevisionNo, int CurrentStepNo, int TotalSteps, IReadOnlyList<VoStepResponse> ApprovalSteps);

    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new DecimalAsStringJsonConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;

    public VariationOrdersControllerTests(CustomWebApplicationFactory factory)
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
        using var context = _factory.CreateDbContextForSeeding(tenantId);
        var project = await context.Projects.IgnoreQueryFilters().SingleAsync(p => p.TenantId == tenantId);
        return project.Id;
    }

    /// <summary>Seeds a real <see cref="Activity"/> (via <see cref="WBSNode"/>) so a VO's scope
    /// payload has a genuine target - the create endpoint validates ActivityIds for real.</summary>
    private Guid SeedActivity(Guid tenantId, Guid projectId, decimal budgetCost)
    {
        using var context = _factory.CreateDbContextForSeeding(tenantId);
        var node = new WBSNode(tenantId, projectId, $"C-{Guid.NewGuid():N}", "Node", 100m);
        context.WBSNodes.Add(node);
        var activity = new Activity(tenantId, node.Id, $"A-{Guid.NewGuid():N}", "Activity", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), 10, budgetCost);
        context.Activities.Add(activity);
        context.SaveChanges();
        return activity.Id;
    }

    /// <summary>
    /// S10-QA-01 gap closure: R4/R5/V-6 each need a project whose escalation math/band gaps are
    /// fully under this test's own control - genuinely isolated, never an extra project added to one
    /// of the two dev-seeded tenants. Adding a second project under an existing dev-seeded tenant was
    /// tried first and broke every other test in this class: <see cref="GetSeededProjectIdAsync"/>
    /// (used throughout this file) assumes exactly one project per tenant
    /// (<c>SingleAsync(p =&gt; p.TenantId == tenantId)</c>), so a whole new <see cref="Tenant"/> - with
    /// its own dedicated user per role, hashed with the same
    /// <see cref="DevDataSeeder.DevSeedPassword"/> every dev-seeded user has so
    /// <see cref="LoginAsync"/> needs no special-casing - is the only isolation that does not
    /// perturb the shared fixture database. Seeded directly via EF (same "own
    /// <c>CreateDbContextForSeeding</c>, never the HTTP-bound create surface" pattern
    /// <see cref="SeedActivity"/> already establishes).
    /// </summary>
    private (Guid TenantId, string EmailDomain) SeedIsolatedTenant(params UserRole[] roles)
    {
        var emailDomain = $"vo-http-fixture-{Guid.NewGuid():N}.dev";
        var tenant = new Tenant($"VO HTTP Fixture Tenant {Guid.NewGuid():N}");

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

    /// <summary>Seeds a real, isolated <see cref="Project"/> under <paramref name="tenantId"/> - see
    /// <see cref="SeedIsolatedTenant"/>'s remarks for why the tenant itself must also be isolated.</summary>
    private Guid SeedIsolatedProject(Guid tenantId, decimal bac, decimal? originalContractValue = null)
    {
        using var context = _factory.CreateDbContextForSeeding(tenantId);
        var project = Project.Create(
            tenantId, "VO HTTP Fixture Project", $"VOHTTP-{Guid.NewGuid():N}", "Owner",
            DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(1), bac, DateTimeOffset.UtcNow,
            contractValue: originalContractValue ?? bac);
        context.Projects.Add(project);
        context.SaveChanges();
        return project.Id;
    }

    /// <summary>domain-rules.md §3.3's <c>TH-Default-VO</c> band shape, as a project-scoped override
    /// carrying a configured cumulative-VO-escalation threshold - the tenant-wide default policy
    /// (<see cref="CMPlus.Infrastructure.Persistence.Seed.ApprovalPolicySeeder"/>) deliberately seeds
    /// <c>CumulativeVoEscalationPct = NULL</c> (ADR-0015), which is exactly the real-HTTP gap
    /// qa-engineer found: no HTTP test anywhere seeded an escalation policy at all.</summary>
    private void SeedEscalationPolicy(
        Guid tenantId, Guid projectId, DateTimeOffset effectiveFrom, decimal thresholdPct = 10.00m, UserRole escalationRole = UserRole.Executive)
    {
        using var context = _factory.CreateDbContextForSeeding(tenantId);
        var policy = ApprovalPolicy.CreateInitialVersion(
            tenantId, projectId, ApprovalDocumentType.VariationOrder, effectiveFrom,
            [
                new ApprovalPolicyRuleInput(1, 0.00m, 500_000.00m, UserRole.PM),
                new ApprovalPolicyRuleInput(1, 500_000.00m, 5_000_000.00m, UserRole.PM),
                new ApprovalPolicyRuleInput(2, 500_000.00m, 5_000_000.00m, UserRole.ProjectDirector),
                new ApprovalPolicyRuleInput(1, 5_000_000.00m, null, UserRole.PM),
                new ApprovalPolicyRuleInput(2, 5_000_000.00m, null, UserRole.ProjectDirector),
                new ApprovalPolicyRuleInput(3, 5_000_000.00m, null, UserRole.Executive),
            ],
            allowSelfApproval: false, cumulativeVoEscalationPct: thresholdPct, cumulativeVoEscalationRole: escalationRole);
        context.ApprovalPolicies.Add(policy);
        context.SaveChanges();
    }

    /// <summary>domain-rules.md §3.3's <c>TH-Gap-VO</c>: nothing covers <c>[0, 100,000)</c>, for R5.</summary>
    private void SeedGapPolicy(Guid tenantId, Guid projectId, DateTimeOffset effectiveFrom)
    {
        using var context = _factory.CreateDbContextForSeeding(tenantId);
        var policy = ApprovalPolicy.CreateInitialVersion(
            tenantId, projectId, ApprovalDocumentType.VariationOrder, effectiveFrom,
            [new ApprovalPolicyRuleInput(1, 100_000.00m, null, UserRole.PM)]);
        context.ApprovalPolicies.Add(policy);
        context.SaveChanges();
    }

    /// <summary>Creates and submits a VO with a single scope line whose delta equals
    /// <paramref name="amount"/> exactly (domain-rules.md §5.2's invariant) - both real HTTP calls.
    /// Asserts both succeed (200) so a caller's own assertions can focus on the chain shape.</summary>
    private async Task<Guid> CreateAndSubmitAsync(HttpClient client, Guid projectId, decimal amount, Guid activityId)
    {
        var createResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/variation-orders",
            new
            {
                VoNumber = $"VO-{Guid.NewGuid():N}",
                Amount = amount,
                TimeImpactDays = 0,
                ScopeItems = new[] { new { ActivityId = activityId, BudgetCostDelta = amount } },
            });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);

        var submitResponse = await client.PostAsync($"/api/v1/variation-orders/{created!.Id}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        return created.Id;
    }

    /// <summary>Logs in as each role in <paramref name="chain"/> (in order) against
    /// <paramref name="emailDomain"/>'s seeded users and approves <paramref name="voId"/> once per
    /// role - real HTTP throughout, mirroring <see cref="Full_Chain_Two_Different_Real_Users_Approve_A_Vo_And_The_Projects_Bac_Moves_By_The_Signed_Amount"/>'s
    /// shape for an arbitrary chain length. Returns the final response's <c>Status</c>.</summary>
    private async Task<string> ApproveFullChainAsync(HttpClient client, Guid voId, string emailDomain, IReadOnlyList<UserRole> chain)
    {
        string? status = null;
        foreach (var role in chain)
        {
            var approver = await LoginAsync(client, $"{role.ToString().ToLowerInvariant()}@{emailDomain}");
            Authorize(client, approver.AccessToken);
            var response = await client.PostAsJsonAsync($"/api/v1/variation-orders/{voId}/approve", new { Comment = (string?)null });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
            status = body!.Status;
        }

        return status!;
    }

    [Fact]
    public async Task An_Unauthenticated_Create_Request_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{Guid.NewGuid()}/variation-orders",
            new { VoNumber = "VO-1", Amount = 100_000.00m, TimeImpactDays = 0, ScopeItems = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_Site_User_Is_Forbidden_From_Creating_A_Variation_Order()
    {
        using var client = _factory.CreateClient();
        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{Guid.NewGuid()}/variation-orders",
            new { VoNumber = "VO-1", Amount = 100_000.00m, TimeImpactDays = 0, ScopeItems = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_Qs_User_Can_Create_And_Submit_A_Vo_And_The_Real_Seeded_Policy_Resolves_The_Chain_R3_Shape()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);
        var projectId = await GetSeededProjectIdAsync(qs.TenantId);
        var activityId = SeedActivity(qs.TenantId, projectId, 5_000_000.00m);

        // R3: Add +300,000.00 -> single-step [PM] (below the 500k band boundary).
        var createResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/variation-orders",
            new
            {
                VoNumber = $"VO-{Guid.NewGuid():N}",
                Description = "Additional works",
                Justification = "Site instruction",
                Amount = 300_000.00m,
                TimeImpactDays = 0,
                ScopeItems = new[] { new { ActivityId = activityId, BudgetCostDelta = 300_000.00m } },
            });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal("Draft", created!.Status);

        var submitResponse = await client.PostAsync($"/api/v1/variation-orders/{created.Id}/submit", content: null);

        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal("PendingApproval", submitted!.Status);
        Assert.Equal(1, submitted.TotalSteps);
        Assert.Equal("PM", submitted.ApprovalSteps.Single().RequiredRole);
    }

    [Fact]
    public async Task Approve_Has_No_Static_Role_Gate_A_Site_User_Reaches_The_Handler_And_Is_Refused_By_The_Resolved_Chain()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@siam-construction.dev");
        Authorize(client, admin.AccessToken);
        var projectId = await GetSeededProjectIdAsync(admin.TenantId);
        var activityId = SeedActivity(admin.TenantId, projectId, 5_000_000.00m);

        var createResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/variation-orders",
            new
            {
                VoNumber = $"VO-{Guid.NewGuid():N}",
                Amount = 300_000.00m,
                TimeImpactDays = 0,
                ScopeItems = new[] { new { ActivityId = activityId, BudgetCostDelta = 300_000.00m } },
            });
        var created = await createResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        var submitResponse = await client.PostAsync($"/api/v1/variation-orders/{created!.Id}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode); // requires PM

        var site = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, site.AccessToken);

        var response = await client.PostAsJsonAsync($"/api/v1/variation-orders/{created.Id}/approve", new { Comment = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/not-current-step", problem!.Type);
        Assert.Equal("VariationOrderNotAuthorizedForApprovalStep", problem.Detail);
    }

    [Fact]
    public async Task Full_Chain_Two_Different_Real_Users_Approve_A_Vo_And_The_Projects_Bac_Moves_By_The_Signed_Amount()
    {
        using var client = _factory.CreateClient();
        var admin = await LoginAsync(client, "admin@bkk-infra.dev");
        Authorize(client, admin.AccessToken);
        var projectId = await GetSeededProjectIdAsync(admin.TenantId);
        var activityId = SeedActivity(admin.TenantId, projectId, 5_000_000.00m);

        // 800,000.00 -> [500k, 5M) band -> [PM, ProjectDirector], matching R2/R6's own shape.
        var createResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/variation-orders",
            new
            {
                VoNumber = $"VO-{Guid.NewGuid():N}",
                Amount = 800_000.00m,
                TimeImpactDays = 0,
                ScopeItems = new[] { new { ActivityId = activityId, BudgetCostDelta = 800_000.00m } },
            });
        var created = await createResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/variation-orders/{created!.Id}/submit", content: null)).StatusCode);

        Guid tenantId = admin.TenantId;
        decimal bacBefore;
        using (var context = _factory.CreateDbContextForSeeding(tenantId))
        {
            bacBefore = context.Projects.IgnoreQueryFilters().Single(p => p.Id == projectId).BAC;
        }

        var pm = await LoginAsync(client, "pm@bkk-infra.dev");
        Authorize(client, pm.AccessToken);
        var afterPm = await client.PostAsJsonAsync($"/api/v1/variation-orders/{created.Id}/approve", new { Comment = "Reviewed." });
        Assert.Equal(HttpStatusCode.OK, afterPm.StatusCode);
        var afterPmBody = await afterPm.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal("PendingApproval", afterPmBody!.Status);
        Assert.Equal(2, afterPmBody.CurrentStepNo);

        var director = await LoginAsync(client, "projectdirector@bkk-infra.dev");
        Authorize(client, director.AccessToken);
        var afterDirector = await client.PostAsJsonAsync($"/api/v1/variation-orders/{created.Id}/approve", new { Comment = (string?)null });
        Assert.Equal(HttpStatusCode.OK, afterDirector.StatusCode);
        var afterDirectorBody = await afterDirector.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal("Approved", afterDirectorBody!.Status);
        Assert.Equal(800_000.00m, afterDirectorBody.Amount); // never overwritten to abs()

        using var verifyContext = _factory.CreateDbContextForSeeding(tenantId);
        var projectAfter = verifyContext.Projects.IgnoreQueryFilters().Single(p => p.Id == projectId);
        Assert.Equal(bacBefore + 800_000.00m, projectAfter.BAC);
    }

    // -----------------------------------------------------------------------------------------
    // S10-QA-01 gap closure. domain-rules.md §3.4's DoD: R1-R6 "ผ่านครบผ่าน API จริง (ไม่ใช่แค่ unit
    // ของ service)" - pass completely through the real API, not merely a unit of the service. Before
    // this, only R3 (and a positive-amount BAC move) had real-HTTP coverage; R1, R2, R4, R5, R6 existed
    // only at handler level in CMPlus.Integration.Tests.Vo.VariationOrderApprovalRoutingFixtureTests,
    // which never touches this WebApi project - no controller, no JWT/RBAC, no model binding, no JSON
    // round trip. qa-engineer proved the gap was real by mutation: VariationOrder.ApplyContent's
    // `Amount = amount` -> `Amount = Math.Abs(amount)` (turns every Deduct VO into an Add, moving BAC
    // UP instead of down) left the then-existing HTTP suite 10/10 green, because its only BAC test
    // (Full_Chain_... above) used a positive amount; and no HTTP test seeded an escalation policy at
    // all, so neutering the V-6 final-approval guard also left it green.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task R1_Add_2_4M_With_Low_Cumulative_Resolves_Pm_Then_ProjectDirector_No_Escalation_Through_Real_Http()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);
        var projectId = await GetSeededProjectIdAsync(qs.TenantId);
        var activityId = SeedActivity(qs.TenantId, projectId, 10_000_000.00m);

        var createResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/variation-orders",
            new
            {
                VoNumber = $"VO-{Guid.NewGuid():N}",
                Amount = 2_400_000.00m,
                TimeImpactDays = 0,
                ScopeItems = new[] { new { ActivityId = activityId, BudgetCostDelta = 2_400_000.00m } },
            });
        var created = await createResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);

        var submitResponse = await client.PostAsync($"/api/v1/variation-orders/{created!.Id}/submit", content: null);

        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal("PendingApproval", submitted!.Status);
        Assert.Equal(2, submitted.TotalSteps);
        Assert.Equal(
            [nameof(UserRole.PM), nameof(UserRole.ProjectDirector)],
            submitted.ApprovalSteps.Select(s => s.RequiredRole)); // VariationOrderDto.From already orders by StepNo.
    }

    /// <summary>
    /// R2, the load-bearing fixture (domain-rules.md §3.4): asserts (i) the persisted, JSON-round-
    /// tripped <c>Amount</c> is still exactly -800,000.00 after a full create-submit-approve-approve
    /// cycle over real HTTP - never overwritten to <c>Math.Abs()</c> anywhere on the path; (ii) the
    /// resolved chain is byte-identical to a twin +800,000.00 Add's; and (iii) the project's real BAC
    /// column moves DOWN by exactly 800,000.00, never up - the concrete, observable consequence a
    /// <c>Math.Abs(amount)</c> mutation in <c>VariationOrder.ApplyContent</c> would flip.
    /// </summary>
    [Fact]
    public async Task R2_Deduct_800k_Persists_A_Negative_Amount_Through_Real_Http_And_Resolves_The_Same_Chain_As_A_Plus_800k_Add()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);
        var projectId = await GetSeededProjectIdAsync(qs.TenantId);

        var deductActivityId = SeedActivity(qs.TenantId, projectId, 5_000_000.00m);
        var deductCreateResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/variation-orders",
            new
            {
                VoNumber = $"VO-{Guid.NewGuid():N}",
                Amount = -800_000.00m,
                TimeImpactDays = 0,
                ScopeItems = new[] { new { ActivityId = deductActivityId, BudgetCostDelta = -800_000.00m } },
            });
        Assert.Equal(HttpStatusCode.OK, deductCreateResponse.StatusCode);
        var deductCreated = await deductCreateResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal(-800_000.00m, deductCreated!.Amount);
        Assert.Equal("Deduct", deductCreated.Type);

        var deductSubmitResponse = await client.PostAsync($"/api/v1/variation-orders/{deductCreated.Id}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, deductSubmitResponse.StatusCode);
        var deductSubmitted = await deductSubmitResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        var deductChain = deductSubmitted!.ApprovalSteps.Select(s => s.RequiredRole).ToList();
        Assert.Equal([nameof(UserRole.PM), nameof(UserRole.ProjectDirector)], deductChain);

        // Twin control: Add +800,000.00 must resolve to the byte-identical chain (ii).
        var addActivityId = SeedActivity(qs.TenantId, projectId, 5_000_000.00m);
        var addCreateResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/variation-orders",
            new
            {
                VoNumber = $"VO-{Guid.NewGuid():N}",
                Amount = 800_000.00m,
                TimeImpactDays = 0,
                ScopeItems = new[] { new { ActivityId = addActivityId, BudgetCostDelta = 800_000.00m } },
            });
        var addCreated = await addCreateResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        var addSubmitResponse = await client.PostAsync($"/api/v1/variation-orders/{addCreated!.Id}/submit", content: null);
        var addSubmitted = await addSubmitResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        var addChain = addSubmitted!.ApprovalSteps.Select(s => s.RequiredRole).ToList();

        Assert.Equal(addChain, deductChain);

        decimal bacBefore;
        using (var context = _factory.CreateDbContextForSeeding(qs.TenantId))
        {
            bacBefore = context.Projects.IgnoreQueryFilters().Single(p => p.Id == projectId).BAC;
        }

        var finalStatus = await ApproveFullChainAsync(
            client, deductCreated.Id, "siam-construction.dev", [UserRole.PM, UserRole.ProjectDirector]);
        Assert.Equal("Approved", finalStatus);

        // (i) the persisted Amount column, re-read through a FRESH GET - a genuine JSON round trip,
        // not merely the in-memory command response - is still exactly -800,000.00.
        var getResponse = await client.GetAsync($"/api/v1/variation-orders/{deductCreated.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var persisted = await getResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal(-800_000.00m, persisted!.Amount);
        Assert.Equal("Deduct", persisted.Type);

        // (iii) BAC moved DOWN by exactly the deduct amount, never up.
        using var verifyContext = _factory.CreateDbContextForSeeding(qs.TenantId);
        var projectAfter = verifyContext.Projects.IgnoreQueryFilters().Single(p => p.Id == projectId);
        Assert.Equal(bacBefore - 800_000.00m, projectAfter.BAC);
    }

    [Fact]
    public async Task R4_Escalation_Appends_Executive_When_Cumulative_Exceeds_10_Percent_Through_Real_Http()
    {
        var (tenantId, emailDomain) = SeedIsolatedTenant(UserRole.QS, UserRole.PM, UserRole.ProjectDirector, UserRole.Executive);
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, $"qs@{emailDomain}");
        Authorize(client, qs.AccessToken);

        // An isolated project (its own brand-new tenant, never touched by any other test in this
        // class) + a project-scoped escalation policy - THE real-HTTP gap qa-engineer found: no HTTP
        // test anywhere seeded a cumulative-VO-escalation policy at all.
        var effectiveFrom = DateTimeOffset.Parse("2025-01-01T00:00:00+07:00");
        var projectId = SeedIsolatedProject(tenantId, bac: 485_000_000.00m, originalContractValue: 485_000_000.00m);
        SeedEscalationPolicy(tenantId, projectId, effectiveFrom);

        // Sigma_prior = 46,000,000.00 - itself >= 5,000,000.00, so [PM, ProjectDirector, Executive] by
        // BAND ALONE (independent of escalation). Fully approved through real HTTP first.
        var priorActivityId = SeedActivity(tenantId, projectId, 900_000_000.00m);
        var priorVoId = await CreateAndSubmitAsync(client, projectId, 46_000_000.00m, priorActivityId);
        var priorFinalStatus = await ApproveFullChainAsync(
            client, priorVoId, emailDomain, [UserRole.PM, UserRole.ProjectDirector, UserRole.Executive]);
        Assert.Equal("Approved", priorFinalStatus);

        // ApproveFullChainAsync leaves the shared client authenticated as its LAST approver
        // (Executive here) - re-authenticate as QS (a VoCrudRoles member) before creating the next VO.
        Authorize(client, qs.AccessToken);

        // 3,200,000.00 alone sits in the [500k, 5M) band -> [PM, ProjectDirector] (2 steps) by band.
        // The third step below can only come from escalation:
        // (46,000,000 + 3,200,000) / 485,000,000 = 10.1443% > 10.00%.
        var activityId = SeedActivity(tenantId, projectId, 900_000_000.00m);
        var createResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/variation-orders",
            new
            {
                VoNumber = $"VO-{Guid.NewGuid():N}",
                Amount = 3_200_000.00m,
                TimeImpactDays = 0,
                ScopeItems = new[] { new { ActivityId = activityId, BudgetCostDelta = 3_200_000.00m } },
            });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        var submitResponse = await client.PostAsync($"/api/v1/variation-orders/{created!.Id}/submit", content: null);

        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal(3, submitted!.TotalSteps);
        Assert.Equal(nameof(UserRole.Executive), submitted.ApprovalSteps[^1].RequiredRole);

        // H-01 inheritance (domain-rules.md §3.4's own warning): the escalated Executive step - a
        // SYNTHESISED rung with no real ApprovalPolicyRule row behind it - must be fully APPROVABLE
        // through the real HTTP/handler path, not merely present in the snapshot.
        var finalStatus = await ApproveFullChainAsync(
            client, created.Id, emailDomain, [UserRole.PM, UserRole.ProjectDirector, UserRole.Executive]);
        Assert.Equal("Approved", finalStatus);
    }

    [Fact]
    public async Task R5_Below_The_Lowest_Configured_Band_Fails_Closed_With_422_ApprovalPolicyGap_Through_Real_Http_And_Stays_Draft()
    {
        var (tenantId, emailDomain) = SeedIsolatedTenant(UserRole.QS, UserRole.PM);
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, $"qs@{emailDomain}");
        Authorize(client, qs.AccessToken);

        var effectiveFrom = DateTimeOffset.Parse("2025-01-01T00:00:00+07:00");
        var projectId = SeedIsolatedProject(tenantId, bac: 1_000_000.00m);
        SeedGapPolicy(tenantId, projectId, effectiveFrom); // TH-Gap-VO: nothing covers [0, 100,000).
        var activityId = SeedActivity(tenantId, projectId, 1_000_000.00m);

        var createResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/variation-orders",
            new
            {
                VoNumber = $"VO-{Guid.NewGuid():N}",
                Amount = 50_000.00m,
                TimeImpactDays = 0,
                ScopeItems = new[] { new { ActivityId = activityId, BudgetCostDelta = 50_000.00m } },
            });
        var created = await createResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal("Draft", created!.Status);

        var submitResponse = await client.PostAsync($"/api/v1/variation-orders/{created.Id}/submit", content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, submitResponse.StatusCode);
        var problem = await submitResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/approval-policy-gap", problem!.Type);
        Assert.Equal("ApprovalPolicyGap", problem.Detail);

        var getResponse = await client.GetAsync($"/api/v1/variation-orders/{created.Id}");
        var persisted = await getResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal("Draft", persisted!.Status); // never auto-approved.
        Assert.Empty(persisted.ApprovalSteps);
    }

    [Fact]
    public async Task R6_Boundary_Exactly_500000_Belongs_To_The_Upper_Band_Through_Real_Http()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);
        var projectId = await GetSeededProjectIdAsync(qs.TenantId);
        var activityId = SeedActivity(qs.TenantId, projectId, 5_000_000.00m);

        var createResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/variation-orders",
            new
            {
                VoNumber = $"VO-{Guid.NewGuid():N}",
                Amount = 500_000.00m,
                TimeImpactDays = 0,
                ScopeItems = new[] { new { ActivityId = activityId, BudgetCostDelta = 500_000.00m } },
            });
        var created = await createResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);

        var submitResponse = await client.PostAsync($"/api/v1/variation-orders/{created!.Id}/submit", content: null);

        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal(2, submitted!.TotalSteps);
        Assert.Equal([nameof(UserRole.PM), nameof(UserRole.ProjectDirector)], submitted.ApprovalSteps.Select(s => s.RequiredRole));
    }

    /// <summary>
    /// V-6, the escalation-bypass race (domain-rules.md §4.7/§4.8) - through real HTTP. Two VOs each
    /// submitted under-threshold at their own submission can be independently approved and cross the
    /// cumulative threshold with no Executive signature anywhere UNLESS the final-approval guard
    /// (<c>ApproveVariationOrderCommandHandler.CheckEscalationBypassAsync</c>) closes it. Added
    /// specifically because R4 alone does not exercise this guard: R4's escalated VO already carries
    /// the Executive step from Submit, so its final approval finds the escalation role already
    /// present in the snapshotted chain and the re-check is a no-op by construction (domain-rules.md
    /// §4.6's "r already in the banded chain -> no-op" row) - only a genuine bypass race like this one
    /// reaches the blocking branch.
    /// </summary>
    [Fact]
    public async Task V6_The_Escalation_Bypass_Race_Is_Closed_By_The_Final_Approval_Guard_Through_Real_Http()
    {
        var (tenantId, emailDomain) = SeedIsolatedTenant(UserRole.QS, UserRole.PM, UserRole.ProjectDirector, UserRole.Executive);
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, $"qs@{emailDomain}");
        Authorize(client, qs.AccessToken);

        var effectiveFrom = DateTimeOffset.Parse("2025-01-01T00:00:00+07:00");
        var projectId = SeedIsolatedProject(tenantId, bac: 485_000_000.00m, originalContractValue: 485_000_000.00m);
        SeedEscalationPolicy(tenantId, projectId, effectiveFrom);

        // Sigma_prior = 44,000,000.00 - itself >= 5M, so [PM, ProjectDirector, Executive] by band alone.
        var baseActivityId = SeedActivity(tenantId, projectId, 900_000_000.00m);
        var baseVoId = await CreateAndSubmitAsync(client, projectId, 44_000_000.00m, baseActivityId);
        var baseFinalStatus = await ApproveFullChainAsync(
            client, baseVoId, emailDomain, [UserRole.PM, UserRole.ProjectDirector, UserRole.Executive]);
        Assert.Equal("Approved", baseFinalStatus);

        // ApproveFullChainAsync leaves the shared client authenticated as its LAST approver
        // (Executive here) - re-authenticate as QS (a VoCrudRoles member) before creating VO-A/VO-B.
        Authorize(client, qs.AccessToken);

        // VO-A +2,400,000.00: (44,000,000+2,400,000)/485,000,000 = 9.5670% -> no escalation, 2 steps.
        var voAActivityId = SeedActivity(tenantId, projectId, 900_000_000.00m);
        var voAId = await CreateAndSubmitAsync(client, projectId, 2_400_000.00m, voAActivityId);

        // VO-B +2,300,000.00: (44,000,000+2,300,000)/485,000,000 = 9.5464% -> no escalation, 2 steps.
        var voBActivityId = SeedActivity(tenantId, projectId, 900_000_000.00m);
        var voBId = await CreateAndSubmitAsync(client, projectId, 2_300_000.00m, voBActivityId);

        // VO-A fully approved -> Sigma^VO = 46,400,000.00.
        var voAFinalStatus = await ApproveFullChainAsync(client, voAId, emailDomain, [UserRole.PM, UserRole.ProjectDirector]);
        Assert.Equal("Approved", voAFinalStatus);

        // VO-B: PM approves (step 1 -> 2) - not checked at non-final steps.
        var pm = await LoginAsync(client, $"pm@{emailDomain}");
        Authorize(client, pm.AccessToken);
        var voBAfterPm = await client.PostAsJsonAsync($"/api/v1/variation-orders/{voBId}/approve", new { Comment = (string?)null });
        Assert.Equal(HttpStatusCode.OK, voBAfterPm.StatusCode);

        // VO-B: ProjectDirector approves - the FINAL step. Re-check:
        // (46,400,000 + 2,300,000) / 485,000,000 = 10.0412% > 10.00% -> BLOCKED, no Executive
        // signature exists anywhere in the audit trail.
        var director = await LoginAsync(client, $"projectdirector@{emailDomain}");
        Authorize(client, director.AccessToken);
        var voBFinalAttempt = await client.PostAsJsonAsync($"/api/v1/variation-orders/{voBId}/approve", new { Comment = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, voBFinalAttempt.StatusCode);
        var problem = await voBFinalAttempt.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("https://cmplus.dev/problems/vo-escalation-threshold-crossed-since-submission", problem!.Type);

        var voBAfterBlockResponse = await client.GetAsync($"/api/v1/variation-orders/{voBId}");
        var voBAfterBlock = await voBAfterBlockResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        Assert.Equal("PendingApproval", voBAfterBlock!.Status); // stays PendingApproval 2/2 - never advanced.
        Assert.Equal(2, voBAfterBlock.CurrentStepNo);
        Assert.Equal(2, voBAfterBlock.TotalSteps);
        Assert.DoesNotContain(voBAfterBlock.ApprovalSteps, s => s.RequiredRole == nameof(UserRole.Executive)); // chain never mutated in place.
    }

    [Fact]
    public async Task GetById_And_List_Round_Trip_Through_Real_Http()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@bkk-infra.dev");
        Authorize(client, qs.AccessToken);
        var projectId = await GetSeededProjectIdAsync(qs.TenantId);
        var activityId = SeedActivity(qs.TenantId, projectId, 2_000_000.00m);

        var createResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/variation-orders",
            new
            {
                VoNumber = $"VO-{Guid.NewGuid():N}",
                Amount = 150_000.00m,
                TimeImpactDays = 0,
                ScopeItems = new[] { new { ActivityId = activityId, BudgetCostDelta = 150_000.00m } },
            });
        var created = await createResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);

        var getResponse = await client.GetAsync($"/api/v1/variation-orders/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var listResponse = await client.GetAsync($"/api/v1/projects/{projectId}/variation-orders");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<VoResponse>>(ResponseJsonOptions);
        Assert.Contains(list!, v => v.Id == created.Id);
    }

    /// <summary>Creates a submitted VO as QS and returns its id, for the read-access tests below.</summary>
    private async Task<Guid> CreateSubmittedVoAsync(HttpClient client)
    {
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);
        var projectId = await GetSeededProjectIdAsync(qs.TenantId);
        var activityId = SeedActivity(qs.TenantId, projectId, 5_000_000.00m);

        var createResponse = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/variation-orders",
            new
            {
                VoNumber = $"VO-{Guid.NewGuid():N}",
                Description = "Additional works",
                Justification = "Site instruction",
                Amount = 300_000.00m,
                TimeImpactDays = 0,
                ScopeItems = new[] { new { ActivityId = activityId, BudgetCostDelta = 300_000.00m } },
            });
        var created = await createResponse.Content.ReadFromJsonAsync<VoResponse>(ResponseJsonOptions);
        await client.PostAsync($"/api/v1/variation-orders/{created!.Id}/submit", content: null);
        return created.Id;
    }

    /// <summary>
    /// An <c>Executive</c> is not a VO author, so they are absent from <c>VoCrudRoles</c> - but
    /// cumulative-VO escalation (ADR-0015) can add them to a VO's chain as its final approver. Before
    /// <c>VoReadRoles</c> existed they got 403 on <c>GET</c> for the very document they were being
    /// asked to sign, while <c>approve</c> (which carries no static role gate, by design) let them
    /// through - i.e. the mechanism invited blind approval. Both halves are asserted: reads open,
    /// writes still closed.
    /// </summary>
    [Fact]
    public async Task An_Executive_Can_Read_A_Vo_They_May_Be_Escalated_To_Approve()
    {
        using var client = _factory.CreateClient();
        var voId = await CreateSubmittedVoAsync(client);

        var executive = await LoginAsync(client, "executive@siam-construction.dev");
        Authorize(client, executive.AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/variation-orders/{voId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/variation-orders/{voId}/approval-actions")).StatusCode);
    }

    [Fact]
    public async Task An_Executive_Still_Cannot_Raise_Or_Alter_A_Vo_Read_Access_Is_Not_Write_Access()
    {
        using var client = _factory.CreateClient();
        var voId = await CreateSubmittedVoAsync(client);

        var executive = await LoginAsync(client, "executive@siam-construction.dev");
        Authorize(client, executive.AccessToken);

        var create = await client.PostAsJsonAsync($"/api/v1/projects/{Guid.NewGuid()}/variation-orders",
            new { VoNumber = "VO-X", Amount = 100_000.00m, TimeImpactDays = 0, ScopeItems = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/v1/variation-orders/{voId}/submit", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/v1/variation-orders/{voId}/withdraw", content: null)).StatusCode);
    }

    /// <summary>
    /// <c>GetApprovalActionHistoryQueryHandler</c>'s existence-check switch had no
    /// <c>VariationOrder</c> arm, so this endpoint returned 404 <b>unconditionally</b> even though the
    /// route, controller and DTO all shipped - a fail-closed default that is indistinguishable from a
    /// legitimate 404, which is exactly why no backend test caught it. Asserting a real 200 with the
    /// submit action present is what pins the arm in place.
    /// </summary>
    [Fact]
    public async Task A_Submitted_Vos_Approval_History_Is_Actually_Returned_Not_An_Unconditional_404()
    {
        using var client = _factory.CreateClient();
        var voId = await CreateSubmittedVoAsync(client);

        var response = await client.GetAsync($"/api/v1/variation-orders/{voId}/approval-actions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = await response.Content.ReadFromJsonAsync<List<JsonElement>>(ResponseJsonOptions);
        Assert.NotEmpty(history!);
    }

    [Fact]
    public async Task An_Unknown_Vo_Id_Still_Returns_404_For_Approval_History_The_Arm_Did_Not_Open_A_Leak()
    {
        using var client = _factory.CreateClient();
        var qs = await LoginAsync(client, "qs@siam-construction.dev");
        Authorize(client, qs.AccessToken);

        var response = await client.GetAsync($"/api/v1/variation-orders/{Guid.NewGuid()}/approval-actions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
