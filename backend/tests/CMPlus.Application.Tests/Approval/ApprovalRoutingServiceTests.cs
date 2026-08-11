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

    private ApprovalRoutingRequest VoRequest(
        ApprovalPolicy policy, decimal amount, decimal? escalationBaselineContractValue = null, decimal? cumulativeBefore = null) =>
        new(ApprovalDocumentType.VariationOrder, ProjectId: null, amount, SubmittedAt, [policy], escalationBaselineContractValue, cumulativeBefore);

    private ApprovalRoutingRequest IpcRequest(ApprovalPolicy policy, decimal grossCertified) =>
        new(ApprovalDocumentType.PaymentCertificate, ProjectId: null, grossCertified, SubmittedAt, [policy]);

    [Fact]
    public void R1_Add_2_4M_With_Low_Cumulative_Resolves_PM_Then_ProjectDirector_No_Escalation()
    {
        var fixture = Fixture("R1");
        var policy = BuildPolicy("TH-Default-VO", ApprovalDocumentType.VariationOrder);
        var amount = fixture.GetProperty("inputs").GetProperty("amount").GetDecimal();
        var cumulativeBefore = fixture.GetProperty("inputs").GetProperty("cumulativeApprovedVoBefore").GetDecimal();

        var result = _service.Resolve(VoRequest(policy, amount, escalationBaselineContractValue: 485_000_000.00m, cumulativeBefore));

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

        // domain-rules.md §3.4: "Cumulative approved VOs before submission Sigma_prior = 6,000,000.00
        // for R1, R2, R3, R6" - TH-Default-VO carries a configured CumulativeVoEscalationPct (10.00),
        // so a baseline must always be supplied once a threshold is configured (S10-BE-02 fix,
        // ApprovalErrorCodes.ContractValueNotConfigured) even for a fixture whose point is band
        // resolution, not escalation.
        var result = _service.Resolve(VoRequest(policy, amount, escalationBaselineContractValue: 485_000_000.00m, cumulativeBefore: 6_000_000.00m));

        Assert.True(result.IsSuccess);
        Assert.Equal(800_000.00m, result.Value.RoutingAmount);
        Assert.Equal(ExpectedChain(fixture), result.Value.Steps.Select(s => s.RequiredRole).ToList());

        // R2 is the load-bearing fixture (domain-rules.md §3.4): three assertions, not one. (i) A^route
        // is positive 800,000.00 (already asserted above); (ii) the chain is byte-identical to R2's twin
        // +800,000.00 Add control; (iii) the persisted... well, ApprovalRoutingService.Resolve never
        // touches the VariationOrder aggregate at all (it is a pure, EF-free routing computation over
        // plain inputs), so there is no "Amount" field here to silently overwrite in the first place -
        // that specific risk (Math.Abs written back onto the aggregate) can only be introduced later, at
        // the command-handler boundary that calls Resolve and then calls VariationOrder.Submit with the
        // ORIGINAL signed amount. That full, three-assertion proof - including the persisted Amount
        // staying negative - lives in SubmitVariationOrderCommandHandlerTests (S10-BE-02) against the
        // real handler, not here.
        var r2PrimeAmount = 800_000.00m; // R2': twin control, Add +800,000.00 (domain-rules.md §3.4)
        var r2PrimeResult = _service.Resolve(VoRequest(policy, r2PrimeAmount, escalationBaselineContractValue: 485_000_000.00m, cumulativeBefore: 6_000_000.00m));
        Assert.True(r2PrimeResult.IsSuccess);
        Assert.Equal(result.Value.Steps.Select(s => s.RequiredRole).ToList(), r2PrimeResult.Value.Steps.Select(s => s.RequiredRole).ToList());
        Assert.Equal(result.Value.RoutingAmount, r2PrimeResult.Value.RoutingAmount);
    }

    [Fact]
    public void R3_Add_300k_Resolves_Single_Step_PM()
    {
        var fixture = Fixture("R3");
        var policy = BuildPolicy("TH-Default-VO", ApprovalDocumentType.VariationOrder);
        var amount = fixture.GetProperty("inputs").GetProperty("amount").GetDecimal();

        var result = _service.Resolve(VoRequest(policy, amount, escalationBaselineContractValue: 485_000_000.00m, cumulativeBefore: 6_000_000.00m));

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

        var result = _service.Resolve(VoRequest(policy, amount, escalationBaselineContractValue: 485_000_000.00m, cumulativeBefore));

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

        var result = _service.Resolve(VoRequest(policy, amount, escalationBaselineContractValue: 485_000_000.00m, cumulativeBefore: 6_000_000.00m));

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

    // ---- ADR-0015: the escalation denominator is the baseline contract value, never the current
    // (VO-inclusive) one - the shipped ApprovalRoutingService.cs:66-70 self-dilution defect ----

    [Fact]
    public void R4_Self_Dilution_Counterfactual_Baseline_And_Current_Denominators_Produce_Different_Chains_On_Identical_Data()
    {
        // Reuses R4's exact numerator inputs verbatim from the fixture file (never hand-copied) and
        // proves the fix by constructing BOTH readings of the denominator from the same underlying
        // facts (domain-rules.md §4.3's own counterfactual table):
        //   - Corig = 485,000,000.00 (the fixture's stated ContractValue, ruled to be the ORIGINAL
        //     value - Reading A - the only reading that reproduces both R4's chain and its stated
        //     10.14%).
        //   - Ccur  = Corig + cumulativeApprovedVoBefore = 531,000,000.00 (the CURRENT contract sum:
        //     531M reflects the 46,000,000.00 of VOs already approved BEFORE this submission; the VO
        //     being submitted now is not yet approved, so it does not further inflate Ccur).
        // The OLD shipped code passed Ccur (Project.ContractValue) as the denominator; the FIX passes
        // Corig (Project.EscalationBaselineContractValue). Same policy, same amount, same cumulative -
        // only the denominator differs - and the two runs disagree on whether escalation fires at all.
        var fixture = Fixture("R4");
        var policy = BuildPolicy("TH-Default-VO", ApprovalDocumentType.VariationOrder);
        var amount = fixture.GetProperty("inputs").GetProperty("amount").GetDecimal(); // 3,200,000.00
        var cumulativeBefore = fixture.GetProperty("inputs").GetProperty("cumulativeApprovedVoBefore").GetDecimal(); // 46,000,000.00
        const decimal originalContractValue = 485_000_000.00m;
        var currentContractValue = originalContractValue + cumulativeBefore; // 531,000,000.00

        var newFixedResult = _service.Resolve(VoRequest(policy, amount, originalContractValue, cumulativeBefore));
        var oldBuggyResult = _service.Resolve(VoRequest(policy, amount, currentContractValue, cumulativeBefore));

        Assert.True(newFixedResult.IsSuccess);
        Assert.True(oldBuggyResult.IsSuccess);

        // NEW (fixed): 49,200,000.00 / 485,000,000.00 = 10.1443...% > 10.00% -> escalates.
        Assert.True(newFixedResult.Value.EscalationApplied);
        Assert.Equal(3, newFixedResult.Value.Steps.Count);
        Assert.Equal(UserRole.Executive, newFixedResult.Value.Steps[^1].RequiredRole);

        // OLD (buggy, self-diluting): 49,200,000.00 / 531,000,000.00 = 9.2655...% < 10.00% -> the
        // exact self-dilution failure mode ADR-0015 fixes - the SAME governance-crossing VO reads as
        // "no escalation needed" purely because the denominator grew to include prior approvals.
        Assert.False(oldBuggyResult.Value.EscalationApplied);
        Assert.Equal(2, oldBuggyResult.Value.Steps.Count);
        Assert.DoesNotContain(oldBuggyResult.Value.Steps, s => s.RequiredRole == UserRole.Executive);

        // Same routing amount and same band-only chain either way - only the escalation verdict
        // diverges, proving the denominator (not the band resolution) is what changed.
        Assert.Equal(newFixedResult.Value.RoutingAmount, oldBuggyResult.Value.RoutingAmount);
    }

    [Fact]
    public void Null_CumulativeVoEscalationPct_Skips_Escalation_Entirely_Never_Treated_As_Zero()
    {
        // ADR-0015: NULL means "no escalation configured" and must never be read as "0" by any
        // consumer - 0 would escalate EVERY VO (any positive cumulative ratio exceeds a 0% threshold),
        // which is the specific failure mode this test exists to catch. A mutant that replaced
        // `policy.CumulativeVoEscalationPct is { } thresholdPct` with
        // `(policy.CumulativeVoEscalationPct ?? 0m)` would escalate here; the real, correct behaviour
        // must not.
        var policy = ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), projectId: null, ApprovalDocumentType.VariationOrder, SubmittedAt.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, 0m, null, UserRole.PM)],
            allowSelfApproval: false,
            cumulativeVoEscalationPct: null, // not configured
            cumulativeVoEscalationRole: UserRole.Executive);

        // A deliberately large ratio (10,000.00 / 1.00 = 1,000,000%) so any "null treated as 0"
        // mutant would certainly escalate - this is not a boundary case, it is nowhere near one.
        var request = new ApprovalRoutingRequest(
            ApprovalDocumentType.VariationOrder, ProjectId: null, Amount: 10_000.00m, SubmittedAt,
            CandidatePolicies: [policy], EscalationBaselineContractValue: 1.00m, CumulativeApprovedVoAmount: 0m);

        var result = _service.Resolve(request);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.EscalationApplied);
        Assert.DoesNotContain(result.Value.Steps, s => s.RequiredRole == UserRole.Executive);
        Assert.Single(result.Value.Steps); // band-only chain, unmodified
    }

    [Fact]
    public void Numerator_Is_Net_Signed_Not_Gross_Absolute_A_Deduct_Can_Prevent_Escalation_That_Abs_Would_Trigger()
    {
        // domain-rules.md §4.3: "net-signed-over-baseline is the only combination reproducing both"
        // R4's stated chain and its 10.14%. This test isolates the numerator half of that claim: a
        // Deduct VO must be able to keep the ratio UNDER threshold precisely because it is subtracted,
        // not added as if it were an Add of the same magnitude (gross/absolute, N-2). Chosen so the
        // two readings disagree on the escalation verdict itself, not merely on the ratio's size:
        //   net-signed: (48,000,000.00 + (-3,000,000.00)) / 485,000,000.00 =  9.2783...% -> no escalation
        //   gross/abs:  (48,000,000.00 +   3,000,000.00 ) / 485,000,000.00 = 10.5155...% -> escalation
        var policy = BuildPolicy("TH-Default-VO", ApprovalDocumentType.VariationOrder);
        const decimal deductAmount = -3_000_000.00m; // VO-type Deduct
        const decimal cumulativeBefore = 48_000_000.00m;

        var result = _service.Resolve(VoRequest(policy, deductAmount, 485_000_000.00m, cumulativeBefore));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.EscalationApplied);
        Assert.DoesNotContain(result.Value.Steps, s => s.RequiredRole == UserRole.Executive);
        // And routing itself still correctly used the absolute value for BAND selection (R2's own
        // point) - the two abs()/signed rules apply to two different questions, per domain-rules.md §3.2.
        Assert.Equal(3_000_000.00m, result.Value.RoutingAmount);
    }

    [Fact]
    public void Missing_EscalationBaselineContractValue_Fails_Closed_With_ContractValueNotConfigured_Rather_Than_Dividing_By_Null()
    {
        // S10-BE-02 / domain-rules.md §4.6: this test's own name and expectation flip today
        // (2026-08-10) - it used to assert a silent "skip escalation" degrade, which is exactly the
        // ApprovalRoutingService.cs:66 defect domain-rules.md §4.6 identifies and this sprint's DoD
        // assigns to be fixed here. An escalation-configured policy with no usable baseline (missing,
        // or <= 0) must now fail closed with 422 ContractValueNotConfigured - "never divide, and never
        // silently skip" - never a quiet success that lets a misconfigured project's VOs bypass
        // governance entirely.
        var policy = ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), projectId: null, ApprovalDocumentType.VariationOrder, SubmittedAt.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, 0m, null, UserRole.PM)],
            allowSelfApproval: false,
            cumulativeVoEscalationPct: 10.00m,
            cumulativeVoEscalationRole: UserRole.Executive);

        var request = new ApprovalRoutingRequest(
            ApprovalDocumentType.VariationOrder, ProjectId: null, Amount: 10_000.00m, SubmittedAt,
            CandidatePolicies: [policy]); // EscalationBaselineContractValue omitted (null default)

        var result = _service.Resolve(request);

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalErrorCodes.ContractValueNotConfigured, result.Error);
    }

    [Fact]
    public void Zero_Or_Negative_EscalationBaselineContractValue_Also_Fails_Closed_With_ContractValueNotConfigured()
    {
        // domain-rules.md §4.6's literal condition is "C^esc <= 0", not merely "missing" - a project
        // whose ContractValue/OriginalContractValue was left at its zero default (never yet configured)
        // must fail exactly the same way as a caller that omitted the parameter entirely.
        var policy = ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), projectId: null, ApprovalDocumentType.VariationOrder, SubmittedAt.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, 0m, null, UserRole.PM)],
            allowSelfApproval: false,
            cumulativeVoEscalationPct: 10.00m,
            cumulativeVoEscalationRole: UserRole.Executive);

        var request = new ApprovalRoutingRequest(
            ApprovalDocumentType.VariationOrder, ProjectId: null, Amount: 10_000.00m, SubmittedAt,
            CandidatePolicies: [policy], EscalationBaselineContractValue: 0.00m);

        var result = _service.Resolve(request);

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalErrorCodes.ContractValueNotConfigured, result.Error);
    }

    [Fact]
    public void V5a_Boundary_Exactly_At_Threshold_Does_Not_Escalate_Strict_Greater_Than()
    {
        // domain-rules.md §4.8 V-5a: Sigma_prior=45,300,000.00, VO +3,200,000.00 -> numerator
        // 48,500,000.00 -> Phi = 48,500,000/485,000,000 = EXACTLY 10.000000% -> NOT > 10.00 -> no
        // escalation, chain stays the 2-step band-only chain.
        var policy = BuildPolicy("TH-Default-VO", ApprovalDocumentType.VariationOrder);

        var result = _service.Resolve(VoRequest(policy, 3_200_000.00m, 485_000_000.00m, 45_300_000.00m));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.EscalationApplied);
        Assert.Equal(2, result.Value.Steps.Count);
        Assert.DoesNotContain(result.Value.Steps, s => s.RequiredRole == UserRole.Executive);
    }

    [Fact]
    public void V5b_Rounding_Must_Not_Decide_Unrounded_10_004_Percent_Still_Escalates()
    {
        // domain-rules.md §4.8 V-5b: Sigma_prior=45,319,400.00, VO +3,200,000.00 -> numerator
        // 48,519,400.00 -> Phi = 10.004000% (unrounded) -> escalates. An implementation that rounds
        // Phi to decimal(5,2) before comparing would see 10.00 and wrongly NOT escalate - this fixture
        // exists solely to catch that mutant.
        var policy = BuildPolicy("TH-Default-VO", ApprovalDocumentType.VariationOrder);

        var result = _service.Resolve(VoRequest(policy, 3_200_000.00m, 485_000_000.00m, 45_319_400.00m));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.EscalationApplied);
        Assert.Equal(3, result.Value.Steps.Count);
        Assert.Equal(UserRole.Executive, result.Value.Steps[^1].RequiredRole);
    }
}
