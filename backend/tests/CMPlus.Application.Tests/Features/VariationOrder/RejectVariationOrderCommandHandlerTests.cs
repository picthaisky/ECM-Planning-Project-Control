using CMPlus.Application.Features.VariationOrder.Commands.Approve;
using CMPlus.Application.Features.VariationOrder.Commands.Reject;
using CMPlus.Application.Features.VariationOrder.Commands.ReturnForRevision;
using CMPlus.Application.Tests.Features.Payment;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.VariationOrder;

/// <summary>
/// ADR-0016 / domain-rules.md §8: unlike Sprint 9's <c>PaymentCertificate</c> (which needed a
/// retrofit, <c>RejectPaymentCertificateCommandHandlerTests</c>'s own V-11 suite), the VO ships
/// quorum-bound rejection correctly from day one - this file proves that claim by execution rather
/// than trusting the doc comment, mirroring that same V-11a-f structure.
/// </summary>
public class RejectVariationOrderCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

    private sealed record Fixture(
        FakeVariationOrderRepository Repository,
        FakeApprovalActionRepository ActionRepository,
        FakeProjectRepository ProjectRepository,
        Guid TenantId);

    private static RejectVariationOrderCommandHandler CreateHandler(Fixture fixture, Guid actorUserId, UserRole actorRole) =>
        new(fixture.Repository, fixture.ActionRepository, new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(actorUserId, actorRole), new FakeClockForPayment(Now));

    /// <summary>The Approve handler needs a resolvable Project even though none of these tests reach
    /// the final (money-moving) step - <c>IProjectRepository.FindAsync</c> is called before the
    /// <c>wouldFinalize</c> branch, so <see cref="Fixture.ProjectRepository"/> must already carry a
    /// project matching the seeded VO's own <c>ProjectId</c> (see <see cref="SeedSubmittedVo"/>).</summary>
    private static ApproveVariationOrderCommandHandler CreateApproveHandler(Fixture fixture, Guid actorUserId, UserRole actorRole) =>
        new(
            fixture.Repository, fixture.ProjectRepository, new FakeApprovalPolicyReaderForPayment(),
            new CMPlus.Application.Approval.ApprovalRoutingService(), fixture.ActionRepository,
            new FakePaymentCertificateRepository(), new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(actorUserId, actorRole), new FakeClockForPayment(Now));

    private static (Fixture Fixture, Domain.Entities.VariationOrder Vo) SeedSubmittedVo(
        IReadOnlyList<VariationOrderApprovalStepInput> steps, Guid? createdBy = null, Guid? submittedBy = null, bool allowSelfApproval = false)
    {
        var tenantId = Guid.NewGuid();
        var projectRepository = new FakeProjectRepository();
        var project = Domain.Entities.Project.Create(
            tenantId, "Project", "P-1", "Owner", Now.AddYears(-1), Now.AddYears(1), bac: 100_000_000.00m, dataDate: Now);
        projectRepository.Seed(project);
        var fixture = new Fixture(new FakeVariationOrderRepository(), new FakeApprovalActionRepository(), projectRepository, tenantId);
        var vo = new Domain.Entities.VariationOrder(
            tenantId, project.Id, $"VO-{Guid.NewGuid():N}", createdBy ?? Guid.NewGuid(), 300_000.00m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 300_000.00m)]);
        vo.Submit(steps, Guid.NewGuid(), 1, allowSelfApproval, submittedBy ?? Guid.NewGuid(), Now);
        fixture.Repository.Seed(vo);
        return (fixture, vo);
    }

    [Fact]
    public async Task Handle_Refuses_An_Intermediate_Approver_Even_When_They_Hold_The_Eventual_Final_Steps_Role()
    {
        var (fixture, vo) = SeedSubmittedVo([new(1, UserRole.PM, 1), new(2, UserRole.ProjectDirector, 1)]);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector);

        var result = await handler.Handle(new RejectVariationOrderCommand(vo.Id, "Reject early."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderNotAuthorizedForApprovalStep", result.Error);
        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status);
    }

    [Fact]
    public async Task Handle_Allows_The_Final_Steps_Approver_To_Reject_Once_The_Chain_Reaches_Them()
    {
        var (fixture, vo) = SeedSubmittedVo([new(1, UserRole.PM, 1), new(2, UserRole.ProjectDirector, 1)]);
        var approveResult = await CreateApproveHandler(fixture, Guid.NewGuid(), UserRole.PM)
            .Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);
        Assert.True(approveResult.IsSuccess);
        Assert.Equal(2, vo.CurrentStepNo);

        var result = await CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector)
            .Handle(new RejectVariationOrderCommand(vo.Id, "Scope excluded, terminal."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VariationOrderStatus.Rejected, result.Value.Status);
        var action = Assert.Single(fixture.ActionRepository.Actions, a => a.Action == ApprovalActionType.Reject);
        Assert.Equal("Scope excluded, terminal.", action.Comment);
    }

    [Fact]
    public async Task Handle_Does_Not_Block_The_Creator_From_Rejecting_Their_Own_Submission_At_The_Final_Step()
    {
        // Deliberate: approval-workflow.md §6.1's self-approval restriction is scoped to "approve" -
        // it does not extend to Reject, for either document type.
        var creatorId = Guid.NewGuid();
        var (fixture, vo) = SeedSubmittedVo([new(1, UserRole.PM, 1)], createdBy: creatorId);

        var result = await CreateHandler(fixture, creatorId, UserRole.PM)
            .Handle(new RejectVariationOrderCommand(vo.Id, "Rejecting my own submission."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VariationOrderStatus.Rejected, result.Value.Status);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Vo_Does_Not_Exist()
    {
        var (fixture, _) = SeedSubmittedVo([new(1, UserRole.PM, 1)]);
        var result = await CreateHandler(fixture, Guid.NewGuid(), UserRole.PM)
            .Handle(new RejectVariationOrderCommand(Guid.NewGuid(), "x"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderNotFound", result.Error);
    }

    /// <summary>
    /// L-01 (domain-rules.md §7.5, sprint-10 security review): before the fix, this handler stamped
    /// <c>currentUser.UserId ?? Guid.Empty</c> onto an append-only evidence row instead of failing
    /// closed - Create/Approve already did this correctly, Reject/ReturnForRevision/Withdraw/Cancel
    /// did not. No test anywhere in this codebase exercised the null-actor case before this fix
    /// (grepped, confirmed absent) - this is genuinely new coverage, not a re-assertion.
    /// </summary>
    [Fact]
    public async Task Handle_Fails_Closed_With_ActorRequired_Rather_Than_Fabricating_Guid_Empty_When_The_Actor_Is_Unresolvable()
    {
        var (fixture, vo) = SeedSubmittedVo([new(1, UserRole.PM, 1)]);
        var handler = new RejectVariationOrderCommandHandler(
            fixture.Repository, fixture.ActionRepository, new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(null, UserRole.PM), new FakeClockForPayment(Now));

        var result = await handler.Handle(new RejectVariationOrderCommand(vo.Id, "x"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("VariationOrderActorRequired", result.Error);
        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status); // untouched
        Assert.Empty(fixture.ActionRepository.Actions); // nothing fabricated onto the evidence trail
    }

    // ---- ADR-0016 / domain-rules.md §8: "quorum binds rejection" (V-11a-f) ----

    private static (Fixture Fixture, Domain.Entities.VariationOrder Vo) SeedSubmittedVoWithFinalStepQuorum(int quorumCount) =>
        SeedSubmittedVoRaw([new VariationOrderApprovalStepInput(1, UserRole.ProjectDirector, quorumCount)]);

    private static (Fixture Fixture, Domain.Entities.VariationOrder Vo) SeedSubmittedVoRaw(IReadOnlyList<VariationOrderApprovalStepInput> steps) =>
        SeedSubmittedVo(steps);

    [Fact]
    public async Task Handle_A_QuorumCount_Two_Final_Step_Does_Not_Terminate_On_The_First_Reject_Vote_V11b_Part1()
    {
        var (fixture, vo) = SeedSubmittedVoWithFinalStepQuorum(quorumCount: 2);

        var result = await CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector)
            .Handle(new RejectVariationOrderCommand(vo.Id, "Not acceptable."), CancellationToken.None);

        Assert.True(result.IsSuccess); // the vote itself is accepted...
        Assert.Equal(VariationOrderStatus.PendingApproval, result.Value.Status); // ...but does not terminate
        var action = Assert.Single(fixture.ActionRepository.Actions);
        Assert.Equal(ApprovalActionType.Reject, action.Action);
        Assert.NotNull(vo.LastVoteAt); // N-03 parity: stamped even on a non-advancing vote
    }

    [Fact]
    public async Task Handle_A_QuorumCount_Two_Final_Step_Terminates_Once_A_Second_Distinct_Rejector_Votes_V11b_Part2()
    {
        var (fixture, vo) = SeedSubmittedVoWithFinalStepQuorum(quorumCount: 2);
        await CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector)
            .Handle(new RejectVariationOrderCommand(vo.Id, "First rejection."), CancellationToken.None);
        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status);

        var result = await CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector)
            .Handle(new RejectVariationOrderCommand(vo.Id, "Second rejection."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VariationOrderStatus.Rejected, result.Value.Status);
        Assert.Equal(2, fixture.ActionRepository.Actions.Count(a => a.Action == ApprovalActionType.Reject));
    }

    [Fact]
    public async Task Handle_QuorumCount_One_Reject_Is_Unaffected_Terminates_On_A_Single_Vote_As_Before()
    {
        var (fixture, vo) = SeedSubmittedVoWithFinalStepQuorum(quorumCount: 1);

        var result = await CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector)
            .Handle(new RejectVariationOrderCommand(vo.Id, "Not acceptable."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VariationOrderStatus.Rejected, result.Value.Status);
    }

    [Fact]
    public async Task Handle_Blocks_An_Actor_Who_Already_Approved_A_Different_Step_This_Revision_From_Then_Rejecting_V11a()
    {
        var (fixture, vo) = SeedSubmittedVo([new(1, UserRole.PM, 1), new(2, UserRole.PM, 1), new(3, UserRole.ProjectDirector, 1)]);
        var actorId = Guid.NewGuid();

        var approve1 = await CreateApproveHandler(fixture, actorId, UserRole.PM)
            .Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);
        Assert.True(approve1.IsSuccess);
        Assert.Equal(2, vo.CurrentStepNo);

        await CreateApproveHandler(fixture, Guid.NewGuid(), UserRole.PM)
            .Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);
        Assert.Equal(3, vo.CurrentStepNo);

        var rejectResult = await CreateHandler(fixture, actorId, UserRole.ProjectDirector)
            .Handle(new RejectVariationOrderCommand(vo.Id, "Trying to reject after already approving."), CancellationToken.None);

        Assert.True(rejectResult.IsFailure);
        Assert.Equal("VariationOrderDuplicateChainVoter", rejectResult.Error);
        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status);
    }

    [Fact]
    public async Task Handle_A_Split_Committee_Neither_Quorum_Reaches_But_ReturnForRevision_Still_Escapes_The_Deadlock_V11c_V11d()
    {
        var (fixture, vo) = SeedSubmittedVoWithFinalStepQuorum(quorumCount: 2);
        var approverId = Guid.NewGuid();
        var rejectorId = Guid.NewGuid();

        var approveResult = await CreateApproveHandler(fixture, approverId, UserRole.ProjectDirector)
            .Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);
        Assert.True(approveResult.IsSuccess);
        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status); // 1 approval, quorum 2 - not cleared

        var rejectResult = await CreateHandler(fixture, rejectorId, UserRole.ProjectDirector)
            .Handle(new RejectVariationOrderCommand(vo.Id, "I disagree."), CancellationToken.None);
        Assert.True(rejectResult.IsSuccess);
        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status); // 1 rejection, quorum 2 - not cleared either

        // Both voters are now blocked from voting again, in either direction.
        var blockedReject = await CreateHandler(fixture, approverId, UserRole.ProjectDirector)
            .Handle(new RejectVariationOrderCommand(vo.Id, "Switching my vote."), CancellationToken.None);
        Assert.True(blockedReject.IsFailure);
        Assert.Equal("VariationOrderDuplicateChainVoter", blockedReject.Error);

        var blockedApprove = await CreateApproveHandler(fixture, rejectorId, UserRole.ProjectDirector)
            .Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);
        Assert.True(blockedApprove.IsFailure);
        Assert.Equal("VariationOrderDuplicateChainVoter", blockedApprove.Error);

        // The escape valve: a THIRD ProjectDirector, no vote cast, ReturnForRevision - not quorum-bound.
        var thirdPartyReturnHandler = new ReturnVariationOrderForRevisionCommandHandler(
            fixture.Repository, fixture.ActionRepository, new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(Guid.NewGuid(), UserRole.ProjectDirector), new FakeClockForPayment(Now));
        var returnResult = await thirdPartyReturnHandler.Handle(
            new ReturnVariationOrderForRevisionCommand(vo.Id, "Split committee - returning for revision."), CancellationToken.None);

        Assert.True(returnResult.IsSuccess);
        Assert.Equal(VariationOrderStatus.Draft, returnResult.Value.Status); // deadlock escaped
        Assert.Equal(2, vo.RevisionNo);
        Assert.Empty(vo.ApprovalSteps);
    }

    /// <summary>
    /// docs/security/reviews/sprint-10.md §5: "<c>ReturnForRevision</c> carries no duplicate-voter
    /// guard on either document type ... It is load-bearing and undocumented -
    /// <c>ReturnForRevision</c> must never gain a <c>DuplicateChainVoter</c> guard; add a regression
    /// test naming this so a future 'consistency' cleanup cannot remove the escape." Distinct from
    /// <see cref="Handle_A_Split_Committee_Neither_Quorum_Reaches_But_ReturnForRevision_Still_Escapes_The_Deadlock_V11c_V11d"/>,
    /// which uses a FRESH third role-holder who never voted - this test uses the SAME actor who just
    /// voted, which is the shape that actually matters: <c>Approve</c>/<c>Reject</c> both reject a
    /// repeat voter via <c>DuplicateChainVoter</c>, so if <c>ReturnForRevision</c> ever gained the
    /// identical guard "for consistency", a <c>q=2</c> step holding one vote from each of the only two
    /// role-holders would have NO recovery path at all - the exact deadlock ADR-0016 was written to
    /// eliminate.
    /// </summary>
    [Fact]
    public async Task ReturnForRevision_Must_Never_Gain_A_DuplicateChainVoter_Guard_Even_For_An_Actor_Who_Already_Voted()
    {
        var (fixture, vo) = SeedSubmittedVoWithFinalStepQuorum(quorumCount: 2);
        var actorId = Guid.NewGuid();

        var approveResult = await CreateApproveHandler(fixture, actorId, UserRole.ProjectDirector)
            .Handle(new ApproveVariationOrderCommand(vo.Id, null), CancellationToken.None);
        Assert.True(approveResult.IsSuccess);
        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status); // 1 of 2 - not cleared

        // The SAME actor who just voted - not a fresh third party - returns the document for revision.
        // If DuplicateChainVoter ever applied here, this would fail; it must not.
        var returnHandler = new ReturnVariationOrderForRevisionCommandHandler(
            fixture.Repository, fixture.ActionRepository, new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(actorId, UserRole.ProjectDirector), new FakeClockForPayment(Now));

        var returnResult = await returnHandler.Handle(
            new ReturnVariationOrderForRevisionCommand(vo.Id, "Withdrawing my own vote via return."), CancellationToken.None);

        Assert.True(returnResult.IsSuccess, returnResult.IsFailure ? returnResult.Error : string.Empty);
        Assert.Equal(VariationOrderStatus.Draft, returnResult.Value.Status);
        Assert.Equal(2, vo.RevisionNo);
        Assert.Empty(vo.ApprovalSteps);
    }
}
