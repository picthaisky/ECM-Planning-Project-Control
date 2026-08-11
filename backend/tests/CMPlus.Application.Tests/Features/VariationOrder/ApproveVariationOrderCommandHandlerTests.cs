using CMPlus.Application.Approval;
using CMPlus.Application.Features.VariationOrder.Commands.Approve;
using CMPlus.Application.Tests.Features.Payment;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.VariationOrder;

/// <summary>
/// S10-BE-03: the money-moving transition. Covers the four things the task explicitly called out as
/// highest-risk: (1) BAC/ContractValue sign preservation on a Deduct VO; (2) the "BAC is a scalar,
/// PV/EV are sums over Activity.BudgetCost" invariant; (3) the escalation-bypass guard (domain-rules.md
/// §4.7 / fixture V-6); (4) the zero-<see cref="ProjectFinanceLedger"/>-rows rule (domain-rules.md §6.4).
/// </summary>
public class ApproveVariationOrderCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

    private sealed record Fixture(
        FakeVariationOrderRepository Repository,
        FakeProjectRepository ProjectRepository,
        FakeApprovalPolicyReaderForPayment PolicyReader,
        FakeApprovalActionRepository ActionRepository,
        FakePaymentCertificateRepository CertificateRepository,
        Guid TenantId,
        Guid ProjectId);

    private static Fixture NewFixture(decimal projectBac = 100_000_000.00m, decimal? projectOriginalContractValue = null)
    {
        var tenantId = Guid.NewGuid();
        var projectRepository = new FakeProjectRepository();
        var project = Project.Create(
            tenantId, "Project", "P-1", "Owner", Now.AddYears(-1), Now.AddYears(1),
            bac: projectBac, dataDate: Now, contractValue: projectOriginalContractValue ?? projectBac);
        projectRepository.Seed(project);

        return new Fixture(
            new FakeVariationOrderRepository(), projectRepository, new FakeApprovalPolicyReaderForPayment(),
            new FakeApprovalActionRepository(), new FakePaymentCertificateRepository(), tenantId, project.Id);
    }

    private static ApproveVariationOrderCommandHandler CreateHandler(Fixture fixture, Guid actorUserId, UserRole actorRole) =>
        new(
            fixture.Repository,
            fixture.ProjectRepository,
            fixture.PolicyReader,
            new ApprovalRoutingService(), // real Sprint 2 service - single source of truth for Phi (see handler's own remarks)
            fixture.ActionRepository,
            fixture.CertificateRepository,
            new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(actorUserId, actorRole),
            new FakeClockForPayment(Now));

    private static Project SeededProject(Fixture fixture) => fixture.ProjectRepository.FindAsync(fixture.ProjectId).Result!;

    /// <summary>Builds one <see cref="Activity"/>, one <see cref="Domain.Entities.VariationOrder"/>
    /// whose single scope line targets it 1:1 with <paramref name="amount"/>, submits it with
    /// <paramref name="steps"/>, and seeds both. <paramref name="policyId"/> defaults to
    /// <see cref="Guid.Empty"/> (the "no policy configured" fallback chain shape) so tests that do not
    /// care about the escalation re-check never need a registered <see cref="ApprovalPolicy"/> at all.</summary>
    private static (Domain.Entities.VariationOrder Vo, Activity Activity) SeedSubmittedVoWithOneActivity(
        Fixture fixture,
        IReadOnlyList<VariationOrderApprovalStepInput> steps,
        decimal amount,
        decimal activityBudgetBefore = 5_000_000.00m,
        decimal activityProgressPct = 0m,
        Guid? createdBy = null,
        Guid? submittedBy = null,
        bool allowSelfApproval = false,
        Guid? policyId = null,
        int policyVersion = 1)
    {
        var activity = new Activity(fixture.TenantId, Guid.NewGuid(), "A-1", "Activity 1", Now, Now.AddDays(30), 10, activityBudgetBefore);
        if (activityProgressPct > 0m)
        {
            activity.RecordProgress(Now, activityProgressPct, null, Guid.NewGuid(), ProgressSource.Manual, Now);
        }

        fixture.Repository.SeedActivity(fixture.ProjectId, activity);

        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, $"VO-{Guid.NewGuid():N}", createdBy ?? Guid.NewGuid(), amount,
            "Test VO", null, 0, [new VariationOrderScopeItemInput(activity.Id, amount)]);
        vo.Submit(steps, policyId ?? Guid.Empty, policyVersion, allowSelfApproval, submittedBy ?? Guid.NewGuid(), Now);
        fixture.Repository.Seed(vo);

        return (vo, activity);
    }

    // ------------------------------------------------------------------------------------
    // Basic guards (H-01/H-02/ADR-0016 shape, mirroring ApprovePaymentCertificateCommandHandlerTests)
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Vo_Does_Not_Exist()
    {
        var fixture = NewFixture();
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderNotFound", result.Error);
    }

    [Fact]
    public async Task Handle_Returns_InvalidStatusForTransition_When_Still_Draft()
    {
        var fixture = NewFixture();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 100_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 100_000.00m)]);
        fixture.Repository.Seed(vo);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderInvalidStatusForTransition", result.Error);
    }

    [Fact]
    public async Task Handle_Rejects_The_Wrong_Role_No_Escape_Hatch()
    {
        var fixture = NewFixture();
        var (vo, _) = SeedSubmittedVoWithOneActivity(fixture, [new(1, UserRole.PM, 1)], 300_000.00m);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.Site);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderNotAuthorizedForApprovalStep", result.Error);
        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status);
        Assert.Equal(1, vo.CurrentStepNo);
    }

    [Fact]
    public async Task Handle_Blocks_The_Creator_And_Submitter_From_Self_Approving_By_Default()
    {
        var fixture = NewFixture();
        var creatorId = Guid.NewGuid();
        var (vo, _) = SeedSubmittedVoWithOneActivity(fixture, [new(1, UserRole.PM, 1)], 300_000.00m, createdBy: creatorId);
        var handler = CreateHandler(fixture, creatorId, UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderSelfApprovalNotPermitted", result.Error);
    }

    [Fact]
    public async Task Handle_Allows_Self_Approval_When_The_Pinned_Policy_Opted_In()
    {
        var fixture = NewFixture();
        var creatorId = Guid.NewGuid();
        var (vo, _) = SeedSubmittedVoWithOneActivity(
            fixture, [new(1, UserRole.PM, 1)], 300_000.00m, createdBy: creatorId, submittedBy: creatorId, allowSelfApproval: true);
        var handler = CreateHandler(fixture, creatorId, UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VariationOrderStatus.Approved, result.Value.Status);
    }

    [Fact]
    public async Task Handle_Blocks_DuplicateChainVoter_An_Actor_Who_Already_Voted_This_Revision()
    {
        var fixture = NewFixture();
        IReadOnlyList<VariationOrderApprovalStepInput> twoPmSteps = [new(1, UserRole.PM, 1), new(2, UserRole.PM, 1)];
        var (vo, _) = SeedSubmittedVoWithOneActivity(fixture, twoPmSteps, 300_000.00m);
        var actorId = Guid.NewGuid();

        var first = await CreateHandler(fixture, actorId, UserRole.PM).Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(2, vo.CurrentStepNo);

        var second = await CreateHandler(fixture, actorId, UserRole.PM).Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal("VariationOrderDuplicateChainVoter", second.Error);
    }

    [Fact]
    public async Task Handle_Returns_CorruptApprovalChain_Instead_Of_Throwing_When_The_Snapshot_Has_No_Rung_For_The_Current_Step()
    {
        var fixture = NewFixture();
        IReadOnlyList<VariationOrderApprovalStepInput> corruptSteps = [new(1, UserRole.PM, 1), new(1, UserRole.QS, 1)];
        var (vo, _) = SeedSubmittedVoWithOneActivity(fixture, corruptSteps, 300_000.00m);

        var first = await CreateHandler(fixture, Guid.NewGuid(), UserRole.PM).Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);
        Assert.True(first.IsSuccess); // step 1's PM rung resolves fine, advances CurrentStepNo to 2
        Assert.Equal(2, vo.CurrentStepNo);

        var second = await CreateHandler(fixture, Guid.NewGuid(), UserRole.QS).Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal("VariationOrderCorruptApprovalChain", second.Error);
    }

    [Fact]
    public async Task Handle_Returns_ConcurrencyConflict_When_The_Repository_Save_Reports_A_Conflict()
    {
        var fixture = NewFixture();
        var (vo, _) = SeedSubmittedVoWithOneActivity(fixture, [new(1, UserRole.PM, 1)], 300_000.00m);
        fixture.Repository.SaveShouldSucceed = false;
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderConcurrencyConflict", result.Error);
    }

    // ------------------------------------------------------------------------------------
    // BAC/ContractValue signed move - sign preservation on a Deduct VO
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_On_Final_Step_Moves_Bac_And_ContractValue_Up_By_Amount_For_An_Add_Vo()
    {
        var fixture = NewFixture(projectBac: 100_000_000.00m);
        var (vo, activity) = SeedSubmittedVoWithOneActivity(fixture, [new(1, UserRole.PM, 1)], 2_400_000.00m, activityBudgetBefore: 5_000_000.00m);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, "Looks correct."), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        var project = SeededProject(fixture);
        Assert.Equal(102_400_000.00m, project.BAC);
        Assert.Equal(102_400_000.00m, project.ContractValue);
        Assert.Equal(100_000_000.00m, vo.BacBefore);
        Assert.Equal(102_400_000.00m, vo.BacAfter);
        Assert.Equal(7_400_000.00m, activity.BudgetCost);
    }

    /// <summary>
    /// The exact risk the task called out: an implementation that writes <c>Math.Abs(Amount)</c> back
    /// onto the aggregate (or applies the delta unsigned anywhere on this path) would make BAC go UP
    /// on a Deduct VO instead of down. This test fails loudly if that mutation is ever introduced.
    /// </summary>
    [Fact]
    public async Task Handle_On_Final_Step_Moves_Bac_And_ContractValue_Down_For_A_Deduct_Vo_Sign_Preserved()
    {
        var fixture = NewFixture(projectBac: 100_000_000.00m);
        var (vo, activity) = SeedSubmittedVoWithOneActivity(
            fixture, [new(1, UserRole.PM, 1)], -800_000.00m, activityBudgetBefore: 5_000_000.00m);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        var project = SeededProject(fixture);
        Assert.Equal(99_200_000.00m, project.BAC); // DOWN, not up
        Assert.Equal(99_200_000.00m, project.ContractValue);
        Assert.True(project.BAC < 100_000_000.00m);
        Assert.Equal(4_200_000.00m, activity.BudgetCost); // DOWN by the same 800,000.00
        // The persisted Amount was never overwritten to its absolute value anywhere on this path.
        Assert.Equal(-800_000.00m, vo.Amount);
        Assert.Equal(VariationOrderType.Deduct, vo.Type);
    }

    [Fact]
    public async Task Handle_On_A_Non_Final_Step_Advances_Without_Moving_Bac_Or_ContractValue_At_All()
    {
        var fixture = NewFixture(projectBac: 100_000_000.00m);
        IReadOnlyList<VariationOrderApprovalStepInput> twoSteps = [new(1, UserRole.PM, 1), new(2, UserRole.ProjectDirector, 1)];
        var (vo, activity) = SeedSubmittedVoWithOneActivity(fixture, twoSteps, 2_400_000.00m);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VariationOrderStatus.PendingApproval, result.Value.Status);
        var project = SeededProject(fixture);
        Assert.Equal(100_000_000.00m, project.BAC); // untouched - not yet the final step
        Assert.Equal(5_000_000.00m, activity.BudgetCost); // untouched
        Assert.Null(vo.BacBefore);
    }

    // ------------------------------------------------------------------------------------
    // The BAC invariant: Project.BAC is a scalar, PV/EV are sums over Activity.BudgetCost
    // (domain-rules.md §5.2 - "the subtlest thing in this sprint")
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_Applies_Every_Scope_Lines_Delta_To_Its_Own_Activity_Summing_To_Exactly_The_Vos_Bac_Delta()
    {
        var fixture = NewFixture(projectBac: 100_000_000.00m);
        var activityA = new Activity(fixture.TenantId, Guid.NewGuid(), "A-1", "Activity A", Now, Now.AddDays(10), 5, 1_000_000.00m);
        var activityB = new Activity(fixture.TenantId, Guid.NewGuid(), "A-2", "Activity B", Now, Now.AddDays(10), 5, 2_000_000.00m);
        fixture.Repository.SeedActivity(fixture.ProjectId, activityA);
        fixture.Repository.SeedActivity(fixture.ProjectId, activityB);

        // Net Amount = 500,000.00: +800,000.00 on A, -300,000.00 on B.
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 500_000.00m, "Mixed scope", null, 0,
            [new VariationOrderScopeItemInput(activityA.Id, 800_000.00m), new VariationOrderScopeItemInput(activityB.Id, -300_000.00m)]);
        vo.Submit([new(1, UserRole.PM, 1)], Guid.Empty, 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(vo);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(1_800_000.00m, activityA.BudgetCost); // 1,000,000.00 + 800,000.00
        Assert.Equal(1_700_000.00m, activityB.BudgetCost); // 2,000,000.00 - 300,000.00

        var project = SeededProject(fixture);
        var totalActivityDelta = (activityA.BudgetCost - 1_000_000.00m) + (activityB.BudgetCost - 2_000_000.00m);
        var bacDelta = project.BAC - 100_000_000.00m;

        // THE invariant, asserted directly: the sum of every activity's own budget delta equals
        // Project.BAC's own delta, to the cent. A bug that moves one without the other is caught here
        // even if each individual assertion above happened to look locally plausible.
        Assert.Equal(500_000.00m, totalActivityDelta);
        Assert.Equal(500_000.00m, bacDelta);
        Assert.Equal(totalActivityDelta, bacDelta);
    }

    [Fact]
    public async Task Handle_Returns_OmitsExecutedScope_When_A_Negative_Delta_Targets_An_Activity_With_Recorded_Progress()
    {
        var fixture = NewFixture();
        var (vo, activity) = SeedSubmittedVoWithOneActivity(
            fixture, [new(1, UserRole.PM, 1)], -100_000.00m, activityBudgetBefore: 5_000_000.00m, activityProgressPct: 25m);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VoOmitsExecutedScope", result.Error);
        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status); // nothing moved
        Assert.Equal(5_000_000.00m, activity.BudgetCost);
        Assert.Equal(100_000_000.00m, SeededProject(fixture).BAC);
    }

    [Fact]
    public async Task Handle_Returns_ScopeActivityBudgetWouldBeNegative_When_A_Lines_Delta_Would_Drive_That_Activity_Below_Zero()
    {
        var fixture = NewFixture();
        var (vo, activity) = SeedSubmittedVoWithOneActivity(
            fixture, [new(1, UserRole.PM, 1)], -80_000.00m, activityBudgetBefore: 50_000.00m);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VoScopeActivityBudgetWouldBeNegative", result.Error);
        Assert.Equal(50_000.00m, activity.BudgetCost); // untouched
    }

    [Fact]
    public async Task Handle_Returns_WouldMakeContractValueNegative_And_Moves_Nothing()
    {
        var fixture = NewFixture(projectBac: 500_000.00m);
        var (vo, activity) = SeedSubmittedVoWithOneActivity(
            fixture, [new(1, UserRole.PM, 1)], -600_000.00m, activityBudgetBefore: 700_000.00m);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VoWouldMakeContractValueNegative", result.Error);
        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status);
        Assert.Equal(500_000.00m, SeededProject(fixture).BAC);
        Assert.Equal(700_000.00m, activity.BudgetCost);
    }

    // ------------------------------------------------------------------------------------
    // Zero ProjectFinanceLedger rows, absolutely (domain-rules.md §6.4) + §6.2 disclosure
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_Never_Adds_Any_Ledger_Entry_Even_When_An_InFlight_Certificate_Exists_For_The_Project()
    {
        var fixture = NewFixture(projectBac: 100_000_000.00m);
        var (vo, _) = SeedSubmittedVoWithOneActivity(fixture, [new(1, UserRole.PM, 1)], 1_000_000.00m);

        var inFlightCertificate = new PaymentCertificate(
            fixture.TenantId, fixture.ProjectId, 1, "Period 1", 5_000_000.00m, 0m, Guid.NewGuid());
        inFlightCertificate.SetPeriodClaim(50m, null, null, 5_000_000.00m, 250_000.00m, 500_000.00m, 4_250_000.00m);
        inFlightCertificate.Submit([new(1, UserRole.QS, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        fixture.CertificateRepository.Seed(inFlightCertificate);

        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.PM);
        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Empty(fixture.CertificateRepository.AddedLedgerEntries); // the absolute rule, §6.4

        // And the mandatory disclosure (§6.2) DID fire - not silent.
        var disclosure = Assert.Single(fixture.Repository.AddedAuditLogEntries);
        Assert.Equal("VoApprovedWithCertificateInFlight", disclosure.EntityName);
        Assert.Equal(inFlightCertificate.Id, disclosure.EntityId);
    }

    [Fact]
    public async Task Handle_Writes_No_Disclosure_When_No_Certificate_Is_InFlight_For_The_Project()
    {
        var fixture = NewFixture();
        var (vo, _) = SeedSubmittedVoWithOneActivity(fixture, [new(1, UserRole.PM, 1)], 1_000_000.00m);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.PM);

        var result = await handler.Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(fixture.Repository.AddedAuditLogEntries);
        Assert.Empty(fixture.CertificateRepository.AddedLedgerEntries);
    }

    // ------------------------------------------------------------------------------------
    // The escalation-bypass guard (domain-rules.md §4.7, fixture V-6) - close the bypass
    // ------------------------------------------------------------------------------------

    private static (Fixture Fixture, Guid PolicyId) FixtureWithEscalationPolicy(
        decimal thresholdPct = 10.00m, decimal originalContractValue = 485_000_000.00m)
    {
        var fixture = NewFixture(projectBac: originalContractValue, projectOriginalContractValue: originalContractValue);
        var policy = ApprovalPolicy.CreateInitialVersion(
            fixture.TenantId, projectId: null, ApprovalDocumentType.VariationOrder, Now.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.PM)],
            allowSelfApproval: false, cumulativeVoEscalationPct: thresholdPct, cumulativeVoEscalationRole: UserRole.Executive);
        fixture.PolicyReader.Policies.Add(policy);
        return (fixture, policy.Id);
    }

    /// <summary>The escalation tests below only ever submit <c>Add</c> amounts, so a single very
    /// large, freshly-seeded <see cref="Activity"/> per scope item safely absorbs every delta with no
    /// risk of tripping the (unrelated, already covered elsewhere) per-activity-negative guard.</summary>
    private static VariationOrderScopeItemInput SeededScopeItem(Fixture fixture, decimal amount)
    {
        var activity = new Activity(fixture.TenantId, Guid.NewGuid(), $"A-{Guid.NewGuid():N}", "Activity", Now, Now.AddDays(30), 10, 900_000_000.00m);
        fixture.Repository.SeedActivity(fixture.ProjectId, activity);
        return new VariationOrderScopeItemInput(activity.Id, amount);
    }

    private static Domain.Entities.VariationOrder SeedApprovedVo(Fixture fixture, decimal amount)
    {
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, $"VO-{Guid.NewGuid():N}", Guid.NewGuid(), amount, null, null, 0,
            [SeededScopeItem(fixture, amount)]);
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, false, Now);
        Assert.Equal(VariationOrderStatus.Approved, vo.Status);
        fixture.Repository.Seed(vo);
        return vo;
    }

    /// <summary>
    /// V-6 steps 1-5 exactly: Sigma_prior = 44,000,000.00 before VO-A; VO-A Add +2,400,000.00 is then
    /// approved (step 3), so Sigma^VO = 46,400,000.00 at the moment VO-B's final step is voted on -
    /// modelled here as one already-Approved stand-in VO carrying that combined 46,400,000.00, since
    /// this test only needs the RESULTING cumulative total, not VO-A's own submit/approve mechanics
    /// (covered by the OTHER escalation tests in this file). VO-B Add +2,300,000.00 is submitted with
    /// the BAND-ONLY 2-step chain [PM, ProjectDirector] (as it would have resolved before VO-A's
    /// approval pushed the cumulative total up). PM's vote (step 1) succeeds normally;
    /// ProjectDirector's vote (step 2, final) must be BLOCKED - (46,400,000 + 2,300,000) /
    /// 485,000,000 = 10.0412...% &gt; 10.00%, and Executive is not in VO-B's snapshotted chain.
    /// </summary>
    [Fact]
    public async Task Handle_Blocks_The_Final_Approval_When_The_Escalation_Threshold_Was_Crossed_By_Other_Vos_Since_Submission()
    {
        var (fixture, policyId) = FixtureWithEscalationPolicy();
        SeedApprovedVo(fixture, 46_400_000.00m); // Sigma^VO at the moment of VO-B's final vote (V-6 step 3's result)

        var voB = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-B", Guid.NewGuid(), 2_300_000.00m, null, null, 0,
            [SeededScopeItem(fixture, 2_300_000.00m)]);
        voB.Submit([new(1, UserRole.PM, 1), new(2, UserRole.ProjectDirector, 1)], policyId, 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(voB);

        var pmResult = await CreateHandler(fixture, Guid.NewGuid(), UserRole.PM)
            .Handle(new ApproveVariationOrderCommand(voB.Id, null), CancellationToken.None);
        Assert.True(pmResult.IsSuccess);
        Assert.Equal(2, voB.CurrentStepNo);
        var lastVoteAtAfterPm = voB.LastVoteAt;
        var actionCountAfterPm = fixture.ActionRepository.Actions.Count;

        var pdResult = await CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector)
            .Handle(new ApproveVariationOrderCommand(voB.Id, null), CancellationToken.None);

        Assert.True(pdResult.IsFailure);
        Assert.Equal("VoEscalationThresholdCrossedSinceSubmission", pdResult.Error);

        // "VO-B stays PendingApproval 2/2" (V-6) - byte-for-byte, nothing advanced or was recorded.
        Assert.Equal(VariationOrderStatus.PendingApproval, voB.Status);
        Assert.Equal(2, voB.CurrentStepNo);
        Assert.Equal(2, voB.TotalSteps);
        Assert.DoesNotContain(voB.ApprovalSteps, s => s.RequiredRole == UserRole.Executive); // never appended to the pinned chain
        Assert.Equal(lastVoteAtAfterPm, voB.LastVoteAt); // the blocked PD attempt never stamped a vote
        Assert.Equal(actionCountAfterPm, fixture.ActionRepository.Actions.Count); // no ApprovalAction written for the blocked attempt
        Assert.Null(voB.BacBefore); // no effects recorded
        Assert.Equal(485_000_000.00m, SeededProject(fixture).BAC); // no BAC change
    }

    /// <summary>Control: identical shape, but Sigma_prior stays low enough that Phi never crosses the
    /// threshold - the final approval must succeed normally. Proves the guard is not simply refusing
    /// every final Approve on an escalation-configured policy.</summary>
    [Fact]
    public async Task Handle_Does_Not_Block_The_Final_Approval_When_The_Escalation_Ratio_Stays_Under_Threshold()
    {
        var (fixture, policyId) = FixtureWithEscalationPolicy();
        SeedApprovedVo(fixture, 20_000_000.00m); // well under threshold even with this VO added

        var voB = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-B", Guid.NewGuid(), 2_300_000.00m, null, null, 0,
            [SeededScopeItem(fixture, 2_300_000.00m)]);
        voB.Submit([new(1, UserRole.PM, 1), new(2, UserRole.ProjectDirector, 1)], policyId, 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(voB);

        await CreateHandler(fixture, Guid.NewGuid(), UserRole.PM).Handle(new ApproveVariationOrderCommand(voB.Id, null), CancellationToken.None);
        var result = await CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector)
            .Handle(new ApproveVariationOrderCommand(voB.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(VariationOrderStatus.Approved, voB.Status);
        Assert.NotNull(voB.CumulativeVoPctAtApproval);
    }

    /// <summary>If the escalation role is already required by VO-B's OWN snapshotted chain (e.g. it
    /// escalated correctly at Submit time), the re-check must be a no-op even though Phi still exceeds
    /// the threshold - there is no bypass risk to guard against.</summary>
    [Fact]
    public async Task Handle_Does_Not_Block_When_The_Escalation_Role_Is_Already_Part_Of_The_Snapshotted_Chain()
    {
        var (fixture, policyId) = FixtureWithEscalationPolicy();
        SeedApprovedVo(fixture, 44_000_000.00m);

        var voB = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-B", Guid.NewGuid(), 2_300_000.00m, null, null, 0,
            [SeededScopeItem(fixture, 2_300_000.00m)]);
        // Already escalated at Submit (3 steps, includes Executive) - unlike the blocking test above.
        voB.Submit(
            [new(1, UserRole.PM, 1), new(2, UserRole.ProjectDirector, 1), new(3, UserRole.Executive, 1)],
            policyId, 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(voB);

        await CreateHandler(fixture, Guid.NewGuid(), UserRole.PM).Handle(new ApproveVariationOrderCommand(voB.Id, null), CancellationToken.None);
        await CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector).Handle(new ApproveVariationOrderCommand(voB.Id, null), CancellationToken.None);
        var result = await CreateHandler(fixture, Guid.NewGuid(), UserRole.Executive)
            .Handle(new ApproveVariationOrderCommand(voB.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(VariationOrderStatus.Approved, voB.Status);
    }

    [Fact]
    public async Task Handle_Never_Re_Checks_Escalation_When_No_Threshold_Is_Configured_On_The_Pinned_Policy()
    {
        var fixture = NewFixture(projectBac: 485_000_000.00m, projectOriginalContractValue: 485_000_000.00m);
        var policy = ApprovalPolicy.CreateInitialVersion(
            fixture.TenantId, null, ApprovalDocumentType.VariationOrder, Now.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.PM)]); // CumulativeVoEscalationPct: null
        fixture.PolicyReader.Policies.Add(policy);
        SeedApprovedVo(fixture, 400_000_000.00m); // absurdly high Sigma_prior - would obviously "escalate" if checked

        var voB = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-B", Guid.NewGuid(), 1_000_000.00m, null, null, 0,
            [SeededScopeItem(fixture, 1_000_000.00m)]);
        voB.Submit([new(1, UserRole.PM, 1)], policy.Id, 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(voB);

        var result = await CreateHandler(fixture, Guid.NewGuid(), UserRole.PM)
            .Handle(new ApproveVariationOrderCommand(voB.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Null(voB.CumulativeVoPctAtApproval); // nothing to record - no threshold was configured
    }

    /// <summary>
    /// H-01 (docs/security/reviews/sprint-10.md): the exact bypass the review found, reproduced and
    /// closed at the unit level. Before the fix, <c>CheckEscalationBypassAsync</c> re-ran the pinned
    /// policy through <c>IApprovalRoutingService.Resolve</c> (passing it as a single-item "candidate"
    /// list), which re-applied <c>SelectPolicy</c>'s <c>IsActive</c>/effective-window predicate to a
    /// policy that had ALREADY been chosen. <c>UpdateApprovalPolicyCommandHandler</c> deactivates the
    /// current version on every ordinary edit - so the moment an Admin touched the VO policy at all
    /// (a band, a quorum, a typo fix) between VO-B's PM approval and its final Project-Director vote,
    /// selection found nothing and silently substituted the permissive, no-<c>Executive</c>
    /// <c>FallbackChain</c> - the guard's blocking condition went permanently false and the final
    /// approval succeeded with no Executive signature anywhere. <c>ResolvePinned</c> never selects, so
    /// this must still block regardless of the pinned policy's <c>IsActive</c> flag at re-check time.
    /// </summary>
    [Fact]
    public async Task Handle_Still_Blocks_The_Final_Approval_When_The_Pinned_Policy_Was_Deactivated_By_An_Ordinary_Edit_Since_Submission_H01()
    {
        var (fixture, policyId) = FixtureWithEscalationPolicy();
        SeedApprovedVo(fixture, 46_400_000.00m); // Sigma^VO at the moment of VO-B's final vote (V-6 step 3's result)

        var voB = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-B", Guid.NewGuid(), 2_300_000.00m, null, null, 0,
            [SeededScopeItem(fixture, 2_300_000.00m)]);
        voB.Submit([new(1, UserRole.PM, 1), new(2, UserRole.ProjectDirector, 1)], policyId, 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(voB);

        var pmResult = await CreateHandler(fixture, Guid.NewGuid(), UserRole.PM)
            .Handle(new ApproveVariationOrderCommand(voB.Id, null), CancellationToken.None);
        Assert.True(pmResult.IsSuccess);
        Assert.Equal(2, voB.CurrentStepNo);

        // The exact H-01 trigger: an ordinary policy edit deactivates the pinned version between
        // submission and the final vote - UpdateApprovalPolicyCommandHandler.cs:45 calls
        // current?.Deactivate(now) unconditionally on every save, never only on a semantic change.
        var pinnedPolicy = fixture.PolicyReader.Policies.Single(p => p.Id == policyId);
        pinnedPolicy.Deactivate(Now.AddMinutes(1));
        Assert.False(pinnedPolicy.IsActive);

        var pdResult = await CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector)
            .Handle(new ApproveVariationOrderCommand(voB.Id, null), CancellationToken.None);

        // MUST still block - the guard must not care whether the pinned policy happens to still be
        // IsActive. Before the H-01 fix this line returned Success with no Executive signature ever
        // recorded (BYPASS, execution-verified in the review).
        Assert.True(pdResult.IsFailure);
        Assert.Equal("VoEscalationThresholdCrossedSinceSubmission", pdResult.Error);
        Assert.Equal(VariationOrderStatus.PendingApproval, voB.Status);
        Assert.Equal(2, voB.CurrentStepNo);
        Assert.DoesNotContain(voB.ApprovalSteps, s => s.RequiredRole == UserRole.Executive);
        Assert.Null(voB.BacBefore); // no effects recorded - nothing moved
        Assert.Equal(485_000_000.00m, SeededProject(fixture).BAC); // unchanged
    }

    [Fact]
    public async Task Handle_Never_Re_Checks_Escalation_On_The_No_Policy_Fallback_Chain()
    {
        // ApprovalPolicyId == Guid.Empty (SeedSubmittedVoWithOneActivity's default) - no real policy
        // row exists to carry a threshold at all.
        var fixture = NewFixture();
        var (vo, _) = SeedSubmittedVoWithOneActivity(fixture, [new(1, UserRole.ProjectDirector, 1)], 1_000_000.00m);

        var result = await CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector)
            .Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(VariationOrderStatus.Approved, vo.Status);
    }
}
