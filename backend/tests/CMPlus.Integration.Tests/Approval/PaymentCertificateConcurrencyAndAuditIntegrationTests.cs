using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;

namespace CMPlus.Integration.Tests.Approval;

/// <summary>
/// S9-BE-05 DoD: "ใช้ RowVersion → ผู้อนุมัติพร้อมกัน 2 คน คนที่สองได้ 409" wired up for real (the
/// domain-level proof already exists in <c>PaymentCertificateConcurrencyTests</c>; this exercises
/// the new <c>IPaymentCertificateRepository.TrySaveChangesAsync</c> S9-BE-05 added on top of it) -
/// plus "every act writes both an ApprovalAction and an AuditLog".
/// </summary>
public class PaymentCertificateConcurrencyAndAuditIntegrationTests
{
    private static readonly DateTimeOffset EffectiveFrom = DateTimeOffset.Parse("2025-01-01T00:00:00+07:00");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    [Fact]
    public async Task Two_Concurrent_Approvers_Loaded_Before_Either_Saves_The_Second_Repository_Save_Reports_A_Conflict_Not_A_Double_Advance()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        // A single-step QS-only policy keeps the assertion simple: one clean Approve certifies.
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, false, null, null,
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS)]);

        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 5_000_000.00m, Guid.NewGuid());
        var submitResult = await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(submitResult.IsSuccess);
        Assert.Equal(1, submitResult.Value.TotalSteps);

        // Two independent contexts both load the SAME row (same RowVersion) before either writes -
        // exactly what "two simultaneous approvers" means in practice.
        using var contextForApproverOne = harness.CreateContext();
        using var contextForApproverTwo = harness.CreateContext();

        var repositoryOne = new PaymentCertificateRepository(contextForApproverOne);
        var repositoryTwo = new PaymentCertificateRepository(contextForApproverTwo);

        var certificateSeenByOne = await repositoryOne.FindAsync(certificateId);
        var certificateSeenByTwo = await repositoryTwo.FindAsync(certificateId);

        certificateSeenByOne!.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, Now);
        certificateSeenByTwo!.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, Now);

        var savedByOne = await repositoryOne.TrySaveChangesAsync();
        var savedByTwo = await repositoryTwo.TrySaveChangesAsync();

        Assert.True(savedByOne);
        Assert.False(savedByTwo); // reports a conflict rather than throwing past the repository boundary

        var persisted = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(PaymentCertificateStatus.Certified, persisted.Status); // advanced exactly once, not twice
    }

    [Fact]
    public async Task Every_Approval_Act_Writes_Both_An_ApprovalAction_And_An_AuditLog_Row()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 5_000_000.00m, Guid.NewGuid());

        var submitterId = Guid.NewGuid();
        var submitResult = await harness.SubmitAsync(certificateId, submitterId, UserRole.QS);
        Assert.True(submitResult.IsSuccess);

        var qsApproverId = Guid.NewGuid();
        var approveResult = await harness.ApproveAsync(certificateId, qsApproverId, UserRole.QS, "Verified quantities.");
        Assert.True(approveResult.IsSuccess);

        var actions = await harness.LoadActionsAsync(certificateId);
        Assert.Equal(2, actions.Count);
        Assert.Contains(actions, a => a.Action == ApprovalActionType.Submit && a.ActorUserId == submitterId && a.ActorRoleAtTime == UserRole.QS);
        var approveAction = Assert.Single(actions, a => a.Action == ApprovalActionType.Approve);
        Assert.Equal(qsApproverId, approveAction.ActorUserId);
        Assert.Equal(UserRole.QS, approveAction.ActorRoleAtTime);
        Assert.Equal("Verified quantities.", approveAction.Comment);

        // The certificate's own AuditLog trail: Created (seed) + Updated (Submit) + Updated (Approve).
        var certificateAuditLogs = await harness.LoadAuditLogsAsync(nameof(PaymentCertificate), certificateId);
        Assert.Equal(3, certificateAuditLogs.Count);
        Assert.Single(certificateAuditLogs, l => l.Action == AuditAction.Created);
        Assert.Equal(2, certificateAuditLogs.Count(l => l.Action == AuditAction.Updated));

        // Each ApprovalAction row itself is also audited - a Created row per act, never Updated
        // (the entity has no mutator methods at all).
        foreach (var action in actions)
        {
            var actionAuditLogs = await harness.LoadAuditLogsAsync(nameof(ApprovalAction), action.Id);
            var log = Assert.Single(actionAuditLogs);
            Assert.Equal(AuditAction.Created, log.Action);
        }
    }

    [Fact]
    public async Task Reject_At_The_Final_Step_Also_Writes_Its_ApprovalAction_And_AuditLog_With_The_Mandatory_Comment()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, false, null, null,
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS)]);
        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 5_000_000.00m, Guid.NewGuid());
        await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);

        var rejectorId = Guid.NewGuid();
        var result = await harness.RejectAsync(certificateId, rejectorId, UserRole.QS, "Work not actually complete on site.");

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Rejected, result.Value.Status);

        var rejectAction = Assert.Single(await harness.LoadActionsAsync(certificateId), a => a.Action == ApprovalActionType.Reject);
        Assert.Equal(rejectorId, rejectAction.ActorUserId);
        Assert.Equal("Work not actually complete on site.", rejectAction.Comment);

        var certificateAuditLogs = await harness.LoadAuditLogsAsync(nameof(PaymentCertificate), certificateId);
        Assert.Contains(certificateAuditLogs, l => l.Action == AuditAction.Updated && l.AfterJson != null && l.AfterJson.Contains("\"Status\":6"));
    }
}
