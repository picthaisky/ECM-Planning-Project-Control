using CMPlus.Application.Features.Payment.Commands.ReturnForRevision;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Payment;

/// <summary>approval-workflow.md §4: guard is "actor holds <b>any pending</b> step's role" - the
/// distinguishing feature from Approve/Reject, which only ever look at one specific step. Resolved
/// from the chain snapshotted onto the document at Submit time (security review sprint-09.md H-01
/// fix).</summary>
public class ReturnPaymentCertificateForRevisionCommandHandlerTests
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

    private static ReturnPaymentCertificateForRevisionCommandHandler CreateHandler(Fixture fixture, Guid actorUserId, UserRole actorRole) =>
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

    private static (Fixture Fixture, PaymentCertificate Certificate) SeedSubmittedCertificate()
    {
        var tenantId = Guid.NewGuid();
        var fixture = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);

        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 5_000_000.00m, 0m, Guid.NewGuid());
        certificate.SetPeriodClaim(100m, null, null, 5_000_000.00m, 0m, 0m, 5_000_000.00m);
        certificate.Submit(ThreeStepChain(), Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        fixture.Repository.Seed(certificate);

        return (fixture, certificate);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Certificate_Does_Not_Exist()
    {
        var (fixture, _) = SeedSubmittedCertificate();
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.QS);

        var result = await handler.Handle(new ReturnPaymentCertificateForRevisionCommand(Guid.NewGuid(), "x"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotFound", result.Error);
    }

    [Fact]
    public async Task Handle_Allows_The_Current_Steps_Approver_To_Return_For_Revision()
    {
        var (fixture, certificate) = SeedSubmittedCertificate();
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.QS); // current step (1) is QS

        var result = await handler.Handle(
            new ReturnPaymentCertificateForRevisionCommand(certificate.Id, "Quantities look wrong."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Draft, result.Value.Status);
        Assert.Equal(2, result.Value.RevisionNo);
    }

    [Fact]
    public async Task Handle_Allows_A_Later_Not_Yet_Reached_Steps_Approver_To_Return_For_Revision_Early()
    {
        // approval-workflow.md §4: "any pending step's role" - ProjectDirector (step 3) has not had
        // their turn yet (current step is 1, QS) but is still a pending approver and may act now.
        var (fixture, certificate) = SeedSubmittedCertificate();
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.ProjectDirector);

        var result = await handler.Handle(
            new ReturnPaymentCertificateForRevisionCommand(certificate.Id, "Scope is wrong, fix before it even reaches me."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Draft, result.Value.Status);
    }

    [Fact]
    public async Task Handle_Refuses_An_Already_Cleared_Steps_Approver_Whose_Role_Is_No_Longer_Pending()
    {
        // Step 1 (QS) clears first, moving CurrentStepNo to 2 (PM). QS's role is no longer "pending"
        // for this revision - only steps >= CurrentStepNo count.
        var (fixture, certificate) = SeedSubmittedCertificate();
        var approveHandler = CreateApproveHandler(fixture, Guid.NewGuid(), UserRole.QS);
        await approveHandler.Handle(new CMPlus.Application.Features.Payment.Commands.Approve.ApprovePaymentCertificateCommand(certificate.Id, null), CancellationToken.None);
        Assert.Equal(2, certificate.CurrentStepNo);

        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.QS);
        var result = await handler.Handle(
            new ReturnPaymentCertificateForRevisionCommand(certificate.Id, "Too late, already cleared."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", result.Error);
    }

    [Fact]
    public async Task Handle_Rejects_A_Role_That_Holds_No_Step_In_This_Chain_At_All_No_Escape_Hatch()
    {
        var (fixture, certificate) = SeedSubmittedCertificate();
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.Site);

        var result = await handler.Handle(
            new ReturnPaymentCertificateForRevisionCommand(certificate.Id, "Not my role at all."), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", result.Error);
    }

    [Fact]
    public async Task Handle_Voids_The_Chain_Snapshot_And_Records_The_ApprovalAction_Against_The_Revision_Being_Closed()
    {
        var (fixture, certificate) = SeedSubmittedCertificate();
        var pinnedPolicyId = certificate.ApprovalPolicyId!.Value;
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.QS);

        await handler.Handle(new ReturnPaymentCertificateForRevisionCommand(certificate.Id, "Fix the quantities."), CancellationToken.None);

        Assert.Null(certificate.ApprovalPolicyId);
        Assert.Equal(0, certificate.CurrentStepNo);
        Assert.Equal(0, certificate.TotalSteps);
        Assert.Empty(certificate.ApprovalSteps); // the chain snapshot itself is voided too

        var action = Assert.Single(fixture.ActionRepository.Actions);
        Assert.Equal(ApprovalActionType.ReturnForRevision, action.Action);
        Assert.Equal(1, action.RevisionNo); // the revision that WAS pending, not the new one (2)
        Assert.Equal("Fix the quantities.", action.Comment);
        Assert.Equal(pinnedPolicyId, action.ApprovalPolicyId); // still records which version routed the closed revision
    }

    [Fact]
    public async Task Handle_Returns_InvalidStatusForTransition_When_Not_PendingApproval()
    {
        var tenantId = Guid.NewGuid();
        var fixture = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 1_000_000m, 0m, Guid.NewGuid());
        fixture.Repository.Seed(certificate); // Draft

        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.QS);
        var result = await handler.Handle(new ReturnPaymentCertificateForRevisionCommand(certificate.Id, "x"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateInvalidStatusForTransition", result.Error);
    }

    [Fact]
    public async Task Handle_Returns_ConcurrencyConflict_When_The_Repository_Save_Reports_A_Conflict()
    {
        var (fixture, certificate) = SeedSubmittedCertificate();
        fixture.Repository.SaveShouldSucceed = false;
        var handler = CreateHandler(fixture, Guid.NewGuid(), UserRole.QS);

        var result = await handler.Handle(new ReturnPaymentCertificateForRevisionCommand(certificate.Id, "x"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateConcurrencyConflict", result.Error);
    }
}
