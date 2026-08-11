using CMPlus.Application.Features.Payment.Commands.Reject;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Payment;

/// <summary>approval-workflow.md §4/§6.1: only the final step's approver may reject; intermediate
/// approvers may only ReturnForRevision. Authority is resolved from the chain snapshotted onto the
/// document at Submit time (security review sprint-09.md H-01 fix).</summary>
public class RejectPaymentCertificateCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    private static IReadOnlyList<PaymentCertificateApprovalStepInput> ThreeStepChain() =>
        [
            new(1, UserRole.QS, 1),
            new(2, UserRole.PM, 1),
            new(3, UserRole.ProjectDirector, 1),
        ];

    private sealed record Fixture(
        FakePaymentCertificateRepository Repository, FakeApprovalActionRepository ActionRepository, Guid TenantId);

    private static RejectPaymentCertificateCommandHandler CreateHandler(Fixture fixture, Guid actorUserId, UserRole actorRole) =>
        new(
            fixture.Repository,
            fixture.ActionRepository,
            new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(actorUserId, actorRole),
            new FakeClockForPayment(Now));

    private static CMPlus.Application.Features.Payment.Commands.Approve.ApprovePaymentCertificateCommandHandler CreateApproveHandler(
        Fixture fixture, Guid actorUserId, UserRole actorRole) =>
        new(
            fixture.Repository,
            fixture.ActionRepository,
            new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(actorUserId, actorRole),
            new FakeClockForPayment(Now));

    private static (Fixture Fixture, PaymentCertificate Certificate) SeedSubmittedCertificate(
        bool allowSelfApproval = false, Guid? createdBy = null, Guid? submittedBy = null)
    {
        var tenantId = Guid.NewGuid();
        var fixture = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);

        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 5_000_000.00m, 0m, createdBy ?? Guid.NewGuid());
        certificate.SetPeriodClaim(100m, null, null, 5_000_000.00m, 0m, 0m, 5_000_000.00m);
        certificate.Submit(ThreeStepChain(), Guid.NewGuid(), 1, allowSelfApproval, submittedBy ?? Guid.NewGuid(), Now);

        fixture.Repository.Seed(certificate);
        return (fixture, certificate);
    }

    [Fact]
    public async Task Handle_Refuses_An_Intermediate_Approver_Even_When_They_Hold_The_Eventual_Final_Steps_Role()
    {
        // ProjectDirector genuinely holds the step-3 (final) role, but the certificate is still at
        // step 1 (QS) - Reject is refused; only ReturnForRevision is available to them at this point.
        var (fixture, certificate) = SeedSubmittedCertificate();
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector);

        var result = await handler.Handle(new RejectPaymentCertificateCommand(certificate.Id, "Reject early."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", result.Error);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status);
    }

    [Fact]
    public async Task Handle_Refuses_The_Current_Steps_Own_Approver_When_Not_Yet_At_The_Final_Step()
    {
        var (fixture, certificate) = SeedSubmittedCertificate();
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.QS); // current step's own role, but not final

        var result = await handler.Handle(new RejectPaymentCertificateCommand(certificate.Id, "No."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", result.Error);
    }

    [Fact]
    public async Task Handle_Allows_The_Final_Steps_Approver_To_Reject_Once_The_Chain_Reaches_Them()
    {
        var (fixture, certificate) = SeedSubmittedCertificate();

        // Clear steps 1 and 2 first so CurrentStepNo reaches the final step (3, ProjectDirector).
        var approveHandler1 = CreateApproveHandler(fixture, Guid.NewGuid(), UserRole.QS);
        await approveHandler1.Handle(new CMPlus.Application.Features.Payment.Commands.Approve.ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);
        var approveHandler2 = CreateApproveHandler(fixture, Guid.NewGuid(), UserRole.PM);
        await approveHandler2.Handle(new CMPlus.Application.Features.Payment.Commands.Approve.ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);
        Assert.Equal(3, certificate.CurrentStepNo);

        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector);
        var result = await handler.Handle(new RejectPaymentCertificateCommand(certificate.Id, "Scope excluded, terminal."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Rejected, result.Value.Status);
        var action = Assert.Single(fixture.ActionRepository.Actions, a => a.Action == ApprovalActionType.Reject);
        Assert.Equal("Scope excluded, terminal.", action.Comment);
        Assert.Equal(3, action.StepNo);
    }

    [Fact]
    public async Task Handle_On_A_Single_Step_Chain_Allows_That_Steps_Approver_To_Reject_Immediately()
    {
        var tenantId = Guid.NewGuid();
        var fixture = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 1_000_000.00m, 0m, Guid.NewGuid());
        certificate.SetPeriodClaim(100m, null, null, 1_000_000.00m, 0m, 0m, 1_000_000.00m);
        certificate.Submit([new(1, UserRole.QS, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(certificate);

        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.QS);
        var result = await handler.Handle(new RejectPaymentCertificateCommand(certificate.Id, "Not acceptable."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Rejected, result.Value.Status);
    }

    [Fact]
    public async Task Handle_Does_Not_Block_The_Creator_From_Rejecting_Their_Own_Submission_At_The_Final_Step()
    {
        // Deliberate: approval-workflow.md §6.1's self-approval restriction is scoped to "approve"
        // only - it does not extend to Reject.
        var creatorId = Guid.NewGuid();
        var (fixture, certificate) = SeedSubmittedCertificate(createdBy: creatorId);

        var approveHandler1 = CreateApproveHandler(fixture, Guid.NewGuid(), UserRole.QS);
        await approveHandler1.Handle(new CMPlus.Application.Features.Payment.Commands.Approve.ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);
        var approveHandler2 = CreateApproveHandler(fixture, Guid.NewGuid(), UserRole.PM);
        await approveHandler2.Handle(new CMPlus.Application.Features.Payment.Commands.Approve.ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        var handler = CreateHandler(fixture, creatorId, UserRole.ProjectDirector);
        var result = await handler.Handle(new RejectPaymentCertificateCommand(certificate.Id, "Rejecting my own submission."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Rejected, result.Value.Status);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Certificate_Does_Not_Exist()
    {
        var (fixture, _) = SeedSubmittedCertificate();
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector);

        var result = await handler.Handle(new RejectPaymentCertificateCommand(Guid.NewGuid(), "x"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotFound", result.Error);
    }

    [Fact]
    public async Task Handle_Returns_ConcurrencyConflict_When_The_Repository_Save_Reports_A_Conflict()
    {
        var tenantId = Guid.NewGuid();
        var fixture = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 1_000_000.00m, 0m, Guid.NewGuid());
        certificate.SetPeriodClaim(100m, null, null, 1_000_000.00m, 0m, 0m, 1_000_000.00m);
        certificate.Submit([new(1, UserRole.QS, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(certificate);
        fixture.Repository.SaveShouldSucceed = false;

        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.QS);
        var result = await handler.Handle(new RejectPaymentCertificateCommand(certificate.Id, "x"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateConcurrencyConflict", result.Error);
    }

    // ---- ADR-0016 / domain-rules.md §8: "quorum binds rejection" (N-05 fix, V-11a-f) ----

    private static (Fixture Fixture, PaymentCertificate Certificate) SeedSubmittedCertificateWithFinalStepQuorum(int quorumCount)
    {
        var tenantId = Guid.NewGuid();
        var fixture = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 1_000_000.00m, 0m, Guid.NewGuid());
        certificate.SetPeriodClaim(100m, null, null, 1_000_000.00m, 0m, 0m, 1_000_000.00m);
        certificate.Submit([new(1, UserRole.ProjectDirector, quorumCount)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(certificate);
        return (fixture, certificate);
    }

    [Fact]
    public async Task Handle_A_QuorumCount_Two_Final_Step_Does_Not_Terminate_On_The_First_Reject_Vote_V11b_Part1()
    {
        // V-11b, step 1: PD-A rejects a QuorumCount=2 step - vote recorded, still PendingApproval.
        // Before ADR-0016 this single rejector would have terminated the document immediately
        // regardless of QuorumCount (the exact N-05 defect).
        var (fixture, certificate) = SeedSubmittedCertificateWithFinalStepQuorum(quorumCount: 2);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector);

        var result = await handler.Handle(new RejectPaymentCertificateCommand(certificate.Id, "Not acceptable."), CancellationToken.None);

        Assert.True(result.IsSuccess); // the vote itself is accepted...
        Assert.Equal(PaymentCertificateStatus.PendingApproval, result.Value.Status); // ...but does not terminate the document
        var action = Assert.Single(fixture.ActionRepository.Actions);
        Assert.Equal(ApprovalActionType.Reject, action.Action); // the vote IS recorded on the append-only ledger
        Assert.NotNull(certificate.LastVoteAt); // N-03 parity: stamped even on a non-advancing vote
    }

    [Fact]
    public async Task Handle_A_QuorumCount_Two_Final_Step_Terminates_Once_A_Second_Distinct_Rejector_Votes_V11b_Part2()
    {
        // V-11b, step 2: PD-A rejects (1 of 2), then PD-B (a distinct actor) rejects -> terminal.
        var (fixture, certificate) = SeedSubmittedCertificateWithFinalStepQuorum(quorumCount: 2);
        var firstHandler = CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector);
        await firstHandler.Handle(new RejectPaymentCertificateCommand(certificate.Id, "First rejection."), CancellationToken.None);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status);

        var secondHandler = CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector);
        var result = await secondHandler.Handle(new RejectPaymentCertificateCommand(certificate.Id, "Second rejection."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Rejected, result.Value.Status);
        Assert.Equal(2, fixture.ActionRepository.Actions.Count(a => a.Action == ApprovalActionType.Reject));
    }

    [Fact]
    public async Task Handle_QuorumCount_One_Reject_Is_Unaffected_Terminates_On_A_Single_Vote_As_Before()
    {
        // ADR-0016's blast-radius claim, asserted directly against the handler (not just the Domain
        // method): QuorumCount=1 is the default and overwhelmingly common configuration and must see
        // zero behavioural difference.
        var (fixture, certificate) = SeedSubmittedCertificateWithFinalStepQuorum(quorumCount: 1);
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector);

        var result = await handler.Handle(new RejectPaymentCertificateCommand(certificate.Id, "Not acceptable."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Rejected, result.Value.Status);
    }

    [Fact]
    public async Task Handle_Blocks_The_Same_Actor_From_Rejecting_Twice_Toward_Their_Own_Steps_Quorum_DuplicateChainVoter()
    {
        // V-11a's mirror image within Reject alone (not the cross-action case): a QuorumCount=2 step
        // cannot be satisfied by the same rejector voting twice.
        var (fixture, certificate) = SeedSubmittedCertificateWithFinalStepQuorum(quorumCount: 2);
        var actorId = Guid.NewGuid();
        var firstHandler = CreateHandler(fixture, actorId, UserRole.ProjectDirector);
        var first = await firstHandler.Handle(new RejectPaymentCertificateCommand(certificate.Id, "First."), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var secondHandler = CreateHandler(fixture, actorId, UserRole.ProjectDirector);
        var second = await secondHandler.Handle(new RejectPaymentCertificateCommand(certificate.Id, "Trying again."), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal("PaymentCertificateDuplicateChainVoter", second.Error);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status); // still waiting on a distinct second rejector
    }

    [Fact]
    public async Task Handle_Blocks_An_Actor_Who_Already_Approved_A_Different_Step_This_Revision_From_Then_Rejecting_V11a()
    {
        // V-11a (the exact N-05 execution-verified scenario): an actor who approved 1-of-2 elsewhere
        // in the chain may not then reject at the final step. Distinct StepNos so this genuinely
        // exercises the "any StepNo, same revision" scope of DuplicateChainVoter, not merely
        // per-step duplication.
        var (fixture, certificate) = SeedSubmittedCertificate(); // 3-step chain: QS(1), PM(2), ProjectDirector(3)
        var actorId = Guid.NewGuid();

        var stepOneApprover = CreateApproveHandler(fixture, actorId, UserRole.QS);
        var approveResult = await stepOneApprover.Handle(
            new CMPlus.Application.Features.Payment.Commands.Approve.ApprovePaymentCertificateCommand(certificate.Id, null),
            CancellationToken.None);
        Assert.True(approveResult.IsSuccess);
        Assert.Equal(2, certificate.CurrentStepNo);

        var stepTwoApprover = CreateApproveHandler(fixture, Guid.NewGuid(), UserRole.PM);
        await stepTwoApprover.Handle(
            new CMPlus.Application.Features.Payment.Commands.Approve.ApprovePaymentCertificateCommand(certificate.Id, null),
            CancellationToken.None);
        Assert.Equal(3, certificate.CurrentStepNo); // final step reached

        // The same physical actor is now recorded as attempting to reject at the final step, holding
        // whatever role that step requires - DuplicateChainVoter must fire purely on ActorUserId,
        // independent of ActorRoleAtTime, exactly like its Approve-only predecessor.
        var rejectHandler = CreateHandler(fixture, actorId, UserRole.ProjectDirector);
        var rejectResult = await rejectHandler.Handle(
            new RejectPaymentCertificateCommand(certificate.Id, "Trying to reject after already approving."), CancellationToken.None);

        Assert.True(rejectResult.IsFailure);
        Assert.Equal("PaymentCertificateDuplicateChainVoter", rejectResult.Error);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status); // never reached Rejected
    }

    [Fact]
    public async Task Handle_A_Split_Committee_Neither_Quorum_Reaches_But_ReturnForRevision_Still_Escapes_The_Deadlock_V11c_V11d()
    {
        // V-11c + V-11d: a QuorumCount=2 step holding one Approve and one Reject satisfies neither
        // quorum and both voters are now blocked (DuplicateChainVoter) - the domain-rules.md §8.4
        // deadlock. ReturnForRevision is deliberately NOT quorum-bound, so a third holder of the
        // pending step's role can still send it back - proving the escape valve genuinely works, not
        // merely that it compiles.
        var (fixture, certificate) = SeedSubmittedCertificateWithFinalStepQuorum(quorumCount: 2);
        var approverId = Guid.NewGuid();
        var rejectorId = Guid.NewGuid();

        var approveHandler = CreateApproveHandler(fixture, approverId, UserRole.ProjectDirector);
        var approveResult = await approveHandler.Handle(
            new CMPlus.Application.Features.Payment.Commands.Approve.ApprovePaymentCertificateCommand(certificate.Id, null),
            CancellationToken.None);
        Assert.True(approveResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status); // 1 approval, quorum 2 - not cleared

        var rejectHandler = CreateHandler(fixture, rejectorId, UserRole.ProjectDirector);
        var rejectResult = await rejectHandler.Handle(
            new RejectPaymentCertificateCommand(certificate.Id, "I disagree."), CancellationToken.None);
        Assert.True(rejectResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status); // 1 rejection, quorum 2 - not cleared either

        // Both voters are now blocked from voting again, in either direction - the deadlock.
        var approverTriesRejectNow = CreateHandler(fixture, approverId, UserRole.ProjectDirector);
        var blockedReject = await approverTriesRejectNow.Handle(
            new RejectPaymentCertificateCommand(certificate.Id, "Switching my vote."), CancellationToken.None);
        Assert.True(blockedReject.IsFailure);
        Assert.Equal("PaymentCertificateDuplicateChainVoter", blockedReject.Error);

        var rejectorTriesApproveNow = CreateApproveHandler(fixture, rejectorId, UserRole.ProjectDirector);
        var blockedApprove = await rejectorTriesApproveNow.Handle(
            new CMPlus.Application.Features.Payment.Commands.Approve.ApprovePaymentCertificateCommand(certificate.Id, null),
            CancellationToken.None);
        Assert.True(blockedApprove.IsFailure);
        Assert.Equal("PaymentCertificateDuplicateChainVoter", blockedApprove.Error);

        // The escape valve: a THIRD ProjectDirector, who has cast no vote at all this revision, still
        // holds the (only, currently pending) step's role and may ReturnForRevision - not quorum-bound.
        var thirdPartyReturnHandler = new CMPlus.Application.Features.Payment.Commands.ReturnForRevision.ReturnPaymentCertificateForRevisionCommandHandler(
            fixture.Repository,
            fixture.ActionRepository,
            new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(Guid.NewGuid(), UserRole.ProjectDirector),
            new FakeClockForPayment(Now));
        var returnResult = await thirdPartyReturnHandler.Handle(
            new CMPlus.Application.Features.Payment.Commands.ReturnForRevision.ReturnPaymentCertificateForRevisionCommand(
                certificate.Id, "Split committee - returning for revision."),
            CancellationToken.None);

        Assert.True(returnResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Draft, returnResult.Value.Status); // deadlock escaped, never permanently stuck
        Assert.Equal(2, certificate.RevisionNo);
        Assert.Empty(certificate.ApprovalSteps); // chain snapshot voided, exactly like any other ReturnForRevision
    }
}
