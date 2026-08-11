using CMPlus.Application.Features.VariationOrder.Commands.Cancel;
using CMPlus.Application.Features.VariationOrder.Commands.Create;
using CMPlus.Application.Features.VariationOrder.Commands.ReturnForRevision;
using CMPlus.Application.Features.VariationOrder.Commands.UpdateContent;
using CMPlus.Application.Features.VariationOrder.Commands.Withdraw;
using CMPlus.Application.Features.VariationOrder.Queries.GetVariationOrder;
using CMPlus.Application.Features.VariationOrder.Queries.ListVariationOrders;
using CMPlus.Application.Tests.Features.Payment;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.VariationOrder;

/// <summary>
/// S10-BE-01: baseline coverage for the remaining command/query handlers not already covered in
/// depth elsewhere in this folder (Create, UpdateContent, ReturnForRevision, Withdraw, Cancel, and
/// the two reads) - one happy path and the handler's own distinguishing guard per action, mirroring
/// the equivalent Payment Certificate coverage shape.
/// </summary>
public class RemainingCommandAndQueryHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

    private sealed record Fixture(FakeVariationOrderRepository Repository, FakeProjectRepository ProjectRepository, Guid TenantId, Guid ProjectId);

    private static Fixture NewFixture()
    {
        var tenantId = Guid.NewGuid();
        var projectRepository = new FakeProjectRepository();
        var project = Project.Create(tenantId, "Project", "P-1", "Owner", Now.AddYears(-1), Now.AddYears(1), bac: 10_000_000m, dataDate: Now);
        projectRepository.Seed(project);
        return new Fixture(new FakeVariationOrderRepository(), projectRepository, tenantId, project.Id);
    }

    /// <summary>Create/UpdateContent both validate scope-item ActivityIds against the project's real
    /// activities (<see cref="VariationOrderErrorCodes.UnknownActivity"/>) - tests that are not
    /// specifically exercising that guard need a genuinely-seeded id, not a bare <c>Guid.NewGuid()</c>.</summary>
    private static Guid SeededActivityId(Fixture fixture)
    {
        var activity = new Activity(fixture.TenantId, Guid.NewGuid(), $"A-{Guid.NewGuid():N}", "Activity", Now, Now.AddDays(10), 5, 10_000_000.00m);
        fixture.Repository.SeedActivity(fixture.ProjectId, activity);
        return activity.Id;
    }

    // ---- Create ----

    [Fact]
    public async Task Create_Succeeds_And_Persists_A_Draft_Vo()
    {
        var fixture = NewFixture();
        var handler = new CreateVariationOrderCommandHandler(
            fixture.Repository, fixture.ProjectRepository, new FakeCurrentUserContextForPayment(Guid.NewGuid(), UserRole.QS));

        var result = await handler.Handle(
            new CreateVariationOrderCommand(
                fixture.ProjectId, "VO-100", "Additional works", "Site instruction", 500_000.00m, 0,
                [new VariationOrderScopeItemInput(SeededActivityId(fixture), 500_000.00m)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(VariationOrderStatus.Draft, result.Value.Status);
        Assert.Equal(1, fixture.Repository.SaveCallCount);
    }

    [Fact]
    public async Task Create_Rejects_A_Duplicate_VoNumber_For_The_Same_Project()
    {
        var fixture = NewFixture();
        var existing = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-100", Guid.NewGuid(), 100_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 100_000.00m)]);
        fixture.Repository.Seed(existing);
        var handler = new CreateVariationOrderCommandHandler(
            fixture.Repository, fixture.ProjectRepository, new FakeCurrentUserContextForPayment(Guid.NewGuid(), UserRole.QS));

        var result = await handler.Handle(
            new CreateVariationOrderCommand(fixture.ProjectId, "VO-100", null, null, 200_000.00m, 0,
                [new VariationOrderScopeItemInput(Guid.NewGuid(), 200_000.00m)]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderDuplicateVoNumber", result.Error);
    }

    [Fact]
    public async Task Create_Rejects_A_Scope_Budget_Mismatch_Before_Ever_Constructing_The_Aggregate()
    {
        var fixture = NewFixture();
        var handler = new CreateVariationOrderCommandHandler(
            fixture.Repository, fixture.ProjectRepository, new FakeCurrentUserContextForPayment(Guid.NewGuid(), UserRole.QS));

        var result = await handler.Handle(
            new CreateVariationOrderCommand(fixture.ProjectId, "VO-101", null, null, 500_000.00m, 0,
                [new VariationOrderScopeItemInput(SeededActivityId(fixture), 499_999.00m)]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VoScopeBudgetMismatch", result.Error);
    }

    [Fact]
    public async Task Create_Returns_ProjectNotFound_For_An_Unknown_Project()
    {
        var fixture = NewFixture();
        var handler = new CreateVariationOrderCommandHandler(
            fixture.Repository, fixture.ProjectRepository, new FakeCurrentUserContextForPayment(Guid.NewGuid(), UserRole.QS));

        var result = await handler.Handle(
            new CreateVariationOrderCommand(Guid.NewGuid(), "VO-102", null, null, 100_000.00m, 0,
                [new VariationOrderScopeItemInput(Guid.NewGuid(), 100_000.00m)]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderProjectNotFound", result.Error);
    }

    // ---- UpdateContent ----

    [Fact]
    public async Task UpdateContent_RePrices_A_Draft_Vo()
    {
        var fixture = NewFixture();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 400_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 400_000.00m)]);
        fixture.Repository.Seed(vo);
        var handler = new UpdateVariationOrderContentCommandHandler(fixture.Repository);
        var newActivityId = SeededActivityId(fixture);

        var result = await handler.Handle(
            new UpdateVariationOrderContentCommand(vo.Id, "Re-priced", null, 600_000.00m, 2,
                [new VariationOrderScopeItemInput(newActivityId, 600_000.00m)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(600_000.00m, vo.Amount);
        Assert.Equal(2, vo.TimeImpactDays);
    }

    [Fact]
    public async Task UpdateContent_Refuses_Once_The_Vo_Has_Left_Draft()
    {
        var fixture = NewFixture();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 400_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 400_000.00m)]);
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(vo);
        var handler = new UpdateVariationOrderContentCommandHandler(fixture.Repository);

        var result = await handler.Handle(
            new UpdateVariationOrderContentCommand(vo.Id, null, null, 999_999.00m, 0,
                [new VariationOrderScopeItemInput(Guid.NewGuid(), 999_999.00m)]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderInvalidStatusForTransition", result.Error);
    }

    // ---- ReturnForRevision ----

    [Fact]
    public async Task ReturnForRevision_Requires_The_Actor_To_Hold_A_Pending_Steps_Role()
    {
        var fixture = NewFixture();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 400_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 400_000.00m)]);
        vo.Submit([new(1, UserRole.PM, 1), new(2, UserRole.ProjectDirector, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(vo);
        var actionRepository = new FakeApprovalActionRepository();
        var handler = new ReturnVariationOrderForRevisionCommandHandler(
            fixture.Repository, actionRepository, new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(Guid.NewGuid(), UserRole.QS), new FakeClockForPayment(Now));

        var result = await handler.Handle(new ReturnVariationOrderForRevisionCommand(vo.Id, "Needs rework."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderNotAuthorizedForApprovalStep", result.Error);
    }

    [Fact]
    public async Task ReturnForRevision_Succeeds_For_A_Holder_Of_The_Current_Pending_Steps_Role_V10()
    {
        var fixture = NewFixture();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-022", Guid.NewGuid(), 400_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 400_000.00m)]);
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(vo);
        var actionRepository = new FakeApprovalActionRepository();
        var handler = new ReturnVariationOrderForRevisionCommandHandler(
            fixture.Repository, actionRepository, new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(Guid.NewGuid(), UserRole.PM), new FakeClockForPayment(Now));

        var result = await handler.Handle(new ReturnVariationOrderForRevisionCommand(vo.Id, "Please re-price at the new drawing revision."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VariationOrderStatus.Draft, result.Value.Status);
        Assert.Equal(2, vo.RevisionNo); // fixture V-10 step 2
        Assert.Empty(vo.ApprovalSteps);
        var action = Assert.Single(actionRepository.Actions);
        Assert.Equal(ApprovalActionType.ReturnForRevision, action.Action);
        Assert.Equal(1, action.RevisionNo); // closes out the revision that WAS pending, not the new one
    }

    /// <summary>L-01 (domain-rules.md §7.5, sprint-10 security review): fail closed rather than
    /// fabricate <c>Guid.Empty</c> onto the append-only evidence trail.</summary>
    [Fact]
    public async Task ReturnForRevision_Fails_Closed_With_ActorRequired_When_The_Actor_Is_Unresolvable()
    {
        var fixture = NewFixture();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 400_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 400_000.00m)]);
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(vo);
        var actionRepository = new FakeApprovalActionRepository();
        var handler = new ReturnVariationOrderForRevisionCommandHandler(
            fixture.Repository, actionRepository, new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(null, UserRole.PM), new FakeClockForPayment(Now));

        var result = await handler.Handle(new ReturnVariationOrderForRevisionCommand(vo.Id, "x"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderActorRequired", result.Error);
        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status); // untouched
        Assert.Empty(actionRepository.Actions);
    }

    // ---- Withdraw ----

    [Fact]
    public async Task Withdraw_Succeeds_For_The_Submitter_Before_Any_Vote()
    {
        var fixture = NewFixture();
        var submitterId = Guid.NewGuid();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 400_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 400_000.00m)]);
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, submitterId, Now);
        fixture.Repository.Seed(vo);
        var handler = new WithdrawVariationOrderCommandHandler(
            fixture.Repository, new FakeApprovalActionRepository(), new FakeCurrentUserContextForPayment(submitterId, UserRole.QS));

        var result = await handler.Handle(new WithdrawVariationOrderCommand(vo.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VariationOrderStatus.Draft, result.Value.Status);
        Assert.Equal(1, vo.RevisionNo); // unchanged, unlike ReturnForRevision
    }

    [Fact]
    public async Task Withdraw_Refuses_Once_A_Vote_Has_Been_Cast_This_Revision()
    {
        var fixture = NewFixture();
        var submitterId = Guid.NewGuid();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 400_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 400_000.00m)]);
        // First step's quorum is 2 - one vote can be cast without advancing CurrentStepNo at all,
        // which is exactly why the Withdraw guard cannot rely on CurrentStepNo alone.
        vo.Submit([new(1, UserRole.PM, 2)], Guid.NewGuid(), 1, false, submitterId, Now);
        fixture.Repository.Seed(vo);
        var actionRepository = new FakeApprovalActionRepository();
        actionRepository.Add(new ApprovalAction(
            fixture.TenantId, ApprovalDocumentType.VariationOrder, vo.Id, revisionNo: 1, stepNo: 1,
            Guid.NewGuid(), UserRole.PM, ApprovalActionType.Approve, comment: null, Now, Guid.NewGuid(), 1));
        var handler = new WithdrawVariationOrderCommandHandler(
            fixture.Repository, actionRepository, new FakeCurrentUserContextForPayment(submitterId, UserRole.QS));

        var result = await handler.Handle(new WithdrawVariationOrderCommand(vo.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderWithdrawAfterVoteCast", result.Error);
    }

    /// <summary>L-01 (domain-rules.md §7.5, sprint-10 security review): fail closed rather than
    /// fabricate <c>Guid.Empty</c> onto the append-only evidence trail. Before the fix, a null actor
    /// here reached <c>VariationOrder.Withdraw(Guid.Empty)</c>, which would have thrown a generic
    /// <see cref="Domain.Common.DomainException"/> ("Only the submitter may withdraw") rather than the
    /// specific, typed 401 this fix returns - masking the real problem (no authenticated user) behind
    /// an unrelated authorization error.</summary>
    [Fact]
    public async Task Withdraw_Fails_Closed_With_ActorRequired_When_The_Actor_Is_Unresolvable()
    {
        var fixture = NewFixture();
        var submitterId = Guid.NewGuid();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 400_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 400_000.00m)]);
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, submitterId, Now);
        fixture.Repository.Seed(vo);
        var handler = new WithdrawVariationOrderCommandHandler(
            fixture.Repository, new FakeApprovalActionRepository(), new FakeCurrentUserContextForPayment(null, UserRole.QS));

        var result = await handler.Handle(new WithdrawVariationOrderCommand(vo.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderActorRequired", result.Error);
        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status); // untouched
    }

    // ---- Cancel ----

    [Fact]
    public async Task Cancel_Succeeds_For_The_Creator_And_Requires_A_Comment_At_The_Command_Level()
    {
        var fixture = NewFixture();
        var creatorId = Guid.NewGuid();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", creatorId, 400_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 400_000.00m)]);
        fixture.Repository.Seed(vo);
        var handler = new CancelVariationOrderCommandHandler(
            fixture.Repository, new FakeApprovalActionRepository(), new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(creatorId, UserRole.QS), new FakeClockForPayment(Now));

        var result = await handler.Handle(new CancelVariationOrderCommand(vo.Id, "No longer required - scope dropped."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VariationOrderStatus.Cancelled, result.Value.Status);
    }

    [Fact]
    public async Task Cancel_Refuses_Someone_Who_Is_Neither_Creator_Nor_Pm()
    {
        var fixture = NewFixture();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 400_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 400_000.00m)]);
        fixture.Repository.Seed(vo);
        var handler = new CancelVariationOrderCommandHandler(
            fixture.Repository, new FakeApprovalActionRepository(), new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(Guid.NewGuid(), UserRole.QS), new FakeClockForPayment(Now));

        var result = await handler.Handle(new CancelVariationOrderCommand(vo.Id, "Trying to cancel someone else's VO."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderNotAuthorizedForApprovalStep", result.Error);
    }

    /// <summary>L-01 (domain-rules.md §7.5, sprint-10 security review): fail closed rather than
    /// fabricate <c>Guid.Empty</c> onto the append-only evidence trail.</summary>
    [Fact]
    public async Task Cancel_Fails_Closed_With_ActorRequired_When_The_Actor_Is_Unresolvable()
    {
        var fixture = NewFixture();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 400_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 400_000.00m)]);
        fixture.Repository.Seed(vo);
        var handler = new CancelVariationOrderCommandHandler(
            fixture.Repository, new FakeApprovalActionRepository(), new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(null, UserRole.PM), new FakeClockForPayment(Now));

        var result = await handler.Handle(new CancelVariationOrderCommand(vo.Id, "x"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderActorRequired", result.Error);
        Assert.Equal(VariationOrderStatus.Draft, vo.Status); // untouched
    }

    // ---- Get / List ----

    [Fact]
    public async Task GetVariationOrder_Returns_NotFound_For_An_Unknown_Id()
    {
        var fixture = NewFixture();
        var handler = new GetVariationOrderQueryHandler(fixture.Repository);

        var result = await handler.Handle(new GetVariationOrderQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderNotFound", result.Error);
    }

    [Fact]
    public async Task GetVariationOrder_Returns_The_Vo_When_It_Exists()
    {
        var fixture = NewFixture();
        var vo = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 400_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 400_000.00m)]);
        fixture.Repository.Seed(vo);
        var handler = new GetVariationOrderQueryHandler(fixture.Repository);

        var result = await handler.Handle(new GetVariationOrderQuery(vo.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(vo.Id, result.Value.Id);
    }

    [Fact]
    public async Task ListVariationOrders_Returns_Every_Vo_For_The_Project_And_Never_404s()
    {
        var fixture = NewFixture();
        var vo1 = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-1", Guid.NewGuid(), 100_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 100_000.00m)]);
        var vo2 = new Domain.Entities.VariationOrder(
            fixture.TenantId, fixture.ProjectId, "VO-2", Guid.NewGuid(), 200_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 200_000.00m)]);
        fixture.Repository.Seed(vo1);
        fixture.Repository.Seed(vo2);
        var handler = new ListVariationOrdersQueryHandler(fixture.Repository);

        var result = await handler.Handle(new ListVariationOrdersQuery(fixture.ProjectId), CancellationToken.None);
        var emptyResult = await handler.Handle(new ListVariationOrdersQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.True(emptyResult.IsSuccess);
        Assert.Empty(emptyResult.Value);
    }
}
