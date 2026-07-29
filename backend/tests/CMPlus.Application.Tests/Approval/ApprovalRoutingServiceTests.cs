using System.Text.Json;
using CMPlus.Application.Approval;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Approval;

/// <summary>
/// S2-BE-05: exercises <see cref="ApprovalRoutingService"/> against the R1-R10 fixtures from
/// approval-workflow.md §8, loaded directly from
/// <c>tests/CMPlus.Application.Tests/Fixtures/approval-routing-fixtures.json</c> (not
/// hand-duplicated) so the test can never silently drift from the source-of-truth fixture file.
///
/// R9 and R10 each blend a pure routing assertion with a state-machine/actor concern
/// (revision-voiding and self-approval rejection respectively) that belongs to the VO/Payment
/// Certificate transition handlers of Sprint 9/10, not to this Sprint 2 pure routing engine
/// (docs/10 risk R-08: Sprint 2's acceptance is routing fixtures only, "no state machine needed").
/// Both fixtures are still exercised in full here for their *routing* half - R9's re-resolution at
/// the revised amount, R10's chain being identical to R8 - with the out-of-scope half documented
/// inline rather than silently skipped.
/// </summary>
public class ApprovalRoutingServiceTests
{
    private static readonly JsonElement FixtureRoot = LoadFixtureRoot();
    private static readonly DateTimeOffset SubmittedAt = new(2026, 7, 28, 0, 0, 0, TimeSpan.FromHours(7));

    private readonly ApprovalRoutingService _service = new();

    private static JsonElement LoadFixtureRoot()
    {
        var path = SolutionRelativePath(Path.Combine(
            "tests", "CMPlus.Application.Tests", "Fixtures", "approval-routing-fixtures.json"));
        var json = File.ReadAllText(path);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static string SolutionRelativePath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CMPlus.sln")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? throw new InvalidOperationException("Could not locate CMPlus.sln from the test output directory.")
            : Path.Combine(dir.FullName, relative);
    }

    private static readonly IReadOnlyDictionary<string, UserRole> RoleNameMap = new Dictionary<string, UserRole>
    {
        ["ProjectManager"] = UserRole.PM,
        ["ProjectDirector"] = UserRole.ProjectDirector,
        ["Executive"] = UserRole.Executive,
        ["QS"] = UserRole.QS,
    };

    private static JsonElement Fixture(string fixtureId) =>
        FixtureRoot.GetProperty("fixtures").EnumerateArray()
            .Single(f => f.GetProperty("fixtureId").GetString() == fixtureId);

    /// <summary>Builds the policy aggregate straight from the fixture file's own
    /// <c>ruleTableVerbatim</c> - the test never hand-copies the rule bands.</summary>
    private static ApprovalPolicy BuildPolicy(string policyName, ApprovalDocumentType documentType)
    {
        var policyElement = FixtureRoot.GetProperty("policies").GetProperty(policyName);

        var rules = policyElement.GetProperty("ruleTableVerbatim").EnumerateArray()
            .Select(r => new ApprovalPolicyRuleInput(
                StepNo: r.GetProperty("stepNo").GetInt32(),
                MinAmount: r.GetProperty("minAmount").GetDecimal(),
                MaxAmount: r.GetProperty("maxAmount").ValueKind == JsonValueKind.Null ? null : r.GetProperty("maxAmount").GetDecimal(),
                RequiredRole: RoleNameMap[r.GetProperty("requiredRole").GetString()!]))
            .ToList();

        decimal? escalationPct = policyElement.TryGetProperty("cumulativeVoEscalationPct", out var pctEl) ? pctEl.GetDecimal() : null;
        UserRole? escalationRole = policyElement.TryGetProperty("cumulativeVoEscalationRole", out var roleEl)
            ? RoleNameMap[roleEl.GetString()!]
            : null;

        return ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), projectId: null, documentType, SubmittedAt.AddYears(-1), rules,
            allowSelfApproval: false, escalationPct, escalationRole);
    }

    private static IReadOnlyList<UserRole> ExpectedChain(JsonElement fixture, string propertyName = "chain") =>
        fixture.GetProperty("expected").GetProperty(propertyName).EnumerateArray()
            .Select(r => RoleNameMap[r.GetString()!])
            .ToList();

    private ApprovalRoutingRequest VoRequest(ApprovalPolicy policy, decimal amount, decimal? contractValue = null, decimal? cumulativeBefore = null) =>
        new(ApprovalDocumentType.VariationOrder, ProjectId: null, amount, SubmittedAt, [policy], contractValue, cumulativeBefore);

    private ApprovalRoutingRequest IpcRequest(ApprovalPolicy policy, decimal grossCertified) =>
        new(ApprovalDocumentType.PaymentCertificate, ProjectId: null, grossCertified, SubmittedAt, [policy]);

    [Fact]
    public void R1_Add_2_4M_With_Low_Cumulative_Resolves_PM_Then_ProjectDirector_No_Escalation()
    {
        var fixture = Fixture("R1");
        var policy = BuildPolicy("TH-Default-VO", ApprovalDocumentType.VariationOrder);
        var amount = fixture.GetProperty("inputs").GetProperty("amount").GetDecimal();
        var cumulativeBefore = fixture.GetProperty("inputs").GetProperty("cumulativeApprovedVoBefore").GetDecimal();

        var result = _service.Resolve(VoRequest(policy, amount, contractValue: 485_000_000.00m, cumulativeBefore));

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.GetProperty("expected").GetProperty("aRoute").GetDecimal(), result.Value.RoutingAmount);
        Assert.Equal(ExpectedChain(fixture), result.Value.Steps.Select(s => s.RequiredRole).ToList());
        Assert.False(result.Value.EscalationApplied);
    }

    [Fact]
    public void R2_Deduct_800k_Routes_On_Absolute_Value()
    {
        var fixture = Fixture("R2");
        var policy = BuildPolicy("TH-Default-VO", ApprovalDocumentType.VariationOrder);
        var amount = fixture.GetProperty("inputs").GetProperty("amount").GetDecimal(); // -800000.00

        var result = _service.Resolve(VoRequest(policy, amount));

        Assert.True(result.IsSuccess);
        Assert.Equal(800_000.00m, result.Value.RoutingAmount);
        Assert.Equal(ExpectedChain(fixture), result.Value.Steps.Select(s => s.RequiredRole).ToList());
    }

    [Fact]
    public void R3_Add_300k_Resolves_Single_Step_PM()
    {
        var fixture = Fixture("R3");
        var policy = BuildPolicy("TH-Default-VO", ApprovalDocumentType.VariationOrder);
        var amount = fixture.GetProperty("inputs").GetProperty("amount").GetDecimal();

        var result = _service.Resolve(VoRequest(policy, amount));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExpectedChain(fixture), result.Value.Steps.Select(s => s.RequiredRole).ToList());
        Assert.Single(result.Value.Steps);
    }

    [Fact]
    public void R4_Escalation_Appends_Executive_When_Cumulative_Exceeds_10_Percent()
    {
        var fixture = Fixture("R4");
        var policy = BuildPolicy("TH-Default-VO", ApprovalDocumentType.VariationOrder);
        var amount = fixture.GetProperty("inputs").GetProperty("amount").GetDecimal();
        var cumulativeBefore = fixture.GetProperty("inputs").GetProperty("cumulativeApprovedVoBefore").GetDecimal();

        var result = _service.Resolve(VoRequest(policy, amount, contractValue: 485_000_000.00m, cumulativeBefore));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.EscalationApplied);
        Assert.Equal(ExpectedChain(fixture, "finalChain"), result.Value.Steps.Select(s => s.RequiredRole).ToList());
        Assert.Equal(UserRole.Executive, result.Value.Steps[^1].RequiredRole);
    }

    [Fact]
    public void R5_Amount_Below_The_Lowest_Configured_Band_Fails_Closed_With_ApprovalPolicyGap()
    {
        // The fixture's "hypothetical policy" is a standalone, minimally-configured policy whose
        // lowest MinAmount is 100,000.00 - built directly here (it is not TH-Default-VO and has no
        // ruleTableVerbatim of its own in the fixture file).
        var fixture = Fixture("R5");
        var lowestMin = fixture.GetProperty("inputs").GetProperty("hypotheticalPolicyLowestMinAmount").GetDecimal();
        var policy = ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), null, ApprovalDocumentType.VariationOrder, SubmittedAt.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, lowestMin, null, UserRole.PM)]);
        var amount = fixture.GetProperty("inputs").GetProperty("amount").GetDecimal();

        var result = _service.Resolve(VoRequest(policy, amount));

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalErrorCodes.PolicyGap, result.Error);
        Assert.Equal("ApprovalPolicyGap", fixture.GetProperty("expected").GetProperty("errorType").GetString());
    }

    [Fact]
    public void R6_Boundary_Exactly_500000_Belongs_To_The_Upper_Band_Min_Inclusive_Max_Exclusive()
    {
        var fixture = Fixture("R6");
        var policy = BuildPolicy("TH-Default-VO", ApprovalDocumentType.VariationOrder);
        var amount = fixture.GetProperty("inputs").GetProperty("amount").GetDecimal();

        var result = _service.Resolve(VoRequest(policy, amount));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExpectedChain(fixture), result.Value.Steps.Select(s => s.RequiredRole).ToList());
    }

    [Fact]
    public void R7_Payment_Certificate_Above_ProjectDirector_Threshold_Resolves_Three_Steps()
    {
        var fixture = Fixture("R7");
        var policy = BuildPolicy("TH-Default-IPC", ApprovalDocumentType.PaymentCertificate);
        var gross = fixture.GetProperty("inputs").GetProperty("grossCertifiedAmount_Gk").GetDecimal();

        var result = _service.Resolve(IpcRequest(policy, gross));

        Assert.True(result.IsSuccess);
        Assert.Equal(gross, result.Value.RoutingAmount);
        Assert.Equal(ExpectedChain(fixture), result.Value.Steps.Select(s => s.RequiredRole).ToList());
    }

    [Fact]
    public void R8_Payment_Certificate_Below_ProjectDirector_Threshold_Resolves_Two_Steps()
    {
        var fixture = Fixture("R8");
        var policy = BuildPolicy("TH-Default-IPC", ApprovalDocumentType.PaymentCertificate);
        var gross = fixture.GetProperty("inputs").GetProperty("grossCertifiedAmount_Gk").GetDecimal();

        var result = _service.Resolve(IpcRequest(policy, gross));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExpectedChain(fixture), result.Value.Steps.Select(s => s.RequiredRole).ToList());
    }

    [Fact]
    public void R9_Resubmission_At_A_Lower_Amount_Re_Resolves_And_Drops_ProjectDirector()
    {
        // Routing-only slice of R9: calling Resolve again with the revised amount is exactly the
        // re-resolution approval-workflow.md §5.3 step 8 calls for - no special-case code needed.
        // RevisionNo bumping and voiding the prior QS approval are Sprint 9/10 state-machine
        // concerns, out of scope here (see class remarks).
        var fixture = Fixture("R9");
        var policy = BuildPolicy("TH-Default-IPC", ApprovalDocumentType.PaymentCertificate);
        var resubmittedGross = fixture.GetProperty("inputs").GetProperty("resubmittedGrossCertifiedAmount_Gk").GetDecimal();

        var result = _service.Resolve(IpcRequest(policy, resubmittedGross));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExpectedChain(fixture), result.Value.Steps.Select(s => s.RequiredRole).ToList());
        Assert.DoesNotContain(result.Value.Steps, s => s.RequiredRole == UserRole.ProjectDirector);
    }

    [Fact]
    public void R10_Chain_Resolution_Is_Unaffected_By_Who_Will_Later_Attempt_To_Approve()
    {
        // Routing-only slice of R10: the chain for this amount is identical to R8's - self-approval
        // rejection (SelfApprovalNotPermitted) is an Approve-transition guard over actor identity,
        // which this pure, actor-less routing service does not (and should not) implement; that
        // guard belongs to the Sprint 9/10 Approve command handler (see class remarks).
        var fixture = Fixture("R10");
        var policy = BuildPolicy("TH-Default-IPC", ApprovalDocumentType.PaymentCertificate);
        var gross = fixture.GetProperty("inputs").GetProperty("grossCertifiedAmount_Gk").GetDecimal();

        var result = _service.Resolve(IpcRequest(policy, gross));

        Assert.True(result.IsSuccess);
        Assert.Equal([UserRole.QS, UserRole.PM], result.Value.Steps.Select(s => s.RequiredRole).ToList());
        Assert.Equal("SelfApprovalNotPermitted", fixture.GetProperty("expected").GetProperty("errorType").GetString());
    }

    [Fact]
    public void No_Policy_At_All_Falls_Back_To_A_Single_Mandatory_ProjectDirector_Step()
    {
        var request = new ApprovalRoutingRequest(
            DocumentType: ApprovalDocumentType.VariationOrder, ProjectId: null, Amount: 250_000m,
            SubmittedAt: SubmittedAt, CandidatePolicies: []);

        var result = _service.Resolve(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(Guid.Empty, result.Value.ApprovalPolicyId);
        Assert.Equal([UserRole.ProjectDirector], result.Value.Steps.Select(s => s.RequiredRole).ToList());
    }

    [Fact]
    public void Project_Override_Policy_Is_Preferred_Over_The_Tenant_Default()
    {
        var projectId = Guid.NewGuid();
        var tenantDefault = ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), null, ApprovalDocumentType.VariationOrder, SubmittedAt.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, 0m, null, UserRole.PM)]);
        var projectOverride = ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), projectId, ApprovalDocumentType.VariationOrder, SubmittedAt.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, 0m, null, UserRole.Executive)]);

        var request = new ApprovalRoutingRequest(
            DocumentType: ApprovalDocumentType.VariationOrder, ProjectId: projectId, Amount: 1_000m,
            SubmittedAt: SubmittedAt, CandidatePolicies: [tenantDefault, projectOverride]);

        var result = _service.Resolve(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserRole.Executive, result.Value.Steps.Single().RequiredRole);
    }
}
