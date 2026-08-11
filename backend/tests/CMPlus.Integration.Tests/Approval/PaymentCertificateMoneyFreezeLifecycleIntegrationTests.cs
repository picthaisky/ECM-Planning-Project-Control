using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Approval;

/// <summary>
/// M-01 money-freeze half (security review sprint-09.md §9.4/§9.5): the risk with a blanket "money
/// fields are frozen once Status leaves Draft/NotDue" guard is that it also blocks a legitimate
/// status/step transition that happens to run on the same, already-non-Draft row - which would be
/// strictly worse than the gap it closes. This file proves the guard (wired into every
/// <see cref="ApprovalWorkflowHarness"/>-created context exactly like the production composition
/// root, per its own remarks) is transparent to every real lifecycle path: submit -&gt; approve -&gt;
/// certify -&gt; record payment, and the two loops back to <c>Draft</c> that legitimately re-touch
/// money fields (return-for-revision, and re-pricing while still <c>Draft</c>/before first submit).
/// <see cref="AppendOnlyGuardInterceptorTests"/> proves the narrower, interceptor-level half of this
/// (each money field individually, at the exact Modified/IsModified granularity); this file proves the
/// wider, handler-level "nothing broke" half.
/// </summary>
public class PaymentCertificateMoneyFreezeLifecycleIntegrationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    private static async Task RepriceWhileDraftAsync(
        ApprovalWorkflowHarness harness, Guid certificateId,
        decimal approvePct, decimal gross, decimal retention, decimal advanceRecovery, decimal netPayment)
    {
        // No production command handler calls SetPeriodClaim yet (see IPaymentCertificateRepository's
        // own remarks - "there is today no production path that creates a PaymentCertificate" extends
        // to re-pricing one), so this exercises the Domain method directly against a tracked,
        // guard-wired context - exactly what a future re-price command handler would do, and the same
        // pattern PaymentCertificateConcurrencyTests.RowVersion_Changes_On_Every_Persisted_Mutation
        // already establishes for this gap.
        using var context = harness.CreateContext();
        var certificate = await context.PaymentCertificates.SingleAsync(c => c.Id == certificateId);
        certificate.SetPeriodClaim(approvePct, null, null, gross, retention, advanceRecovery, netPayment);
        await context.SaveChangesAsync(); // must NOT throw - still Draft, money fields editable.
    }

    [Fact]
    public async Task Submit_Approve_Certify_RecordPayment_All_Still_Work_With_The_Money_Freeze_Guard_Active()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, allowSelfApproval: false, null, null,
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS)]);

        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 10_000_000.00m, Guid.NewGuid());

        // Re-pricing a Draft (the field the guard must never block) before first submission.
        await RepriceWhileDraftAsync(harness, certificateId, 50m, 5_000_000.00m, 250_000.00m, 500_000.00m, 4_250_000.00m);

        var submitResult = await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(submitResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, submitResult.Value.Status);

        var approveResult = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.QS, "Verified quantities.");
        Assert.True(approveResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Certified, approveResult.Value.Status);
        Assert.Equal(5_000_000.00m, approveResult.Value.GrossCertifiedAmount);
        Assert.Equal(4_250_000.00m, approveResult.Value.NetPayment);

        var recordPaymentResult = await harness.RecordPaymentAsync(certificateId, Guid.NewGuid(), "TT-2026-001", Now);
        Assert.True(recordPaymentResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Paid, recordPaymentResult.Value.Status);

        var final = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(PaymentCertificateStatus.Paid, final.Status);
        // Money fields survived the entire post-Draft lifecycle unchanged, as EnsureMoneyFieldsEditable
        // already guarantees at the Domain layer - now also structurally guaranteed at persistence.
        Assert.Equal(5_000_000.00m, final.GrossCertifiedAmount);
        Assert.Equal(250_000.00m, final.RetentionAmount);
        Assert.Equal(500_000.00m, final.AdvanceRecoveryAmount);
        Assert.Equal(4_250_000.00m, final.NetPayment);
    }

    [Fact]
    public async Task ReturnForRevision_Then_Repricing_The_Draft_Again_Then_Resubmitting_And_Approving_All_Still_Work_With_The_Guard_Active()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, allowSelfApproval: false, null, null,
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS)]);

        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 10_000_000.00m, Guid.NewGuid());
        await RepriceWhileDraftAsync(harness, certificateId, 50m, 5_000_000.00m, 250_000.00m, 500_000.00m, 4_250_000.00m);

        var firstSubmit = await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(firstSubmit.IsSuccess);

        var returnResult = await harness.ReturnForRevisionAsync(certificateId, Guid.NewGuid(), UserRole.QS, "Quantities need rechecking.");
        Assert.True(returnResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Draft, returnResult.Value.Status);
        Assert.Equal(2, returnResult.Value.RevisionNo);

        // Money fields must be editable again now that the certificate is back to Draft - re-pricing
        // to a DIFFERENT amount proves the guard's freeze boundary tracks Status both ways, not just
        // "once frozen, always frozen".
        await RepriceWhileDraftAsync(harness, certificateId, 80m, 8_000_000.00m, 400_000.00m, 800_000.00m, 6_800_000.00m);

        var resubmitResult = await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(resubmitResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, resubmitResult.Value.Status);

        var approveResult = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(approveResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Certified, approveResult.Value.Status);
        Assert.Equal(8_000_000.00m, approveResult.Value.GrossCertifiedAmount);
        Assert.Equal(6_800_000.00m, approveResult.Value.NetPayment);
    }

    [Fact]
    public async Task Reject_At_The_Final_Step_Still_Works_With_The_Guard_Active_Status_Changes_Money_Fields_Do_Not()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, allowSelfApproval: false, null, null,
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS)]);

        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 10_000_000.00m, Guid.NewGuid());
        await RepriceWhileDraftAsync(harness, certificateId, 50m, 5_000_000.00m, 250_000.00m, 500_000.00m, 4_250_000.00m);
        await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);

        var rejectResult = await harness.RejectAsync(certificateId, Guid.NewGuid(), UserRole.QS, "Work not actually complete on site.");
        Assert.True(rejectResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Rejected, rejectResult.Value.Status);

        var final = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(5_000_000.00m, final.GrossCertifiedAmount); // frozen, but the Reject transition itself was never blocked
    }
}
