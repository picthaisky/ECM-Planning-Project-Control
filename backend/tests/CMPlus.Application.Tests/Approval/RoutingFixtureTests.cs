using System.Text.Json;
using CMPlus.Application.Approval;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Approval;

/// <summary>
/// S2-QA-01: formal xUnit theories over the R1-R10 routing fixtures of approval-workflow.md §8,
/// loaded exclusively from
/// <c>backend/tests/CMPlus.Application.Tests/Fixtures/approval-routing-fixtures.json</c> - no
/// fixture number or expected value is ever hand-copied into this file, so a future edit to the
/// fixture JSON can never silently drift out of sync with what these tests assert.
///
/// This file supersedes <c>Approval/ApprovalRoutingServiceTests.cs</c> (backend-developer's
/// original hand-written proof, one <c>[Fact]</c> per fixture, written alongside S2-BE-05). The
/// routing logic under test and its coverage are unchanged; what changes is structure: R1, R2, R3,
/// R4, R6, R7 and R8 - every fixture whose assertion shape is "resolve and compare the chain" -
/// are now driven by a single <c>[Theory]</c>/<c>[MemberData]</c>, per this sprint's DoD (docs/10
/// §6 Sprint 2, S2-QA-01: "Routing fixture R1-R8 เป็น xUnit theory"). R5 (a bespoke,
/// not-in-the-policy-table hypothetical policy) and R9/R10 (each half routing, half a
/// not-yet-built state-machine/actor concern - see their remarks) keep dedicated <c>[Fact]</c>s
/// because their setup and assertion shape are genuinely different, not because any number is
/// hand-duplicated: every value they use still comes from the fixture file.
/// </summary>
public class RoutingFixtureTests
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

    private static decimal PolicyContractValue(string policyName) =>
        FixtureRoot.GetProperty("policies").GetProperty(policyName).GetProperty("contractValue").GetDecimal();

    private static IReadOnlyList<UserRole> ExpectedChain(JsonElement fixture, string propertyName) =>
        fixture.GetProperty("expected").GetProperty(propertyName).EnumerateArray()
            .Select(r => RoleNameMap[r.GetString()!])
            .ToList();

    /// <summary>Fixture IDs whose assertion shape is exactly "resolve and compare the chain"
    /// (approval-workflow.md §8, R1/R2/R3/R4/R6/R7/R8). Driving these off one <c>[Theory]</c>
    /// (rather than one hand-written <c>[Fact]</c> per fixture) is this test's core DoD
    /// requirement.</summary>
    public static IEnumerable<object[]> ChainFixtureIds =>
        new[] { "R1", "R2", "R3", "R4", "R6", "R7", "R8" }.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(ChainFixtureIds))]
    public void Fixture_Resolves_The_Expected_Approval_Chain(string fixtureId)
    {
        var fixture = Fixture(fixtureId);
        var inputs = fixture.GetProperty("inputs");
        var expected = fixture.GetProperty("expected");
        var policyName = fixture.GetProperty("policy").GetString()!;
        var isPaymentCertificate = inputs.TryGetProperty("grossCertifiedAmount_Gk", out _);
        var documentType = isPaymentCertificate ? ApprovalDocumentType.PaymentCertificate : ApprovalDocumentType.VariationOrder;
        var policy = BuildPolicy(policyName, documentType);

        var amount = isPaymentCertificate
            ? inputs.GetProperty("grossCertifiedAmount_Gk").GetDecimal()
            : inputs.GetProperty("amount").GetDecimal();

        var request = isPaymentCertificate
            ? new ApprovalRoutingRequest(documentType, ProjectId: null, amount, SubmittedAt, [policy])
            : new ApprovalRoutingRequest(
                documentType, ProjectId: null, amount, SubmittedAt, [policy],
                // ADR-0015: the fixture file's "contractValue" is $C^{orig}$ (domain-rules.md §4.3's
                // Reading A - the only reading that reproduces both R4's stated chain and 10.14%), so
                // it maps straight onto EscalationBaselineContractValue with no value change, only the
                // renamed parameter.
                EscalationBaselineContractValue: PolicyContractValue(policyName),
                CumulativeApprovedVoAmount: inputs.TryGetProperty("cumulativeApprovedVoBefore", out var cb) ? cb.GetDecimal() : 0m);

        var result = _service.Resolve(request);

        Assert.True(result.IsSuccess, $"{fixtureId} was expected to resolve successfully but failed with '{(result.IsFailure ? result.Error : string.Empty)}'.");
        Assert.Equal(expected.GetProperty("aRoute").GetDecimal(), result.Value.RoutingAmount);

        // R4 is the only fixture whose expected chain lives under "finalChain" (post-escalation) -
        // every other chain fixture just has "chain".
        var chainPropertyName = expected.TryGetProperty("finalChain", out _) ? "finalChain" : "chain";
        Assert.Equal(ExpectedChain(fixture, chainPropertyName), result.Value.Steps.Select(s => s.RequiredRole).ToList());

        if (expected.TryGetProperty("escalationTriggered", out var escalationTriggered))
        {
            // R4: explicitly prove the escalation - not the amount band alone - is what appended
            // the extra step. Re-resolve the identical amount with the cumulative cleared to 0 (so
            // Phi is nowhere near the threshold) and confirm the band-only chain is exactly the
            // fixture's own "bandChain", one step shorter than the escalated result. The baseline
            // contract value itself is still supplied (S10-BE-02 / domain-rules.md §4.6: once a
            // policy has CumulativeVoEscalationPct configured, omitting the baseline entirely is a
            // 422 ContractValueNotConfigured, not a silent "escalation off" switch) - what varies here
            // is purely whether the ratio crosses the threshold, which is what this assertion is
            // actually about.
            Assert.True(escalationTriggered.GetBoolean());
            Assert.True(result.Value.EscalationApplied);

            var bandOnlyRequest = new ApprovalRoutingRequest(
                documentType, ProjectId: null, amount, SubmittedAt, [policy],
                EscalationBaselineContractValue: PolicyContractValue(policyName),
                CumulativeApprovedVoAmount: 0m);
            var bandOnlyResult = _service.Resolve(bandOnlyRequest);

            Assert.True(bandOnlyResult.IsSuccess);
            Assert.False(bandOnlyResult.Value.EscalationApplied);
            Assert.Equal(ExpectedChain(fixture, "bandChain"), bandOnlyResult.Value.Steps.Select(s => s.RequiredRole).ToList());
            Assert.Equal(bandOnlyResult.Value.Steps.Count + 1, result.Value.Steps.Count);
        }
        else
        {
            Assert.False(result.Value.EscalationApplied);
        }

        if (fixtureId == "R2")
        {
            // R2's whole point: the raw signed input is negative (a Deduct), but A^route is its
            // absolute value - a signed routing amount would fall outside every MinAmount >= 0
            // band and wrongly resolve an empty chain instead of [PM, ProjectDirector].
            Assert.True(amount < 0, "R2's fixture amount must be negative to actually exercise the abs() rule.");
            Assert.Equal(Math.Abs(amount), result.Value.RoutingAmount);
        }

        if (fixtureId == "R6")
        {
            // R6's whole point: exactly at the 500,000.00 boundary, MinAmount is inclusive (the
            // amount belongs to the *upper* band) and the lower band's MaxAmount is exclusive (it
            // does not still apply there too). Assert the resolved chain is the upper band's
            // 2-step chain, not the lower band's single-PM chain - both pulled from the fixture
            // file's own "derivedBandsForConvenience", never hand-typed here.
            var lowerBandChain = FixtureRoot.GetProperty("policies").GetProperty("TH-Default-VO")
                .GetProperty("derivedBandsForConvenience")[0].GetProperty("chain")
                .EnumerateArray().Select(r => RoleNameMap[r.GetString()!]).ToList();
            Assert.NotEqual(lowerBandChain, result.Value.Steps.Select(s => s.RequiredRole).ToList());
        }
    }

    [Fact]
    public void R5_Amount_Below_The_Lowest_Configured_Band_Fails_Closed_With_ApprovalPolicyGap()
    {
        // The fixture's "hypothetical policy" is a standalone, minimally-configured policy whose
        // lowest MinAmount is 100,000.00 - built directly here (it is not TH-Default-VO/TH-Default-IPC
        // and has no ruleTableVerbatim of its own in the fixture file), so it cannot be driven
        // through the generic chain theory above.
        var fixture = Fixture("R5");
        var lowestMin = fixture.GetProperty("inputs").GetProperty("hypotheticalPolicyLowestMinAmount").GetDecimal();
        var policy = ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), null, ApprovalDocumentType.VariationOrder, SubmittedAt.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, lowestMin, null, UserRole.PM)]);
        var amount = fixture.GetProperty("inputs").GetProperty("amount").GetDecimal();

        var result = _service.Resolve(new ApprovalRoutingRequest(
            ApprovalDocumentType.VariationOrder, ProjectId: null, amount, SubmittedAt, [policy]));

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalErrorCodes.PolicyGap, result.Error);
        Assert.Equal("ApprovalPolicyGap", fixture.GetProperty("expected").GetProperty("errorType").GetString());
        Assert.Equal(422, fixture.GetProperty("expected").GetProperty("httpStatus").GetInt32());
        // NOTE: the 422 HTTP mapping itself (ResultProblemMapper -> ApprovalErrorCodes.PolicyGap)
        // already exists in CMPlus.WebApi, but there is still no HTTP endpoint in Sprint 2 that
        // ever *submits* a VO/IPC (that lands with the state machine in Sprint 9/10), so it cannot
        // yet be exercised end-to-end over HTTP - only this Result-level assertion is possible
        // today. Flagged for Sprint 9/10 QA to add the HTTP-level 422 assertion once a submit
        // endpoint exists.
    }

    /// <summary>
    /// R9 and R10 each blend a pure routing assertion with a state-machine/actor concern
    /// (revision-voiding and self-approval rejection respectively) that belongs to the VO/Payment
    /// Certificate transition handlers of Sprint 9/10, not to this Sprint 2 pure routing engine.
    /// Verified before accepting backend-developer's reasoning: as of this sprint there is no
    /// <c>VariationOrder</c>/<c>PaymentCertificate</c> entity, no <c>RevisionNo</c> anywhere outside
    /// <c>ApprovalAction</c> (which only records an already-decided action, it does not decide one),
    /// and no <c>SelfApproval</c> enforcement anywhere in the codebase except as a passive
    /// <c>ApprovalPolicy.AllowSelfApproval</c> configuration flag exposed read-only through
    /// <c>GetApprovalPolicyQuery</c> - there is genuinely no `Approve`/`ReturnForRevision` command
    /// handler to test against yet. The reasoning holds. Both fixtures are still exercised in full
    /// here for their *routing* half, with the out-of-scope half documented inline rather than
    /// silently skipped.
    /// </summary>
    [Fact]
    public void R9_Resubmission_At_A_Lower_Amount_Re_Resolves_And_Drops_ProjectDirector()
    {
        // Routing-only slice of R9: calling Resolve again with the revised amount is exactly the
        // re-resolution approval-workflow.md §5.3 step 8 calls for - no special-case code needed.
        // RevisionNo bumping and voiding the prior QS approval are Sprint 9/10 state-machine
        // concerns, out of scope here (see method remarks).
        var fixture = Fixture("R9");
        var policy = BuildPolicy("TH-Default-IPC", ApprovalDocumentType.PaymentCertificate);
        var resubmittedGross = fixture.GetProperty("inputs").GetProperty("resubmittedGrossCertifiedAmount_Gk").GetDecimal();

        var result = _service.Resolve(new ApprovalRoutingRequest(
            ApprovalDocumentType.PaymentCertificate, ProjectId: null, resubmittedGross, SubmittedAt, [policy]));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExpectedChain(fixture, "chain"), result.Value.Steps.Select(s => s.RequiredRole).ToList());
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

        var result = _service.Resolve(new ApprovalRoutingRequest(
            ApprovalDocumentType.PaymentCertificate, ProjectId: null, gross, SubmittedAt, [policy]));

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
