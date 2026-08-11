using CMPlus.Application.Approval;
using CMPlus.Application.Features.Approval;
using CMPlus.Application.Features.Approval.Queries.SimulateRouting;
using CMPlus.Application.Features.VariationOrder.Commands.Submit;
using CMPlus.Application.Tests.Features.Payment;
using CMPlus.Application.Tests.Features.VariationOrder;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Approval;

/// <summary>
/// S15-BE-01: proves (a) the simulator agrees BY CONSTRUCTION with the real
/// <see cref="SubmitVariationOrderCommandHandler"/> resolution path for identical inputs - not merely
/// that both happen to produce the same-looking chain in one hand-checked example, but that both are
/// exercised against the SAME fixture state so any future divergence (e.g. someone "optimising" the
/// simulator to skip the escalation fetch) is caught here; (b) the simulator has zero side effects
/// (no repository's save is ever called); and (c) ADR-0021's two-simultaneously-active-policies case
/// is surfaced, never hidden.
/// </summary>
public class SimulateApprovalRoutingQueryHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-11T09:00:00+07:00");

    private static readonly IReadOnlyList<ApprovalPolicyRuleInput> ThDefaultVoRules =
    [
        new(1, 0.00m, 500_000.00m, UserRole.PM),
        new(1, 500_000.00m, 5_000_000.00m, UserRole.PM),
        new(2, 500_000.00m, 5_000_000.00m, UserRole.ProjectDirector),
        new(1, 5_000_000.00m, null, UserRole.PM),
        new(2, 5_000_000.00m, null, UserRole.ProjectDirector),
        new(3, 5_000_000.00m, null, UserRole.Executive),
    ];

    private sealed record Fixture(
        Guid TenantId,
        Guid ProjectId,
        FakeProjectRepository ProjectRepository,
        FakeVariationOrderRepository VariationOrderRepository,
        FakeApprovalPolicyReaderForPayment PolicyReader);

    private static Fixture CreateVoFixture(decimal? escalationPct = null)
    {
        var tenantId = Guid.NewGuid();
        var projectRepository = new FakeProjectRepository();
        var project = Project.Create(
            tenantId, "Project", "P-1", "Owner", Now.AddYears(-1), Now.AddYears(1),
            bac: 485_000_000.00m, dataDate: Now, contractValue: 485_000_000.00m);
        projectRepository.Seed(project);

        var policyReader = new FakeApprovalPolicyReaderForPayment();
        policyReader.Policies.Add(ApprovalPolicy.CreateInitialVersion(
            tenantId, projectId: null, ApprovalDocumentType.VariationOrder, Now.AddYears(-1), ThDefaultVoRules,
            allowSelfApproval: false, cumulativeVoEscalationPct: escalationPct, cumulativeVoEscalationRole: UserRole.Executive));

        return new Fixture(tenantId, project.Id, projectRepository, new FakeVariationOrderRepository(), policyReader);
    }

    private static SimulateApprovalRoutingQueryHandler CreateSimulator(Fixture fixture) =>
        new(fixture.PolicyReader, new ApprovalRoutingService(), fixture.ProjectRepository, fixture.VariationOrderRepository, new FakeClockForPayment(Now));

    /// <summary>
    /// The load-bearing mutation-evidence test: builds ONE fixture, runs the real
    /// <see cref="SubmitVariationOrderCommandHandler"/> on a Draft VO and the simulator against the
    /// SAME state for the SAME amount, and asserts the resolved chain, routing amount, escalation
    /// flag and pinned policy id/version are byte-identical between the two paths. If the simulator
    /// ever drifted onto a second implementation (e.g. forgetting to feed
    /// EscalationBaselineContractValue), this fixture (chosen to sit just past the R4 escalation
    /// threshold) would catch it: a simulator that silently skipped escalation would report 2 steps
    /// here while the real Submit reports 3.
    /// </summary>
    [Fact]
    public async Task Simulate_Agrees_With_A_Real_Submit_For_The_Same_Amount_Including_Escalation()
    {
        var fixture = CreateVoFixture(escalationPct: 10.00m);

        // Sigma_prior = 46,000,000.00 already Approved (domain-rules.md R4 fixture).
        var priorApproved = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-000", Guid.NewGuid(), 46_000_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 46_000_000.00m)]);
        priorApproved.Submit([new(1, UserRole.PM, 1), new(2, UserRole.ProjectDirector, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        priorApproved.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, false, Now);
        priorApproved.Approve(Guid.NewGuid(), UserRole.ProjectDirector, UserRole.ProjectDirector, false, Now);
        fixture.VariationOrderRepository.Seed(priorApproved);

        const decimal amount = 3_200_000.00m;

        // 1) The simulator, run FIRST (proves it looks and never writes, since if it mutated
        // anything the subsequent real Submit below would see corrupted state).
        var simulator = CreateSimulator(fixture);
        var simulation = await simulator.Handle(
            new SimulateApprovalRoutingQuery(ApprovalDocumentType.VariationOrder, fixture.ProjectId, amount), CancellationToken.None);

        Assert.True(simulation.IsSuccess, simulation.IsFailure ? simulation.Error : string.Empty);

        // 2) The real Submit, against the identical fixture state.
        var draftVo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-DRAFT", Guid.NewGuid(), amount, "Test VO", null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), amount)]);
        fixture.VariationOrderRepository.Seed(draftVo);

        var submitHandler = new SubmitVariationOrderCommandHandler(
            fixture.VariationOrderRepository, fixture.ProjectRepository, fixture.PolicyReader, new ApprovalRoutingService(),
            new FakeApprovalActionRepository(), new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(Guid.NewGuid(), UserRole.QS), new FakeClockForPayment(Now));

        var submitResult = await submitHandler.Handle(new SubmitVariationOrderCommand(draftVo.Id), CancellationToken.None);
        Assert.True(submitResult.IsSuccess, submitResult.IsFailure ? submitResult.Error : string.Empty);

        // 3) Byte-identical comparison.
        var simulated = simulation.Value;
        Assert.Equal(3, draftVo.TotalSteps); // sanity: this fixture DOES cross the escalation threshold
        Assert.Equal(draftVo.TotalSteps, simulated.Steps.Count);
        Assert.Equal(
            draftVo.ApprovalSteps.OrderBy(s => s.StepNo).Select(s => (s.StepNo, s.RequiredRole, s.QuorumCount)),
            simulated.Steps.OrderBy(s => s.StepNo).Select(s => (s.StepNo, s.RequiredRole, s.QuorumCount)));
        Assert.Equal(draftVo.ApprovalPolicyId, simulated.ApprovalPolicyId);
        Assert.Equal(draftVo.ApprovalPolicyVersion, simulated.ApprovalPolicyVersion);
        Assert.True(simulated.EscalationApplied);
        Assert.Equal(UserRole.Executive, simulated.Steps.OrderBy(s => s.StepNo).Last().RequiredRole);
        Assert.False(simulated.UsedFallbackChain);
        Assert.False(simulated.MultipleActivePoliciesDetected);
    }

    [Fact]
    public async Task Simulate_Has_Zero_Side_Effects_No_Repository_Save_Is_Ever_Called()
    {
        var fixture = CreateVoFixture(escalationPct: 10.00m);
        var simulator = CreateSimulator(fixture);

        var result = await simulator.Handle(
            new SimulateApprovalRoutingQuery(ApprovalDocumentType.VariationOrder, fixture.ProjectId, 2_400_000.00m), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(0, fixture.ProjectRepository.SaveChangesCallCount);
        Assert.Equal(0, fixture.VariationOrderRepository.SaveCallCount);
    }

    [Fact]
    public async Task Simulate_Reports_The_Chain_A_Real_Submit_Would_Hit_For_A_Below_Band_Amount_As_PolicyGap()
    {
        var fixture = CreateVoFixture();

        // Nothing covers [0, 100,000) - TH-Gap-VO shape.
        var gapPolicyReader = new FakeApprovalPolicyReaderForPayment();
        gapPolicyReader.Policies.Add(ApprovalPolicy.CreateInitialVersion(
            fixture.TenantId, null, ApprovalDocumentType.VariationOrder, Now.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, 100_000.00m, null, UserRole.PM)]));
        var gapFixture = fixture with { PolicyReader = gapPolicyReader };
        var simulator = CreateSimulator(gapFixture);

        var result = await simulator.Handle(
            new SimulateApprovalRoutingQuery(ApprovalDocumentType.VariationOrder, fixture.ProjectId, 50_000.00m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalErrorCodes.PolicyGap, result.Error);
    }

    [Fact]
    public async Task Simulate_Returns_ProjectNotFound_For_A_Project_Outside_The_Tenant()
    {
        var fixture = CreateVoFixture();
        var simulator = CreateSimulator(fixture);

        var result = await simulator.Handle(
            new SimulateApprovalRoutingQuery(ApprovalDocumentType.VariationOrder, Guid.NewGuid(), 300_000.00m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalSimulationErrorCodes.ProjectNotFound, result.Error);
    }

    [Fact]
    public async Task Simulate_Reports_The_Fallback_Chain_When_No_Policy_Is_Configured_At_All()
    {
        var tenantId = Guid.NewGuid();
        var projectRepository = new FakeProjectRepository();
        var project = Project.Create(tenantId, "Project", "P-1", "Owner", Now.AddYears(-1), Now.AddYears(1), bac: 1_000_000m, dataDate: Now);
        projectRepository.Seed(project);
        var fixture = new Fixture(tenantId, project.Id, projectRepository, new FakeVariationOrderRepository(), new FakeApprovalPolicyReaderForPayment());
        var simulator = CreateSimulator(fixture);

        var result = await simulator.Handle(
            new SimulateApprovalRoutingQuery(ApprovalDocumentType.VariationOrder, fixture.ProjectId, 300_000.00m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.UsedFallbackChain);
        Assert.Equal(Guid.Empty, result.Value.ApprovalPolicyId);
        Assert.Single(result.Value.Steps);
        Assert.Equal(UserRole.ProjectDirector, result.Value.Steps[0].RequiredRole);
    }

    /// <summary>
    /// ADR-0021: two policy rows simultaneously <c>IsActive</c> for the same
    /// (TenantId, DocumentType, ProjectId=null) scope - the corruption the filtered unique index
    /// cannot prevent for the NULL-ProjectId group. The simulator must NOT fail closed (a real Submit
    /// would still succeed, nondeterministically) and must NOT silently pick one without saying so -
    /// it must surface the ambiguity while still returning the chain <see cref="IApprovalRoutingService"/>
    /// actually resolved, so the simulator both mirrors real behaviour and doubles as a detection tool.
    /// </summary>
    [Fact]
    public async Task Simulate_Surfaces_Two_Simultaneously_Active_Tenant_Wide_Policies_Without_Failing_Closed()
    {
        var fixture = CreateVoFixture();

        var secondPolicy = ApprovalPolicy.CreateInitialVersion(
            fixture.TenantId, projectId: null, ApprovalDocumentType.VariationOrder, Now.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.ProjectDirector)]);
        fixture.PolicyReader.Policies.Add(secondPolicy); // now TWO active ProjectId=null VO policies

        var simulator = CreateSimulator(fixture);
        var result = await simulator.Handle(
            new SimulateApprovalRoutingQuery(ApprovalDocumentType.VariationOrder, fixture.ProjectId, 300_000.00m), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.True(result.Value.MultipleActivePoliciesDetected);
        Assert.Equal(2, result.Value.AmbiguousActivePolicies.Count);
        // The chain returned is a real, resolvable chain from ONE of the two policies - not an error,
        // not a merge of both - exactly mirroring what ApprovalRoutingService.Resolve's FirstOrDefault
        // would hand a real Submit right now.
        Assert.NotEmpty(result.Value.Steps);
        Assert.Contains(result.Value.AmbiguousActivePolicies, p => p.ApprovalPolicyId == result.Value.ApprovalPolicyId);
    }

    [Fact]
    public async Task Simulate_Does_Not_Report_Ambiguity_When_Only_One_Policy_Is_Active()
    {
        var fixture = CreateVoFixture();
        var simulator = CreateSimulator(fixture);

        var result = await simulator.Handle(
            new SimulateApprovalRoutingQuery(ApprovalDocumentType.VariationOrder, fixture.ProjectId, 300_000.00m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.MultipleActivePoliciesDetected);
        Assert.Empty(result.Value.AmbiguousActivePolicies);
    }

    [Fact]
    public async Task Simulate_Discloses_The_Amount_And_Routing_Amount_For_A_Payment_Certificate_With_No_Abs()
    {
        var tenantId = Guid.NewGuid();
        var projectRepository = new FakeProjectRepository();
        var project = Project.Create(tenantId, "Project", "P-1", "Owner", Now.AddYears(-1), Now.AddYears(1), bac: 50_000_000m, dataDate: Now);
        projectRepository.Seed(project);

        var policyReader = new FakeApprovalPolicyReaderForPayment();
        policyReader.Policies.Add(ApprovalPolicy.CreateInitialVersion(
            tenantId, null, ApprovalDocumentType.PaymentCertificate, Now.AddYears(-1),
            [
                new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS),
                new ApprovalPolicyRuleInput(2, 0.00m, null, UserRole.PM),
                new ApprovalPolicyRuleInput(3, 10_000_000.00m, null, UserRole.ProjectDirector),
            ]));

        var fixture = new Fixture(tenantId, project.Id, projectRepository, new FakeVariationOrderRepository(), policyReader);
        var simulator = CreateSimulator(fixture);

        var result = await simulator.Handle(
            new SimulateApprovalRoutingQuery(ApprovalDocumentType.PaymentCertificate, fixture.ProjectId, 21_600_000.00m), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(21_600_000.00m, result.Value.InputAmount);
        Assert.Equal(21_600_000.00m, result.Value.RoutingAmount); // R7 fixture: no abs() for IPC
        Assert.Equal([UserRole.QS, UserRole.PM, UserRole.ProjectDirector], result.Value.Steps.OrderBy(s => s.StepNo).Select(s => s.RequiredRole));
    }
}
