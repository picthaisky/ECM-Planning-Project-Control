using CMPlus.Application.Approval;
using CMPlus.Application.Features.VariationOrder.Commands.Submit;
using CMPlus.Application.Tests.Features.Payment;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.VariationOrder;

/// <summary>
/// S10-BE-02: the routing-service integration at the real handler boundary. R1-R6 themselves are
/// already proven against <c>ApprovalRoutingService</c> directly in
/// <c>CMPlus.Application.Tests.Approval.{ApprovalRoutingServiceTests,RoutingFixtureTests}</c> - this
/// file's job is the one thing a pure-service test structurally cannot prove: that the handler which
/// actually calls <c>Resolve</c> and then <c>VariationOrder.Submit</c> never lets the routing-only
/// absolute value leak back onto the aggregate's own persisted <c>Amount</c>.
/// </summary>
public class SubmitVariationOrderCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

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
        FakeVariationOrderRepository Repository,
        FakeProjectRepository ProjectRepository,
        FakeApprovalPolicyReaderForPayment PolicyReader,
        FakeApprovalActionRepository ActionRepository,
        Guid TenantId,
        Guid ProjectId);

    private static (Fixture Fixture, SubmitVariationOrderCommandHandler Handler) CreateHandler(
        Fixture? existing, decimal? escalationPct = null, Guid? actorUserId = null)
    {
        Fixture fixture;
        if (existing is not null)
        {
            fixture = existing;
        }
        else
        {
            var tenantId = Guid.NewGuid();
            var projectRepository = new FakeProjectRepository();
            // C^orig = 485,000,000.00 (domain-rules.md §3.3's TH-Default-VO fixture policy).
            var project = Project.Create(
                tenantId, "Project", "P-1", "Owner", Now.AddYears(-1), Now.AddYears(1),
                bac: 485_000_000.00m, dataDate: Now, contractValue: 485_000_000.00m);
            projectRepository.Seed(project);

            var policyReader = new FakeApprovalPolicyReaderForPayment();
            policyReader.Policies.Add(ApprovalPolicy.CreateInitialVersion(
                tenantId, projectId: null, ApprovalDocumentType.VariationOrder, Now.AddYears(-1), ThDefaultVoRules,
                allowSelfApproval: false, cumulativeVoEscalationPct: escalationPct, cumulativeVoEscalationRole: UserRole.Executive));

            fixture = new Fixture(
                new FakeVariationOrderRepository(), projectRepository, policyReader, new FakeApprovalActionRepository(),
                tenantId, project.Id); // ProjectId is the SEEDED project's real (UUIDv7-generated) id
        }

        var handler = new SubmitVariationOrderCommandHandler(
            fixture.Repository,
            fixture.ProjectRepository,
            fixture.PolicyReader,
            new ApprovalRoutingService(), // the real Sprint 2 service - "reuse, do not rewrite" (S10-BE-02 DoD)
            fixture.ActionRepository,
            new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(actorUserId ?? Guid.NewGuid(), UserRole.QS),
            new FakeClockForPayment(Now));

        return (fixture, handler);
    }

    private static Domain.Entities.VariationOrder SeedDraft(Fixture fixture, decimal amount)
    {
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, $"VO-{Guid.NewGuid():N}", Guid.NewGuid(), amount,
            "Test VO", null, 0, [new VariationOrderScopeItemInput(Guid.NewGuid(), amount)]);
        fixture.Repository.Seed(vo);
        return vo;
    }

    /// <summary>
    /// R2, the load-bearing fixture (domain-rules.md §3.4): three assertions, not one. (i) the
    /// routing amount used to select the chain is positive; (ii) the chain is byte-identical to a
    /// twin +800,000.00 Add's; (iii) - the assertion only THIS handler-level test can make - the
    /// PERSISTED <see cref="Domain.Entities.VariationOrder.Amount"/> is still exactly -800,000.00. A
    /// test that only checks the chain would pass over an implementation that silently wrote
    /// <c>Math.Abs(Amount)</c> back onto the aggregate inside the handler, which would then make BAC
    /// go UP on a Deduct VO at approval (domain-rules.md §5's effect).
    /// </summary>
    [Fact]
    public async Task R2_Deduct_800k_Resolves_The_Same_Chain_As_A_Plus_800k_Add_And_The_Persisted_Amount_Stays_Negative()
    {
        var (fixtureBase, _) = CreateHandler(null, escalationPct: 10.00m);
        var deductVo = SeedDraft(fixtureBase, -800_000.00m);

        var (_, deductHandler) = CreateHandler(fixtureBase);
        var deductResult = await deductHandler.Handle(new SubmitVariationOrderCommand(deductVo.Id), CancellationToken.None);

        Assert.True(deductResult.IsSuccess, deductResult.IsFailure ? deductResult.Error : string.Empty);

        // (iii) - the persisted Amount is UNCHANGED by Submit: still signed, still negative.
        Assert.Equal(-800_000.00m, deductVo.Amount);
        Assert.Equal(VariationOrderType.Deduct, deductVo.Type);

        var deductChain = deductVo.ApprovalSteps.OrderBy(s => s.StepNo).Select(s => s.RequiredRole).ToList();
        Assert.Equal([UserRole.PM, UserRole.ProjectDirector], deductChain);

        // (ii) twin control: a fresh +800,000.00 Add against the identical policy/cumulative state.
        var addFixture = fixtureBase with { Repository = new FakeVariationOrderRepository() };
        var addVo = SeedDraft(addFixture, 800_000.00m);
        var (_, addHandler) = CreateHandler(addFixture);
        var addResult = await addHandler.Handle(new SubmitVariationOrderCommand(addVo.Id), CancellationToken.None);

        Assert.True(addResult.IsSuccess);
        var addChain = addVo.ApprovalSteps.OrderBy(s => s.StepNo).Select(s => s.RequiredRole).ToList();
        Assert.Equal(addChain, deductChain);
        Assert.Equal(800_000.00m, addVo.Amount); // the control itself stays positive, as expected
    }

    [Fact]
    public async Task R1_Add_2_4M_Resolves_PM_Then_ProjectDirector_No_Escalation()
    {
        var (fixtureBase, _) = CreateHandler(null, escalationPct: 10.00m);
        // Sigma_prior = 6,000,000.00 (an already-Approved VO) so the escalation ratio stays low.
        var priorApproved = new Domain.Entities.VariationOrder(
            fixtureBase.TenantId, fixtureBase.ProjectId, "VO-000", Guid.NewGuid(), 6_000_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 6_000_000.00m)]);
        priorApproved.Submit([new(1, UserRole.PM, 1), new(2, UserRole.ProjectDirector, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        priorApproved.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, false, Now);
        priorApproved.Approve(Guid.NewGuid(), UserRole.ProjectDirector, UserRole.ProjectDirector, false, Now);
        fixtureBase.Repository.Seed(priorApproved);

        var vo = SeedDraft(fixtureBase, 2_400_000.00m);
        var (_, handler) = CreateHandler(fixtureBase);

        var result = await handler.Handle(new SubmitVariationOrderCommand(vo.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([UserRole.PM, UserRole.ProjectDirector], vo.ApprovalSteps.OrderBy(s => s.StepNo).Select(s => s.RequiredRole));
        Assert.Equal(2, vo.TotalSteps);
    }

    [Fact]
    public async Task R4_Escalation_Appends_Executive_When_Cumulative_Exceeds_10_Percent()
    {
        var (fixtureBase, _) = CreateHandler(null, escalationPct: 10.00m);
        // Sigma_prior = 46,000,000.00.
        var priorApproved = new Domain.Entities.VariationOrder(
            fixtureBase.TenantId, fixtureBase.ProjectId, "VO-000", Guid.NewGuid(), 46_000_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 46_000_000.00m)]);
        priorApproved.Submit([new(1, UserRole.PM, 1), new(2, UserRole.ProjectDirector, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        priorApproved.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, false, Now);
        priorApproved.Approve(Guid.NewGuid(), UserRole.ProjectDirector, UserRole.ProjectDirector, false, Now);
        fixtureBase.Repository.Seed(priorApproved);

        var vo = SeedDraft(fixtureBase, 3_200_000.00m);
        var (_, handler) = CreateHandler(fixtureBase);

        var result = await handler.Handle(new SubmitVariationOrderCommand(vo.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, vo.TotalSteps);
        Assert.Equal(UserRole.Executive, vo.ApprovalSteps.OrderBy(s => s.StepNo).Last().RequiredRole);
    }

    [Fact]
    public async Task R5_Below_The_Lowest_Configured_Band_Fails_Closed_And_Leaves_The_Vo_In_Draft()
    {
        var tenantId = Guid.NewGuid();
        var projectRepository = new FakeProjectRepository();
        var seededProject = Project.Create(
            tenantId, "Project", "P-1", "Owner", Now.AddYears(-1), Now.AddYears(1), bac: 1_000_000m, dataDate: Now);
        projectRepository.Seed(seededProject);
        var projectId = seededProject.Id;
        var policyReader = new FakeApprovalPolicyReaderForPayment();
        // TH-Gap-VO: nothing covers [0, 100,000).
        policyReader.Policies.Add(ApprovalPolicy.CreateInitialVersion(
            tenantId, null, ApprovalDocumentType.VariationOrder, Now.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, 100_000.00m, null, UserRole.PM)]));
        var repository = new FakeVariationOrderRepository();
        var vo = new Domain.Entities.VariationOrder(
            tenantId, projectId, "VO-1", Guid.NewGuid(), 50_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 50_000.00m)]);
        repository.Seed(vo);

        var handler = new SubmitVariationOrderCommandHandler(
            repository, projectRepository, policyReader, new ApprovalRoutingService(), new FakeApprovalActionRepository(),
            new FakeTenantProviderForPayment(tenantId), new FakeCurrentUserContextForPayment(Guid.NewGuid(), UserRole.QS),
            new FakeClockForPayment(Now));

        var result = await handler.Handle(new SubmitVariationOrderCommand(vo.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalErrorCodes.PolicyGap, result.Error);
        Assert.Equal(Domain.Enums.VariationOrderStatus.Draft, vo.Status); // never auto-approved, never left Draft
        Assert.Empty(vo.ApprovalSteps);
    }

    [Fact]
    public async Task A_Threshold_Configured_Policy_With_No_Usable_Baseline_Fails_Closed_With_ContractValueNotConfigured()
    {
        // domain-rules.md §4.6: Project.OriginalContractValue defaults to ContractValue, which
        // defaults to BAC - so a project created with bac: 0 (never configured) has an
        // EscalationBaselineContractValue of 0.00, which must block submission rather than silently
        // skip the escalation test.
        var tenantId = Guid.NewGuid();
        var projectRepository = new FakeProjectRepository();
        var seededProject = Project.Create(
            tenantId, "Project", "P-1", "Owner", Now.AddYears(-1), Now.AddYears(1), bac: 0m, dataDate: Now);
        projectRepository.Seed(seededProject);
        var projectId = seededProject.Id;
        var policyReader = new FakeApprovalPolicyReaderForPayment();
        policyReader.Policies.Add(ApprovalPolicy.CreateInitialVersion(
            tenantId, null, ApprovalDocumentType.VariationOrder, Now.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.PM)],
            allowSelfApproval: false, cumulativeVoEscalationPct: 10.00m, cumulativeVoEscalationRole: UserRole.Executive));
        var repository = new FakeVariationOrderRepository();
        var vo = new Domain.Entities.VariationOrder(
            tenantId, projectId, "VO-1", Guid.NewGuid(), 50_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 50_000.00m)]);
        repository.Seed(vo);

        var handler = new SubmitVariationOrderCommandHandler(
            repository, projectRepository, policyReader, new ApprovalRoutingService(), new FakeApprovalActionRepository(),
            new FakeTenantProviderForPayment(tenantId), new FakeCurrentUserContextForPayment(Guid.NewGuid(), UserRole.QS),
            new FakeClockForPayment(Now));

        var result = await handler.Handle(new SubmitVariationOrderCommand(vo.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalErrorCodes.ContractValueNotConfigured, result.Error);
        Assert.Equal(Domain.Enums.VariationOrderStatus.Draft, vo.Status);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Vo_Does_Not_Exist()
    {
        var (_, handler) = CreateHandler(null);

        var result = await handler.Handle(new SubmitVariationOrderCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderNotFound", result.Error);
    }

    [Fact]
    public async Task Handle_Returns_InvalidStatusForTransition_When_Not_Draft()
    {
        var (fixtureBase, _) = CreateHandler(null);
        var vo = SeedDraft(fixtureBase, 300_000.00m);
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        var (_, handler) = CreateHandler(fixtureBase);
        var result = await handler.Handle(new SubmitVariationOrderCommand(vo.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderInvalidStatusForTransition", result.Error);
    }
}
