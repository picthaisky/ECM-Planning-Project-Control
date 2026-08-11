using CMPlus.Application.Features.Payment.Commands.RecordPayment;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Payment;

public class RecordPaymentForPaymentCertificateCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    private sealed record Fixture(
        FakePaymentCertificateRepository Repository, FakeApprovalActionRepository ActionRepository, Guid TenantId);

    private static RecordPaymentForPaymentCertificateCommandHandler CreateHandler(Fixture fixture, Guid actorUserId) =>
        new(
            fixture.Repository,
            fixture.ActionRepository,
            new FakeTenantProviderForPayment(fixture.TenantId),
            new FakeCurrentUserContextForPayment(actorUserId, UserRole.QS),
            new FakeClockForPayment(Now));

    private static (Fixture Fixture, PaymentCertificate Certificate) SeedCertifiedCertificate()
    {
        var tenantId = Guid.NewGuid();
        var fixture = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);

        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 1_000_000.00m, 0m, Guid.NewGuid());
        certificate.SetPeriodClaim(100m, null, null, 1_000_000.00m, 0m, 0m, 1_000_000.00m);
        var policyId = Guid.NewGuid();
        certificate.Submit([new PaymentCertificateApprovalStepInput(1, UserRole.QS, 1)], policyId, 1, false, Guid.NewGuid(), Now);
        certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, Now);
        Assert.Equal(PaymentCertificateStatus.Certified, certificate.Status);

        fixture.Repository.Seed(certificate);
        return (fixture, certificate);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Certificate_Does_Not_Exist()
    {
        var (fixture, _) = SeedCertifiedCertificate();
        var handler = CreateHandler(fixture, Guid.NewGuid());

        var result = await handler.Handle(
            new RecordPaymentForPaymentCertificateCommand(Guid.NewGuid(), "CHQ-1", Now), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotFound", result.Error);
    }

    [Fact]
    public async Task Handle_Returns_InvalidStatusForTransition_When_Not_Certified()
    {
        var tenantId = Guid.NewGuid();
        var fixture = new Fixture(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository(), tenantId);
        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 1_000_000m, 0m, Guid.NewGuid());
        fixture.Repository.Seed(certificate); // Draft

        var handler = CreateHandler(fixture, Guid.NewGuid());
        var result = await handler.Handle(
            new RecordPaymentForPaymentCertificateCommand(certificate.Id, "CHQ-1", Now), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateInvalidStatusForTransition", result.Error);
    }

    [Fact]
    public async Task Handle_Moves_Certified_To_Paid_And_Records_An_ApprovalAction()
    {
        var (fixture, certificate) = SeedCertifiedCertificate();
        var paidAt = DateTimeOffset.Parse("2026-08-15T00:00:00+07:00");
        var actorId = Guid.NewGuid();

        var handler = CreateHandler(fixture, actorId);
        var result = await handler.Handle(
            new RecordPaymentForPaymentCertificateCommand(certificate.Id, "CHQ-000123", paidAt), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Paid, result.Value.Status);
        Assert.Equal("CHQ-000123", result.Value.PaymentReference);
        Assert.Equal(paidAt, result.Value.PaidAt);

        var action = Assert.Single(fixture.ActionRepository.Actions);
        Assert.Equal(ApprovalActionType.RecordPayment, action.Action);
        Assert.Equal(actorId, action.ActorUserId);
        Assert.Equal(UserRole.QS, action.ActorRoleAtTime);
        Assert.Equal(0, action.StepNo);
        Assert.Null(action.Comment);
    }

    [Fact]
    public async Task Handle_Returns_ConcurrencyConflict_When_The_Repository_Save_Reports_A_Conflict()
    {
        var (fixture, certificate) = SeedCertifiedCertificate();
        fixture.Repository.SaveShouldSucceed = false;
        var handler = CreateHandler(fixture, Guid.NewGuid());

        var result = await handler.Handle(
            new RecordPaymentForPaymentCertificateCommand(certificate.Id, "CHQ-1", Now), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateConcurrencyConflict", result.Error);
    }
}
