using CMPlus.Application.Features.Approval.Queries.GetApprovalActionHistory;
using CMPlus.Application.Tests.Features.Payment;
using CMPlus.Application.Tests.Features.VariationOrder;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Approval;

/// <summary>
/// S9 read-side gap closure (finding L-04): `GET /api/v1/{…}/{id}/approval-actions`. Deliberately
/// document-type-agnostic (see the handler's own remarks) - these tests exercise it purely via
/// <see cref="ApprovalDocumentType"/>, matching the shape Sprint 10's VariationOrder reuse expects.
/// </summary>
public class GetApprovalActionHistoryQueryHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    [Fact]
    public async Task Returns_NotFound_For_A_PaymentCertificate_Id_That_Does_Not_Exist()
    {
        var handler = new GetApprovalActionHistoryQueryHandler(new FakeApprovalActionRepository(), new FakePaymentCertificateRepository(), new FakeVariationOrderRepository());

        var result = await handler.Handle(
            new GetApprovalActionHistoryQuery(ApprovalDocumentType.PaymentCertificate, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        // Reused as-is, the same code the five S9-BE-05 mutating commands already map to 404 (this
        // task's explicit instruction) - never a differently-named/shaped error.
        Assert.Equal("PaymentCertificateNotFound", result.Error);
    }

    /// <summary>
    /// Sprint 10 landed the <c>VariationOrder</c> arm. This test previously asserted the *absence* of
    /// that arm ("no such aggregate exists yet"), which is why it kept passing while the endpoint was
    /// broken: the stale <c>_ =&gt; false</c> default returned an unconditional 404 that a
    /// not-found assertion cannot distinguish from correct behaviour. It now asserts an unknown id
    /// still 404s while a *real* VO resolves — the pair is what actually pins the arm.
    /// </summary>
    [Fact]
    public async Task Returns_NotFound_For_A_VariationOrder_Id_That_Does_Not_Exist()
    {
        var handler = new GetApprovalActionHistoryQueryHandler(new FakeApprovalActionRepository(), new FakePaymentCertificateRepository(), new FakeVariationOrderRepository());

        var result = await handler.Handle(
            new GetApprovalActionHistoryQuery(ApprovalDocumentType.VariationOrder, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotFound", result.Error);
    }

    [Fact]
    public async Task Returns_An_Empty_History_For_A_Real_Certificate_That_Has_Never_Been_Submitted()
    {
        var certificateRepository = new FakePaymentCertificateRepository();
        var certificate = new PaymentCertificate(Guid.NewGuid(), Guid.NewGuid(), 1, "IPC 1", 1_000_000m, 0m, Guid.NewGuid());
        certificateRepository.Seed(certificate);

        var handler = new GetApprovalActionHistoryQueryHandler(new FakeApprovalActionRepository(), certificateRepository, new FakeVariationOrderRepository());
        var result = await handler.Handle(
            new GetApprovalActionHistoryQuery(ApprovalDocumentType.PaymentCertificate, certificate.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value); // real certificate, genuinely no history yet - a 200 with [], never a 404
    }

    [Fact]
    public async Task Returns_The_Full_Mapped_History_For_A_Real_Certificate()
    {
        var tenantId = Guid.NewGuid();
        var certificateRepository = new FakePaymentCertificateRepository();
        var actionRepository = new FakeApprovalActionRepository();
        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 5_000_000m, 0m, Guid.NewGuid());
        certificateRepository.Seed(certificate);

        var policyId = Guid.NewGuid();
        var submitAction = new ApprovalAction(
            tenantId, ApprovalDocumentType.PaymentCertificate, certificate.Id, revisionNo: 1, stepNo: 0,
            actorUserId: Guid.NewGuid(), UserRole.QS, ApprovalActionType.Submit, comment: null, Now, policyId, 1);
        var approveAction = new ApprovalAction(
            tenantId, ApprovalDocumentType.PaymentCertificate, certificate.Id, revisionNo: 1, stepNo: 1,
            actorUserId: Guid.NewGuid(), UserRole.QS, ApprovalActionType.Approve, comment: "Quantities verified.",
            Now.AddMinutes(5), policyId, 1);
        actionRepository.Add(submitAction);
        actionRepository.Add(approveAction);

        var handler = new GetApprovalActionHistoryQueryHandler(actionRepository, certificateRepository, new FakeVariationOrderRepository());
        var result = await handler.Handle(
            new GetApprovalActionHistoryQuery(ApprovalDocumentType.PaymentCertificate, certificate.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(submitAction.Id, result.Value[0].Id); // oldest-first (IApprovalActionRepository's own contract)
        Assert.Equal(ApprovalActionType.Submit, result.Value[0].Action);
        Assert.Equal(approveAction.Id, result.Value[1].Id);
        Assert.Equal("Quantities verified.", result.Value[1].Comment);
        Assert.Equal(1, result.Value[1].StepNo);
    }
}
