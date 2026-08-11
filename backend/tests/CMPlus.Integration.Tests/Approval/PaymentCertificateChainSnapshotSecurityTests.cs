using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Integration.Tests.Approval;

/// <summary>
/// Regression coverage for security review sprint-09.md H-01/H-02, both execution-verified there
/// against the real production handlers. The pre-fix 822-test suite passed *over* both defects
/// because every existing fixture either (a) gives every rule sharing a StepNo the same RequiredRole
/// across bands (H-01), or (b) never configures QuorumCount &gt; 1 (H-02). Each fixture below is the
/// one property that varies from every pre-existing fixture, and is the exact shape the review asked
/// for.
/// </summary>
public class PaymentCertificateChainSnapshotSecurityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    /// <summary>
    /// H-01's own reproduction table (sprint-09.md §2 H-01): two rules share StepNo 1 across
    /// different, non-overlapping bands with DIFFERENT roles - an entirely ordinary tiered DoA. Only
    /// the [1,000,000, null) band's role (PM) may act at step 1 for a ฿5,000,000 certificate; the
    /// [0, 1,000,000) band's role (QS) is not part of THIS document's chain at all, even though a
    /// rule bearing that role shares StepNo 1 in the same policy.
    /// </summary>
    [Fact]
    public async Task H01_A_High_Value_Certificate_Requires_PM_At_Step_1_Not_The_Low_Band_QS_Even_Though_Both_Rules_Share_StepNo_1()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, allowSelfApproval: false, null, null,
            [
                new ApprovalPolicyRuleInput(1, 0.00m, 1_000_000.00m, UserRole.QS),
                new ApprovalPolicyRuleInput(1, 1_000_000.00m, null, UserRole.PM),
                new ApprovalPolicyRuleInput(2, 1_000_000.00m, null, UserRole.ProjectDirector),
            ]);

        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 5_000_000.00m, Guid.NewGuid());
        var submitResult = await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(submitResult.IsSuccess);
        Assert.Equal(2, submitResult.Value.TotalSteps); // routes correctly: [PM, ProjectDirector]

        // The out-of-band QS (the low-band [0,1M) rule's role) must be refused at step 1 - they are
        // NOT part of this ฿5,000,000 certificate's actual chain, even though a rule with their role
        // shares StepNo 1 in the same policy.
        var qsAttempt = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(qsAttempt.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", qsAttempt.Error);

        var certificateAfterQsAttempt = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(1, certificateAfterQsAttempt.CurrentStepNo); // never advanced

        // The real step-1 role for this amount (PM) succeeds.
        var pmApproval = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.PM);
        Assert.True(pmApproval.IsSuccess);
        Assert.Equal(2, pmApproval.Value.CurrentStepNo);

        var directorApproval = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.ProjectDirector);
        Assert.True(directorApproval.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Certified, directorApproval.Value.Status);
    }

    /// <summary>H-01's second manifestation (sprint-09.md §2 H-01 item 1): ReturnForRevision must be
    /// equally refused to the out-of-band role, not just Approve - an out-of-band actor must not be
    /// able to bounce a document they hold no real authority over back to Draft either.</summary>
    [Fact]
    public async Task H01_ReturnForRevision_Also_Refuses_The_Out_Of_Band_Role()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, allowSelfApproval: false, null, null,
            [
                new ApprovalPolicyRuleInput(1, 0.00m, 1_000_000.00m, UserRole.QS),
                new ApprovalPolicyRuleInput(1, 1_000_000.00m, null, UserRole.PM),
                new ApprovalPolicyRuleInput(2, 1_000_000.00m, null, UserRole.ProjectDirector),
            ]);

        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 5_000_000.00m, Guid.NewGuid());
        await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);

        var qsAttempt = await harness.ReturnForRevisionAsync(certificateId, Guid.NewGuid(), UserRole.QS, "Not actually my document.");

        Assert.True(qsAttempt.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", qsAttempt.Error);
        var certificate = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status); // never returned to Draft
    }

    /// <summary>
    /// H-02's own reproduction (sprint-09.md §2 H-02): QuorumCount = 2 must require a SECOND distinct
    /// approver before the step clears - one signature must not certify it.
    /// </summary>
    [Fact]
    public async Task H02_A_QuorumCount_Two_Final_Step_Does_Not_Certify_On_A_Single_Signature()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, allowSelfApproval: false, null, null,
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS, RequiredUserId: null, QuorumCount: 2)]);

        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 5_000_000.00m, Guid.NewGuid());
        var submitResult = await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(submitResult.IsSuccess);

        var firstQs = Guid.NewGuid();
        var afterFirst = await harness.ApproveAsync(certificateId, firstQs, UserRole.QS);
        Assert.True(afterFirst.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, afterFirst.Value.Status); // NOT certified yet
        Assert.Equal(1, afterFirst.Value.CurrentStepNo); // still step 1, waiting on the second signature

        var secondQs = Guid.NewGuid();
        var afterSecond = await harness.ApproveAsync(certificateId, secondQs, UserRole.QS);
        Assert.True(afterSecond.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Certified, afterSecond.Value.Status); // now cleared

        var actions = await harness.LoadActionsAsync(certificateId);
        Assert.Equal(2, actions.Count(a => a.Action == ApprovalActionType.Approve));
    }

    /// <summary>
    /// Persistence round-trip proof for the H-01 chain snapshot, across genuinely separate
    /// <see cref="CmPlusDbContext"/> instances (one per harness call, exactly like one scoped
    /// DbContext per HTTP request in production): <c>ReturnForRevision</c> voiding the snapshot must
    /// actually delete the old <c>PaymentCertificateApprovalStep</c> rows in the database, not merely
    /// clear the in-memory collection of the instance that happened to make the change - and a
    /// resubmission must persist a fresh, correctly-shaped set of rows for the new revision.
    /// </summary>
    [Fact]
    public async Task The_Chain_Snapshot_Is_Actually_Deleted_From_The_Database_On_ReturnForRevision_And_Rebuilt_Fresh_On_Resubmit()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, allowSelfApproval: false, null, null,
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS), new ApprovalPolicyRuleInput(2, 0.00m, null, UserRole.PM)]);

        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 5_000_000.00m, Guid.NewGuid());
        await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);

        var afterSubmit = await harness.LoadCertificateWithStepsAsync(certificateId);
        Assert.Equal(2, afterSubmit.ApprovalSteps.Count); // persisted for real, reloaded from a fresh context
        Assert.All(afterSubmit.ApprovalSteps, s => Assert.Equal(1, s.RevisionNo));

        await harness.ReturnForRevisionAsync(certificateId, Guid.NewGuid(), UserRole.QS, "Needs rework.");

        var afterReturn = await harness.LoadCertificateWithStepsAsync(certificateId);
        Assert.Empty(afterReturn.ApprovalSteps); // genuinely deleted from the database, not merely cleared in memory

        await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);

        var afterResubmit = await harness.LoadCertificateWithStepsAsync(certificateId);
        Assert.Equal(2, afterResubmit.ApprovalSteps.Count);
        Assert.All(afterResubmit.ApprovalSteps, s => Assert.Equal(2, s.RevisionNo)); // the NEW revision only
        Assert.Contains(afterResubmit.ApprovalSteps, s => s.StepNo == 1 && s.RequiredRole == UserRole.QS);
        Assert.Contains(afterResubmit.ApprovalSteps, s => s.StepNo == 2 && s.RequiredRole == UserRole.PM);
    }

    /// <summary>Same user cannot cast a second vote toward their own step's quorum - reconciles
    /// quorum enforcement with the pre-existing DuplicateChainVoter rule (ADR-0016; was
    /// DuplicateChainApprover - sprint-09.md H-02 fix guidance: "this must be reconciled with the
    /// existing DuplicateChainApprover check").</summary>
    [Fact]
    public async Task H02_The_Same_User_Cannot_Cast_A_Second_Vote_Toward_Their_Own_Steps_Quorum()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, allowSelfApproval: false, null, null,
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS, RequiredUserId: null, QuorumCount: 2)]);

        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 5_000_000.00m, Guid.NewGuid());
        await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);

        var qsUserId = Guid.NewGuid();
        var first = await harness.ApproveAsync(certificateId, qsUserId, UserRole.QS);
        Assert.True(first.IsSuccess);

        var secondAttemptSameUser = await harness.ApproveAsync(certificateId, qsUserId, UserRole.QS);

        Assert.True(secondAttemptSameUser.IsFailure);
        Assert.Equal("PaymentCertificateDuplicateChainVoter", secondAttemptSameUser.Error);
        var certificate = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status); // still waiting
    }
}
