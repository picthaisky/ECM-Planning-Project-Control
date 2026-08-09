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
}
