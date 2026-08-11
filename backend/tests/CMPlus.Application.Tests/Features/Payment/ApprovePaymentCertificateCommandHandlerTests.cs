using CMPlus.Application.Features.Payment.Commands.Approve;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Payment;

/// <summary>
/// S9-BE-05 DoD: a user who is not the current step's approver gets 403 (never an escape hatch of
/// any kind, including "PM can always approve"); self-approval is blocked unless the pinned policy
/// opted in (fixture R10); one human cannot satisfy two steps of the same chain. Authority is always
/// re-derived from the chain snapshotted onto the document at Submit time
/// (<see cref="PaymentCertificate.ApprovalSteps"/>) - security review sprint-09.md H-01 fix - never
/// re-queried from the policy store, so this handler no longer depends on
/// <c>IApprovalPolicyReader</c> at all. H-02 fix: a step's <c>QuorumCount</c> is enforced by counting
/// distinct approvers of that exact step from the append-only <c>ApprovalAction</c> history.
/// </summary>
public class ApprovePaymentCertificateCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    private static PaymentCertificate SubmittedCertificate(
        Guid tenantId, Guid projectId, IReadOnlyList<PaymentCertificateApprovalStepInput> steps, Guid policyId, int policyVersion,
        Guid createdBy, Guid submittedBy, bool allowSelfApproval = false, decimal grossCertifiedAmount = 5_000_000.00m)
    {
        var certificate = new PaymentCertificate(tenantId, projectId, 1, "IPC 1", grossCertifiedAmount, 0m, createdBy);
        certificate.SetPeriodClaim(100m, null, null, grossCertifiedAmount, 0m, 0m, grossCertifiedAmount);
        certificate.Submit(steps, policyId, policyVersion, allowSelfApproval, submittedBy, Now);
        return certificate;
    }

    private sealed record Fixture(
        FakePaymentCertificateRepository Repository,
        FakeApprovalActionRepository ActionRepository,
        Guid TenantId);

    private static (Fixture Fixture, ApprovePaymentCertificateCommandHandler Handler) CreateHandler(
        Fixture? existing, Guid? actorUserId, UserRole actorRole)
    {
        var fixture = existing ?? new Fixture(
            new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), Guid.NewGuid());

        var handler = new ApprovePaymentCertificateCommandHandler(
            fixture.Repository,
            fixture.ActionRepository,
            new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(actorUserId, actorRole),
            new FakeClockForPayment(Now));

        return (fixture, handler);
    }

    private static IReadOnlyList<PaymentCertificateApprovalStepInput> ThreeStepChain() =>
        [
            new(1, UserRole.QS, 1),
            new(2, UserRole.PM, 1),
            new(3, UserRole.ProjectDirector, 1),
        ];

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Certificate_Does_Not_Exist()
    {
        var (_, handler) = CreateHandler(null, Guid.NewGuid(), UserRole.QS);

        var result = await handler.Handle(new ApprovePaymentCertificateCommand(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotFound", result.Error);
    }

    [Fact]
    public async Task Handle_Returns_InvalidStatusForTransition_When_Not_PendingApproval()
    {
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 1_000_000m, 0m, Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate); // still Draft

        var (_, handler) = CreateHandler(fixtureBase, Guid.NewGuid(), UserRole.QS);
        var result = await handler.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateInvalidStatusForTransition", result.Error);
    }

    [Fact]
    public async Task Handle_Rejects_A_PM_Attempting_To_Approve_A_Step_That_Requires_QS_No_Escape_Hatch()
    {
        // The concrete "no PM escape hatch" proof at the handler level: PM is a legitimate,
        // authenticated business role, yet the current step requires QS - nothing about being a PM
        // grants authority here.
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), ThreeStepChain(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);

        var (_, handler) = CreateHandler(fixtureBase, Guid.NewGuid(), UserRole.PM);
        var result = await handler.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", result.Error);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status); // never advanced
        Assert.Equal(1, certificate.CurrentStepNo);
    }

    [Fact]
    public async Task Handle_Rejects_An_Admin_Attempting_To_Approve_Any_Step_Not_Their_Own_No_Escape_Hatch()
    {
        // Admin is the S9-BE-06 policy-write role, not a universal approver either.
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), ThreeStepChain(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);

        var (_, handler) = CreateHandler(fixtureBase, Guid.NewGuid(), UserRole.Admin);
        var result = await handler.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", result.Error);
    }

    [Fact]
    public async Task Handle_Blocks_The_Creator_From_Self_Approving_By_Default_R10_Style()
    {
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var creatorId = Guid.NewGuid();
        var certificate = SubmittedCertificate(
            tenantId, Guid.NewGuid(), ThreeStepChain(), Guid.NewGuid(), 1, creatorId, Guid.NewGuid(), allowSelfApproval: false);
        fixtureBase.Repository.Seed(certificate);

        var (_, handler) = CreateHandler(fixtureBase, creatorId, UserRole.QS);
        var result = await handler.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateSelfApprovalNotPermitted", result.Error);
    }

    [Fact]
    public async Task Handle_Blocks_The_Submitter_From_Self_Approving_By_Default_R10_Style()
    {
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var submitterId = Guid.NewGuid();
        var certificate = SubmittedCertificate(
            tenantId, Guid.NewGuid(), ThreeStepChain(), Guid.NewGuid(), 1, Guid.NewGuid(), submitterId, allowSelfApproval: false);
        fixtureBase.Repository.Seed(certificate);

        var (_, handler) = CreateHandler(fixtureBase, submitterId, UserRole.QS);
        var result = await handler.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateSelfApprovalNotPermitted", result.Error);
    }

    [Fact]
    public async Task Handle_Allows_Self_Approval_When_The_Pinned_Policy_Opted_In()
    {
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var submitterId = Guid.NewGuid();
        var certificate = SubmittedCertificate(
            tenantId, Guid.NewGuid(), ThreeStepChain(), Guid.NewGuid(), 1, Guid.NewGuid(), submitterId, allowSelfApproval: true);
        fixtureBase.Repository.Seed(certificate);

        var (_, handler) = CreateHandler(fixtureBase, submitterId, UserRole.QS);
        var result = await handler.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, certificate.CurrentStepNo);
    }

    [Fact]
    public async Task Handle_Blocks_The_Same_Human_From_Satisfying_A_Second_Step_Of_The_Same_Chain()
    {
        // approval-workflow.md §6.1: even when the same actor genuinely holds the role a later step
        // requires, they may not approve two steps of one chain - each step needs a distinct human.
        // Constructed with two QS-required steps (a valid, if unusual, policy configuration) since a
        // real user only ever carries one fixed role.
        var tenantId = Guid.NewGuid();
        IReadOnlyList<PaymentCertificateApprovalStepInput> twoQsSteps = [new(1, UserRole.QS, 1), new(2, UserRole.QS, 1)];
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var actorId = Guid.NewGuid();
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), twoQsSteps, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);

        var (fixture1, handler1) = CreateHandler(fixtureBase, actorId, UserRole.QS);
        var firstApproval = await handler1.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);
        Assert.True(firstApproval.IsSuccess);
        Assert.Equal(2, certificate.CurrentStepNo);

        var (_, handler2) = CreateHandler(fixture1, actorId, UserRole.QS);
        var secondApproval = await handler2.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(secondApproval.IsFailure);
        Assert.Equal("PaymentCertificateDuplicateChainVoter", secondApproval.Error);
    }

    [Fact]
    public async Task Handle_Blocks_An_Actor_Who_Already_Cast_A_Non_Terminal_Reject_This_Revision_From_Then_Approving_ADR_0016()
    {
        // ADR-0016 / domain-rules.md §8.3 (V-11a, inverted): DuplicateChainVoter widens
        // DuplicateChainApprover to Action ∈ {Approve, Reject} - an actor who already rejected this
        // revision may not later approve, even the SAME step. QuorumCount=2 on a single-step chain
        // is deliberate: it is the only shape where a Reject vote is both legal (final step) AND
        // non-terminal (quorum not yet reached), so there is a "later" for the same actor to attempt
        // an Approve at all - with QuorumCount=1 the Reject would already be terminal.
        var tenantId = Guid.NewGuid();
        IReadOnlyList<PaymentCertificateApprovalStepInput> quorumTwoFinalStep = [new(1, UserRole.QS, QuorumCount: 2)];
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var actorId = Guid.NewGuid();
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), quorumTwoFinalStep, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);

        var rejectHandler = new CMPlus.Application.Features.Payment.Commands.Reject.RejectPaymentCertificateCommandHandler(
            fixtureBase.Repository,
            fixtureBase.ActionRepository,
            new FakeTenantProviderForPayment(tenantId),
            new FakeCurrentUserContextForPayment(actorId, UserRole.QS),
            new FakeClockForPayment(Now));
        var rejectResult = await rejectHandler.Handle(
            new CMPlus.Application.Features.Payment.Commands.Reject.RejectPaymentCertificateCommand(certificate.Id, "Not acceptable."),
            CancellationToken.None);
        Assert.True(rejectResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status); // 1 of 2 rejectors - not yet terminal

        var (_, approveHandler) = CreateHandler(fixtureBase, actorId, UserRole.QS);
        var approveResult = await approveHandler.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(approveResult.IsFailure);
        Assert.Equal("PaymentCertificateDuplicateChainVoter", approveResult.Error);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status); // still unresolved, not silently advanced
    }

    [Fact]
    public async Task Handle_A_Different_Human_Holding_The_Same_Role_May_Still_Clear_The_Second_Step()
    {
        var tenantId = Guid.NewGuid();
        IReadOnlyList<PaymentCertificateApprovalStepInput> twoQsSteps = [new(1, UserRole.QS, 1), new(2, UserRole.QS, 1)];
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), twoQsSteps, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);

        var (fixture1, handler1) = CreateHandler(fixtureBase, Guid.NewGuid(), UserRole.QS);
        await handler1.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        var (_, handler2) = CreateHandler(fixture1, Guid.NewGuid(), UserRole.QS);
        var result = await handler2.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Certified, certificate.Status);
    }

    [Fact]
    public async Task Handle_On_The_Final_Step_Certifies_And_Posts_Retention_And_Advance_Recovery_Ledger_Entries()
    {
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 21_600_000.00m, 0m, Guid.NewGuid());
        certificate.SetPeriodClaim(100m, null, null, 21_600_000.00m, 1_080_000.00m, 2_160_000.00m, 18_360_000.00m);
        certificate.Submit([new(1, UserRole.QS, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        fixtureBase.Repository.Seed(certificate);

        var (_, handler) = CreateHandler(fixtureBase, Guid.NewGuid(), UserRole.QS);
        var result = await handler.Handle(new ApprovePaymentCertificateCommand(certificate.Id, "Looks correct."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Certified, result.Value.Status);
        Assert.Equal(2, fixtureBase.Repository.AddedLedgerEntries.Count);
        Assert.Contains(fixtureBase.Repository.AddedLedgerEntries, e => e.Category == FinanceLedgerCategory.Retention && e.Amount == 1_080_000.00m);
        Assert.Contains(fixtureBase.Repository.AddedLedgerEntries, e => e.Category == FinanceLedgerCategory.Advance && e.Amount == 2_160_000.00m);

        var action = Assert.Single(fixtureBase.ActionRepository.Actions);
        Assert.Equal("Looks correct.", action.Comment);
        Assert.Equal(1, action.StepNo);
    }

    [Fact]
    public async Task Handle_On_A_Non_Final_Step_Advances_Without_Posting_Any_Ledger_Entries()
    {
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), ThreeStepChain(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);

        var (_, handler) = CreateHandler(fixtureBase, Guid.NewGuid(), UserRole.QS);
        var result = await handler.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, result.Value.Status);
        Assert.Equal(2, result.Value.CurrentStepNo);
        Assert.Empty(fixtureBase.Repository.AddedLedgerEntries);
    }

    [Fact]
    public async Task Handle_Returns_ConcurrencyConflict_When_The_Repository_Save_Reports_A_Conflict()
    {
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), [new(1, UserRole.QS, 1)], Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);
        fixtureBase.Repository.SaveShouldSucceed = false;

        var (_, handler) = CreateHandler(fixtureBase, Guid.NewGuid(), UserRole.QS);
        var result = await handler.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateConcurrencyConflict", result.Error);
    }

    [Fact]
    public async Task Handle_Returns_CorruptApprovalChain_Instead_Of_Throwing_When_The_Snapshot_Has_No_Rung_For_The_Current_Step()
    {
        // security review sprint-09.md M-03: this shape (two rungs sharing StepNo 1, so TotalSteps
        // overcounts distinct StepNos and StepNo 2 has no rung at all) is supposed to be unreachable
        // now that ApprovalPolicy.ValidateBands rejects it at construction time - proven independently
        // by ApprovalPolicyTests. This test proves the decision-time defense-in-depth guard for
        // whatever shape of corruption might still reach a certificate: it degrades to a mapped 409,
        // never an unhandled 500 (M-03's second half of the fix).
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        IReadOnlyList<PaymentCertificateApprovalStepInput> corruptSteps = [new(1, UserRole.QS, 1), new(1, UserRole.PM, 1)];
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), corruptSteps, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);

        var (fixture1, handler1) = CreateHandler(fixtureBase, Guid.NewGuid(), UserRole.QS);
        var firstApproval = await handler1.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);
        Assert.True(firstApproval.IsSuccess); // step 1's QS rung resolves fine, advances CurrentStepNo to 2
        Assert.Equal(2, certificate.CurrentStepNo);

        var (_, handler2) = CreateHandler(fixture1, Guid.NewGuid(), UserRole.PM);
        var result = await handler2.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateCorruptApprovalChain", result.Error);
    }

    // ---- H-02: QuorumCount enforcement (security review sprint-09.md) ----

    [Fact]
    public async Task Handle_A_QuorumCount_Two_Step_Does_Not_Advance_On_The_First_Vote()
    {
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        IReadOnlyList<PaymentCertificateApprovalStepInput> quorumTwoStep = [new(1, UserRole.QS, QuorumCount: 2)];
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), quorumTwoStep, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);

        var (_, handler) = CreateHandler(fixtureBase, Guid.NewGuid(), UserRole.QS);
        var result = await handler.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess); // the vote itself is accepted...
        Assert.Equal(PaymentCertificateStatus.PendingApproval, result.Value.Status); // ...but does not clear the step
        Assert.Equal(1, result.Value.CurrentStepNo);
        Assert.Empty(fixtureBase.Repository.AddedLedgerEntries); // and certainly does not certify/post ledger rows
        var action = Assert.Single(fixtureBase.ActionRepository.Actions);
        Assert.Equal(ApprovalActionType.Approve, action.Action); // the vote IS recorded on the append-only ledger
    }

    [Fact]
    public async Task Handle_A_QuorumCount_Two_Step_Advances_Once_A_Second_Distinct_Approver_Votes()
    {
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        IReadOnlyList<PaymentCertificateApprovalStepInput> quorumTwoStep = [new(1, UserRole.QS, QuorumCount: 2)];
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), quorumTwoStep, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);

        var (fixture1, handler1) = CreateHandler(fixtureBase, Guid.NewGuid(), UserRole.QS);
        await handler1.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        var (_, handler2) = CreateHandler(fixture1, Guid.NewGuid(), UserRole.QS);
        var result = await handler2.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Certified, result.Value.Status);
        Assert.Equal(2, fixtureBase.ActionRepository.Actions.Count(a => a.Action == ApprovalActionType.Approve));
    }

    [Fact]
    public async Task Handle_A_QuorumCount_Two_Step_Reconciles_With_DuplicateChainVoter_The_Same_Actor_Cannot_Vote_Twice()
    {
        // security review sprint-09.md H-02 fix guidance: reconciling quorum with the pre-existing
        // DuplicateChainVoter rule (ADR-0016; was DuplicateChainApprover) - a single actor's second
        // Approve call must still be refused, not silently counted as a second, distinct quorum vote.
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        IReadOnlyList<PaymentCertificateApprovalStepInput> quorumTwoStep = [new(1, UserRole.QS, QuorumCount: 2)];
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), quorumTwoStep, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);
        var actorId = Guid.NewGuid();

        var (fixture1, handler1) = CreateHandler(fixtureBase, actorId, UserRole.QS);
        var first = await handler1.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var (_, handler2) = CreateHandler(fixture1, actorId, UserRole.QS);
        var second = await handler2.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal("PaymentCertificateDuplicateChainVoter", second.Error);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status); // still waiting on a distinct second approver
    }

    [Fact]
    public async Task Handle_Quorum_Counting_Is_Scoped_To_The_Current_Step_Not_The_Whole_Revision()
    {
        // A prior approval on step 1 (quorum 1, already cleared) must not count toward step 2's own
        // QuorumCount of 2 - each step's quorum is independent.
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        IReadOnlyList<PaymentCertificateApprovalStepInput> steps =
            [new(1, UserRole.QS, QuorumCount: 1), new(2, UserRole.PM, QuorumCount: 2)];
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), steps, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);

        var (fixture1, qsHandler) = CreateHandler(fixtureBase, Guid.NewGuid(), UserRole.QS);
        var afterQs = await qsHandler.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);
        Assert.True(afterQs.IsSuccess);
        Assert.Equal(2, afterQs.Value.CurrentStepNo); // step 1 cleared on quorum 1

        var (fixture2, pmHandler1) = CreateHandler(fixture1, Guid.NewGuid(), UserRole.PM);
        var afterFirstPm = await pmHandler1.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);
        Assert.True(afterFirstPm.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, afterFirstPm.Value.Status); // NOT certified - step 2 needs 2 PM votes
        Assert.Equal(2, afterFirstPm.Value.CurrentStepNo);

        var (_, pmHandler2) = CreateHandler(fixture2, Guid.NewGuid(), UserRole.PM);
        var afterSecondPm = await pmHandler2.Handle(new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);
        Assert.True(afterSecondPm.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Certified, afterSecondPm.Value.Status);
    }

    /// <summary>
    /// S9 finding L-01, widened in Sprint 11. Unreachable behind <c>[Authorize]</c> today, but the
    /// old <c>currentUser.UserId ?? Guid.Empty</c> would have attributed a payment certification —
    /// an append-only legal-evidence row — to nobody. It also silently defeated the self-approval
    /// guard, since a real submitter id can never equal <c>Guid.Empty</c>, so the comparison could
    /// not match. Fail closed instead, and never write the ApprovalAction.
    /// </summary>
    [Fact]
    public async Task Handle_Fails_Closed_When_No_Authenticated_User_Can_Be_Resolved()
    {
        var tenantId = Guid.NewGuid();
        var fixtureBase = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), ThreeStepChain(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());
        fixtureBase.Repository.Seed(certificate);
        var (fixture, handler) = CreateHandler(fixtureBase, actorUserId: null, actorRole: UserRole.QS);

        var result = await handler.Handle(
            new ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateActorRequired", result.Error);
        // The guard must run before anything is written - no evidence row for a nobody actor.
        Assert.Empty(fixture.ActionRepository.Actions);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status);
    }
}
