using CMPlus.Application.Approval;
using CMPlus.Application.Features.Payment.Commands.Submit;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Payment;

/// <summary>S9-BE-05 DoD: submit resolves the chain via the real (pure) <see cref="ApprovalRoutingService"/>
/// and snapshots the resolved policy version onto the document; an empty chain fails closed with
/// <c>ApprovalPolicyGap</c> (422), never auto-approving.</summary>
public class SubmitPaymentCertificateCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    private static ApprovalPolicy ThDefaultIpc(Guid tenantId) => ApprovalPolicy.CreateInitialVersion(
        tenantId, projectId: null, ApprovalDocumentType.PaymentCertificate, Now.AddYears(-1),
        [
            new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS),
            new ApprovalPolicyRuleInput(2, 0.00m, null, UserRole.PM),
            new ApprovalPolicyRuleInput(3, 10_000_000.00m, null, UserRole.ProjectDirector),
        ]);

    private static PaymentCertificate DraftCertificate(Guid tenantId, Guid projectId, decimal grossCertifiedAmount)
    {
        var certificate = new PaymentCertificate(tenantId, projectId, 1, "IPC 1", grossCertifiedAmount, 0m, Guid.NewGuid());
        certificate.SetPeriodClaim(
            100m, null, null, grossCertifiedAmount,
            retentionAmount: 0m, advanceRecoveryAmount: 0m, netPayment: grossCertifiedAmount);
        return certificate;
    }

    private sealed record Fixture(
        SubmitPaymentCertificateCommandHandler Handler,
        FakePaymentCertificateRepository Repository,
        FakeApprovalActionRepository ActionRepository,
        FakeApprovalPolicyReaderForPayment PolicyReader,
        Guid TenantId,
        Guid ActorUserId);

    private static Fixture CreateFixture(UserRole actorRole = UserRole.QS)
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var repository = new FakePaymentCertificateRepository();
        var actionRepository = new FakeApprovalActionRepository();
        var policyReader = new FakeApprovalPolicyReaderForPayment();

        var handler = new SubmitPaymentCertificateCommandHandler(
            repository,
            policyReader,
            new ApprovalRoutingService(),
            actionRepository,
            new FakeTenantProviderForPayment(tenantId),
            new FakeCurrentUserContextForPayment(actorUserId, actorRole),
            new FakeClockForPayment(Now));

        return new Fixture(handler, repository, actionRepository, policyReader, tenantId, actorUserId);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Certificate_Does_Not_Exist()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.Handle(new SubmitPaymentCertificateCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotFound", result.Error);
    }

    [Fact]
    public async Task Handle_Returns_InvalidStatusForTransition_When_The_Certificate_Is_Not_Draft()
    {
        var fixture = CreateFixture();
        var projectId = Guid.NewGuid();
        var certificate = DraftCertificate(fixture.TenantId, projectId, 1_000_000m);
        certificate.Submit([new PaymentCertificateApprovalStepInput(1, UserRole.QS, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now); // now PendingApproval
        fixture.Repository.Seed(certificate);

        var result = await fixture.Handler.Handle(new SubmitPaymentCertificateCommand(certificate.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateInvalidStatusForTransition", result.Error);
    }

    [Fact]
    public async Task Handle_Fails_Closed_With_ApprovalPolicyGap_When_No_Policy_Rule_Covers_The_Amount_And_Never_Saves()
    {
        var fixture = CreateFixture();
        var projectId = Guid.NewGuid();
        // A policy whose lowest MinAmount is 100,000.00 - mirrors fixture R5.
        fixture.PolicyReader.Policies.Add(ApprovalPolicy.CreateInitialVersion(
            fixture.TenantId, null, ApprovalDocumentType.PaymentCertificate, Now.AddYears(-1),
            [new ApprovalPolicyRuleInput(1, 100_000.00m, null, UserRole.QS)]));
        var certificate = DraftCertificate(fixture.TenantId, projectId, 50_000.00m);
        fixture.Repository.Seed(certificate);

        var result = await fixture.Handler.Handle(new SubmitPaymentCertificateCommand(certificate.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalErrorCodes.PolicyGap, result.Error);
        Assert.Equal(PaymentCertificateStatus.Draft, certificate.Status); // never auto-approved
        Assert.Equal(0, fixture.Repository.SaveCallCount);
    }

    [Fact]
    public async Task Handle_R7_Resolves_QS_PM_ProjectDirector_And_Pins_The_Policy_Version()
    {
        var fixture = CreateFixture();
        var projectId = Guid.NewGuid();
        var policy = ThDefaultIpc(fixture.TenantId);
        fixture.PolicyReader.Policies.Add(policy);
        var certificate = DraftCertificate(fixture.TenantId, projectId, 21_600_000.00m);
        fixture.Repository.Seed(certificate);

        var result = await fixture.Handler.Handle(new SubmitPaymentCertificateCommand(certificate.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, result.Value.Status);
        Assert.Equal(1, result.Value.CurrentStepNo);
        Assert.Equal(3, result.Value.TotalSteps);
        Assert.Equal(policy.Id, result.Value.ApprovalPolicyId);
        Assert.Equal(policy.Version, result.Value.ApprovalPolicyVersion);
        Assert.Equal(1, fixture.Repository.SaveCallCount);
    }

    [Fact]
    public async Task Handle_R8_Resolves_QS_PM_Only_Below_The_ProjectDirector_Threshold()
    {
        var fixture = CreateFixture();
        var projectId = Guid.NewGuid();
        fixture.PolicyReader.Policies.Add(ThDefaultIpc(fixture.TenantId));
        var certificate = DraftCertificate(fixture.TenantId, projectId, 5_000_000.00m);
        fixture.Repository.Seed(certificate);

        var result = await fixture.Handler.Handle(new SubmitPaymentCertificateCommand(certificate.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalSteps);
    }

    [Fact]
    public async Task Handle_Writes_A_Submit_ApprovalAction_With_ActorRoleAtTime_And_The_Same_Policy_Pin()
    {
        var fixture = CreateFixture(actorRole: UserRole.PM);
        var projectId = Guid.NewGuid();
        var policy = ThDefaultIpc(fixture.TenantId);
        fixture.PolicyReader.Policies.Add(policy);
        var certificate = DraftCertificate(fixture.TenantId, projectId, 5_000_000.00m);
        fixture.Repository.Seed(certificate);

        await fixture.Handler.Handle(new SubmitPaymentCertificateCommand(certificate.Id), CancellationToken.None);

        var action = Assert.Single(fixture.ActionRepository.Actions);
        Assert.Equal(fixture.TenantId, action.TenantId);
        Assert.Equal(ApprovalDocumentType.PaymentCertificate, action.DocumentType);
        Assert.Equal(certificate.Id, action.DocumentId);
        Assert.Equal(1, action.RevisionNo);
        Assert.Equal(ApprovalActionType.Submit, action.Action);
        Assert.Equal(fixture.ActorUserId, action.ActorUserId);
        Assert.Equal(UserRole.PM, action.ActorRoleAtTime);
        Assert.Equal(policy.Id, action.ApprovalPolicyId);
        Assert.Equal(policy.Version, action.ApprovalPolicyVersion);
    }

    [Fact]
    public async Task Handle_Uses_The_Single_Step_ProjectDirector_Fallback_When_No_Policy_Is_Configured_At_All()
    {
        var fixture = CreateFixture();
        var projectId = Guid.NewGuid();
        var certificate = DraftCertificate(fixture.TenantId, projectId, 250_000.00m);
        fixture.Repository.Seed(certificate); // no policies seeded into fixture.PolicyReader at all

        var result = await fixture.Handler.Handle(new SubmitPaymentCertificateCommand(certificate.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalSteps);
        Assert.Equal(Guid.Empty, result.Value.ApprovalPolicyId);
        Assert.Equal(0, result.Value.ApprovalPolicyVersion);
    }

    [Fact]
    public async Task Handle_Returns_ConcurrencyConflict_When_The_Repository_Save_Reports_A_Conflict()
    {
        var fixture = CreateFixture();
        var projectId = Guid.NewGuid();
        fixture.PolicyReader.Policies.Add(ThDefaultIpc(fixture.TenantId));
        var certificate = DraftCertificate(fixture.TenantId, projectId, 5_000_000.00m);
        fixture.Repository.Seed(certificate);
        fixture.Repository.SaveShouldSucceed = false;

        var result = await fixture.Handler.Handle(new SubmitPaymentCertificateCommand(certificate.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateConcurrencyConflict", result.Error);
    }
}
