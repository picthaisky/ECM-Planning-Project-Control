using CMPlus.Application.Features.Payment.Queries.GetPaymentCertificate;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Payment;

/// <summary>
/// S9 read-side gap closure (finding L-04): `GET /api/v1/payment-certificates/{id}`, including the
/// quorum-progress figure (security review sprint-09.md §9.5(ii)).
/// </summary>
public class GetPaymentCertificateQueryHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    private static PaymentCertificate SubmittedCertificate(
        Guid tenantId, Guid projectId, IReadOnlyList<PaymentCertificateApprovalStepInput> steps,
        Guid createdBy, Guid submittedBy, decimal amount = 5_000_000m)
    {
        var certificate = new PaymentCertificate(tenantId, projectId, 1, "IPC 1", amount, 0m, createdBy);
        certificate.SetPeriodClaim(100m, null, null, amount, 0m, 0m, amount);
        certificate.Submit(steps, Guid.NewGuid(), 1, allowSelfApproval: false, submittedBy, Now);
        return certificate;
    }

    [Fact]
    public async Task Returns_NotFound_When_The_Certificate_Does_Not_Exist()
    {
        var handler = new GetPaymentCertificateQueryHandler(new FakePaymentCertificateRepository(), new FakeApprovalActionRepository());

        var result = await handler.Handle(new GetPaymentCertificateQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentCertificateNotFound", result.Error);
    }

    [Fact]
    public async Task A_Draft_Certificate_With_No_Attached_Chain_Has_Null_Quorum_Progress_Not_Zero()
    {
        var repository = new FakePaymentCertificateRepository();
        var certificate = new PaymentCertificate(Guid.NewGuid(), Guid.NewGuid(), 1, "IPC 1", 1_000_000m, 0m, Guid.NewGuid());
        repository.Seed(certificate);

        var handler = new GetPaymentCertificateQueryHandler(repository, new FakeApprovalActionRepository());
        var result = await handler.Handle(new GetPaymentCertificateQuery(certificate.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.TotalSteps);
        Assert.Null(result.Value.CurrentStepApprovalsCollected); // "not applicable", never a fabricated 0
        Assert.False(result.Value.AllowSelfApproval);
        Assert.Empty(result.Value.ApprovalSteps);
    }

    [Fact]
    public async Task A_Freshly_Submitted_Certificate_Reports_Zero_Distinct_Real_Zero_Not_Null()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakePaymentCertificateRepository();
        IReadOnlyList<PaymentCertificateApprovalStepInput> steps = [new(1, UserRole.QS, QuorumCount: 2)];
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), steps, Guid.NewGuid(), Guid.NewGuid());
        repository.Seed(certificate);

        var handler = new GetPaymentCertificateQueryHandler(repository, new FakeApprovalActionRepository());
        var result = await handler.Handle(new GetPaymentCertificateQuery(certificate.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // A chain IS attached (TotalSteps > 0) and genuinely nobody has approved yet - a real 0, not
        // "not applicable" (which would be null). This is the exact distinction ADR-0013(f) draws for
        // ActualCostResult, reused here.
        Assert.Equal(0, result.Value.CurrentStepApprovalsCollected);
        var onlyStep = Assert.Single(result.Value.ApprovalSteps);
        Assert.Equal(2, onlyStep.QuorumCount);
    }

    [Fact]
    public async Task Reports_One_Of_Two_Signatures_Collected_On_A_Quorum_Two_Step()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakePaymentCertificateRepository();
        var actionRepository = new FakeApprovalActionRepository();
        IReadOnlyList<PaymentCertificateApprovalStepInput> steps = [new(1, UserRole.QS, QuorumCount: 2)];
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), steps, Guid.NewGuid(), Guid.NewGuid());
        repository.Seed(certificate);

        var firstApprover = Guid.NewGuid();
        actionRepository.Add(new ApprovalAction(
            tenantId, ApprovalDocumentType.PaymentCertificate, certificate.Id, certificate.RevisionNo, 1,
            firstApprover, UserRole.QS, ApprovalActionType.Approve, comment: null, Now, Guid.NewGuid(), 1));

        var handler = new GetPaymentCertificateQueryHandler(repository, actionRepository);
        var result = await handler.Handle(new GetPaymentCertificateQuery(certificate.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.CurrentStepApprovalsCollected); // "1 of 2" - the UI reads QuorumCount from ApprovalSteps[0]
    }

    [Fact]
    public async Task Quorum_Progress_Is_Scoped_To_The_Current_Step_Not_A_Running_Total_Across_The_Chain()
    {
        // Step 1 collected 1 approval and cleared (quorum 1); the certificate is now on step 2, which
        // has collected none yet. The count must reflect step 2 (0), never leak step 1's history.
        var tenantId = Guid.NewGuid();
        var repository = new FakePaymentCertificateRepository();
        var actionRepository = new FakeApprovalActionRepository();
        IReadOnlyList<PaymentCertificateApprovalStepInput> steps = [new(1, UserRole.QS, 1), new(2, UserRole.PM, 1)];
        var certificate = SubmittedCertificate(tenantId, Guid.NewGuid(), steps, Guid.NewGuid(), Guid.NewGuid());
        repository.Seed(certificate);

        actionRepository.Add(new ApprovalAction(
            tenantId, ApprovalDocumentType.PaymentCertificate, certificate.Id, certificate.RevisionNo, 1,
            Guid.NewGuid(), UserRole.QS, ApprovalActionType.Approve, comment: null, Now, Guid.NewGuid(), 1));
        certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, Now, quorumSatisfied: true);

        Assert.Equal(2, certificate.CurrentStepNo); // sanity: advanced past step 1

        var handler = new GetPaymentCertificateQueryHandler(repository, actionRepository);
        var result = await handler.Handle(new GetPaymentCertificateQuery(certificate.Id), CancellationToken.None);

        Assert.Equal(0, result.Value.CurrentStepApprovalsCollected);
    }
}
