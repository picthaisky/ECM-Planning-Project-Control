using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Approval;

/// <summary>
/// S9-BE-05: approval-workflow.md §8 routing/state fixtures R7-R10, exercised end to end through
/// the real <c>SubmitPaymentCertificateCommandHandler</c>/<c>ApprovePaymentCertificateCommandHandler</c>/
/// <c>ReturnPaymentCertificateForRevisionCommandHandler</c> against a real <c>CmPlusDbContext</c>
/// (EF Core InMemory, per the Docker outage) seeded with the real <c>TH-Default-IPC</c> policy - not
/// the pure routing-service-only slice <c>ApprovalRoutingServiceTests</c> already covers (Sprint 2),
/// which explicitly deferred the state-machine/actor half of R9/R10 to this sprint (see that class's
/// remarks).
/// </summary>
public class PaymentCertificateApprovalRoutingFixtureTests
{
    private static readonly DateTimeOffset EffectiveFrom = DateTimeOffset.Parse("2025-01-01T00:00:00+07:00");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    [Fact]
    public async Task R7_Gross_21_6M_Resolves_QS_PM_ProjectDirector_And_All_Three_Clear_The_Chain_To_Certified()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = Guid.NewGuid();
        var qsUserId = Guid.NewGuid();
        var certificateId = await harness.SeedDraftCertificateAsync(projectId, 21_600_000.00m, qsUserId);

        var submitResult = await harness.SubmitAsync(certificateId, qsUserId, UserRole.QS);
        Assert.True(submitResult.IsSuccess);
        Assert.Equal(3, submitResult.Value.TotalSteps);
        Assert.Equal(1, submitResult.Value.CurrentStepNo);

        var afterQs = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(afterQs.IsSuccess);
        Assert.Equal(2, afterQs.Value.CurrentStepNo);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, afterQs.Value.Status);

        var afterPm = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.PM);
        Assert.True(afterPm.IsSuccess);
        Assert.Equal(3, afterPm.Value.CurrentStepNo);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, afterPm.Value.Status);

        var afterDirector = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.ProjectDirector);
        Assert.True(afterDirector.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Certified, afterDirector.Value.Status);

        var actions = await harness.LoadActionsAsync(certificateId);
        Assert.Equal(4, actions.Count); // Submit + 3 Approve
        Assert.Equal([ApprovalActionType.Submit, ApprovalActionType.Approve, ApprovalActionType.Approve, ApprovalActionType.Approve],
            actions.Select(a => a.Action).ToArray());
        Assert.Equal([UserRole.QS, UserRole.QS, UserRole.PM, UserRole.ProjectDirector],
            actions.Select(a => a.ActorRoleAtTime).ToArray());
    }

    [Fact]
    public async Task R8_Gross_5M_Resolves_QS_PM_Only_No_ProjectDirector_Step()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = Guid.NewGuid();
        var qsUserId = Guid.NewGuid();
        var certificateId = await harness.SeedDraftCertificateAsync(projectId, 5_000_000.00m, qsUserId);

        var submitResult = await harness.SubmitAsync(certificateId, qsUserId, UserRole.QS);

        Assert.True(submitResult.IsSuccess);
        Assert.Equal(2, submitResult.Value.TotalSteps);
    }

    [Fact]
    public async Task R9_Return_For_Revision_Then_Resubmit_Produces_RevisionNo_2_Voids_The_Prior_Approval_And_Re_Resolves_From_Step_1()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var certificateId = await harness.SeedDraftCertificateAsync(projectId, 21_600_000.00m, creatorId);

        // R7's own submission + QS approval (step 1 -> 2, PM's turn).
        var submitterId = Guid.NewGuid();
        var submitResult = await harness.SubmitAsync(certificateId, submitterId, UserRole.QS);
        Assert.True(submitResult.IsSuccess);
        Assert.Equal(3, submitResult.Value.TotalSteps);

        var qsApproverId = Guid.NewGuid();
        var afterQs = await harness.ApproveAsync(certificateId, qsApproverId, UserRole.QS);
        Assert.True(afterQs.IsSuccess);
        Assert.Equal(2, afterQs.Value.CurrentStepNo);

        // PM (the current step's own approver) returns it for revision instead of approving.
        var pmUserId = Guid.NewGuid();
        var returnResult = await harness.ReturnForRevisionAsync(certificateId, pmUserId, UserRole.PM, "Quantities need re-measurement.");
        Assert.True(returnResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Draft, returnResult.Value.Status);
        Assert.Equal(2, returnResult.Value.RevisionNo);
        Assert.Null(returnResult.Value.ApprovalPolicyId); // chain snapshot voided

        // The QS approval from revision 1 is still on the append-only ledger (never deleted) but no
        // longer counts operationally - proven below by re-approval starting fresh at step 1.
        var actionsAfterReturn = await harness.LoadActionsAsync(certificateId);
        Assert.Contains(actionsAfterReturn, a => a.Action == ApprovalActionType.Approve && a.RevisionNo == 1 && a.ActorUserId == qsApproverId);
        Assert.Contains(actionsAfterReturn, a => a.Action == ApprovalActionType.ReturnForRevision && a.RevisionNo == 1);

        // Revise the claim down to the fixture's resubmitted gross (9,000,000.00 < the 10,000,000.00
        // ProjectDirector threshold) - money fields are unfrozen again now that the certificate is
        // back in Draft (approval-workflow.md §4 rule 4 / §6.3 "no partial carry-over").
        var certificateBeforeResubmit = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(PaymentCertificateStatus.Draft, certificateBeforeResubmit.Status);

        await ReviseClaimAsync(harness, certificateId, 9_000_000.00m);

        var resubmitResult = await harness.SubmitAsync(certificateId, submitterId, UserRole.QS);

        Assert.True(resubmitResult.IsSuccess);
        Assert.Equal(2, resubmitResult.Value.RevisionNo);
        Assert.Equal(1, resubmitResult.Value.CurrentStepNo); // re-approval starts at step 1
        Assert.Equal(2, resubmitResult.Value.TotalSteps); // ProjectDirector dropped: 9M < 10M threshold

        // A fresh QS approval is required - the revision-1 approval above does not carry over.
        var freshQsApproval = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(freshQsApproval.IsSuccess);
        Assert.Equal(2, freshQsApproval.Value.CurrentStepNo);
        Assert.Equal(2, freshQsApproval.Value.RevisionNo);
    }

    [Fact]
    public async Task R10_The_QS_Who_Is_Also_The_Submitter_Cannot_Approve_Their_Own_Certificate()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var certificateId = await harness.SeedDraftCertificateAsync(projectId, 5_000_000.00m, creatorId);

        var qsUserId = Guid.NewGuid();
        var submitResult = await harness.SubmitAsync(certificateId, qsUserId, UserRole.QS); // QS is also the submitter
        Assert.True(submitResult.IsSuccess);

        var approveResult = await harness.ApproveAsync(certificateId, qsUserId, UserRole.QS);

        Assert.True(approveResult.IsFailure);
        Assert.Equal("PaymentCertificateSelfApprovalNotPermitted", approveResult.Error);

        var certificate = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(1, certificate.CurrentStepNo); // never advanced
    }

    [Fact]
    public async Task No_PM_Escape_Hatch_A_PM_Cannot_Approve_A_Step_That_Requires_QS_Even_Though_PM_Is_A_Legitimate_Role()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var certificateId = await harness.SeedDraftCertificateAsync(projectId, 5_000_000.00m, creatorId);
        var submitResult = await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(submitResult.IsSuccess);
        Assert.Equal(1, submitResult.Value.CurrentStepNo); // step 1 requires QS

        var pmAttempt = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.PM);

        Assert.True(pmAttempt.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", pmAttempt.Error);

        // Same proof for Admin and Executive - no role name anywhere grants a shortcut.
        var adminAttempt = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.Admin);
        Assert.True(adminAttempt.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", adminAttempt.Error);

        var executiveAttempt = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.Executive);
        Assert.True(executiveAttempt.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", executiveAttempt.Error);

        var certificate = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status);
        Assert.Equal(1, certificate.CurrentStepNo); // still untouched, waiting on the real QS
    }

    /// <summary>Re-derives the corrected period claim the way <c>CertificateCalculator</c> (S9-BE-02)
    /// would, simplified to a flat 0% retention/advance so the test's only variable is the gross
    /// amount that actually drives R9's routing re-resolution.</summary>
    private static async Task ReviseClaimAsync(ApprovalWorkflowHarness harness, Guid certificateId, decimal revisedGross)
    {
        using var context = harness.CreateContext();
        var certificate = await context.PaymentCertificates.SingleAsync(c => c.Id == certificateId);
        certificate.SetPeriodClaim(100m, null, null, revisedGross, 0m, 0m, revisedGross);
        await context.SaveChangesAsync();
    }
}
