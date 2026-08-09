using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Integration.Tests.Approval;

/// <summary>
/// ADR-0008/approval-workflow.md §5.2 "version-pin, never mutate", proven against a real
/// <c>CmPlusDbContext</c>: a S9-BE-06 policy edit (which creates <c>Version+1</c> and deactivates
/// the previous version) must never retroactively change how an already-<c>PendingApproval</c>
/// document is approved. This is the single most important behaviour S9-BE-05/06 add on top of
/// Sprint 2's engine - the whole point of pinning <c>ApprovalPolicyId</c>/<c>ApprovalPolicyVersion</c>
/// onto the document at Submit time instead of re-resolving "the currently active policy" on every
/// subsequent transition.
/// </summary>
public class PaymentCertificateVersionPinningTests
{
    private static readonly DateTimeOffset EffectiveFrom = DateTimeOffset.Parse("2025-01-01T00:00:00+07:00");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    private static readonly IReadOnlyList<ApprovalPolicyRuleInput> IpcV1Rules =
    [
        new(1, 0.00m, null, UserRole.QS),
        new(2, 0.00m, null, UserRole.PM),
    ];

    [Fact]
    public async Task An_In_Flight_Certificate_Keeps_Requiring_V1s_Role_After_The_Policy_Is_Edited_To_V2()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);

        // v1: step 1 QS, step 2 PM.
        var v1Result = await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, allowSelfApproval: false, null, null, IpcV1Rules);
        Assert.True(v1Result.IsSuccess);
        Assert.Equal(1, v1Result.Value.Version);

        var projectId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var certificateId = await harness.SeedDraftCertificateAsync(projectId, 5_000_000.00m, creatorId);

        var submitResult = await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(submitResult.IsSuccess);
        Assert.Equal(1, submitResult.Value.ApprovalPolicyVersion); // pinned to v1
        var pinnedPolicyId = submitResult.Value.ApprovalPolicyId;

        var afterQs = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(afterQs.IsSuccess);
        Assert.Equal(2, afterQs.Value.CurrentStepNo); // now waiting on step 2 (PM, per v1)

        // Now edit the policy: v2's step 2 requires Executive instead of PM - a real DoA change an
        // admin might make (e.g. tightening authority) after this certificate was already submitted.
        var v2Result = await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, allowSelfApproval: false, null, null,
            [
                new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS),
                new ApprovalPolicyRuleInput(2, 0.00m, null, UserRole.Executive),
            ]);
        Assert.True(v2Result.IsSuccess);
        Assert.Equal(2, v2Result.Value.Version);
        Assert.NotEqual(pinnedPolicyId, default);

        // v1 is now deactivated tenant-wide...
        var activePolicy = await LoadActiveTenantDefaultAsync(harness, ApprovalDocumentType.PaymentCertificate);
        Assert.Equal(2, activePolicy!.Version);

        // ...but the in-flight certificate is UNAFFECTED: an Executive (v2's new rule) is still
        // rejected, and a PM (v1's original, pinned rule) still succeeds.
        var executiveAttempt = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.Executive);
        Assert.True(executiveAttempt.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", executiveAttempt.Error);

        var pmApproval = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.PM);
        Assert.True(pmApproval.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Certified, pmApproval.Value.Status);

        // The certified certificate's own pinned policy version never changed either.
        var certifiedCertificate = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(1, certifiedCertificate.ApprovalPolicyVersion);
        Assert.Equal(pinnedPolicyId, certifiedCertificate.ApprovalPolicyId);
    }

    [Fact]
    public async Task A_Brand_New_Submission_After_The_Edit_Routes_Against_The_New_Version_Never_The_Old_One()
    {
        // The other half of the proof: version-pinning protects IN-FLIGHT documents specifically -
        // it must not make the write API inert. A document submitted AFTER the edit uses v2.
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.UpdatePolicyAsync(ApprovalDocumentType.PaymentCertificate, false, null, null, IpcV1Rules);
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, false, null, null,
            [
                new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS),
                new ApprovalPolicyRuleInput(2, 0.00m, null, UserRole.Executive),
            ]);

        var projectId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var certificateId = await harness.SeedDraftCertificateAsync(projectId, 5_000_000.00m, creatorId);

        var submitResult = await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(submitResult.IsSuccess);
        Assert.Equal(2, submitResult.Value.ApprovalPolicyVersion);

        var afterQs = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(afterQs.IsSuccess);

        // PM (v1's now-superseded rule) is rejected; Executive (v2's real, current rule) succeeds.
        var pmAttempt = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.PM);
        Assert.True(pmAttempt.IsFailure);
        Assert.Equal("PaymentCertificateNotAuthorizedForApprovalStep", pmAttempt.Error);

        var executiveApproval = await harness.ApproveAsync(certificateId, Guid.NewGuid(), UserRole.Executive);
        Assert.True(executiveApproval.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Certified, executiveApproval.Value.Status);
    }

    [Fact]
    public async Task Update_Rejects_A_Non_Contiguous_StepNo_Gap_With_A_400_Style_Result_Identifying_The_StepNo()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);

        var result = await harness.UpdatePolicyAsync(
            ApprovalDocumentType.VariationOrder, false, null, null,
            [
                new(1, 0.00m, 500_000.00m, UserRole.PM),
                new(1, 500_000.00m, 5_000_000.00m, UserRole.PM),
                new(3, 500_000.00m, 5_000_000.00m, UserRole.Executive), // StepNo 2 missing
            ]);

        Assert.True(result.IsFailure);
        Assert.Equal("ApprovalPolicyBandGap:2", result.Error);
    }

    [Fact]
    public async Task Update_Rejects_Overlapping_Same_StepNo_Ranges_With_A_400_Style_Result_Identifying_The_StepNo()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);

        var result = await harness.UpdatePolicyAsync(
            ApprovalDocumentType.VariationOrder, false, null, null,
            [
                new(1, 0.00m, 1_000_000.00m, UserRole.PM),
                new(1, 500_000.00m, 2_000_000.00m, UserRole.QS),
            ]);

        Assert.True(result.IsFailure);
        Assert.Equal("ApprovalPolicyBandOverlap:1", result.Error);
    }

    private static async Task<ApprovalPolicy?> LoadActiveTenantDefaultAsync(ApprovalWorkflowHarness harness, ApprovalDocumentType documentType)
    {
        using var context = harness.CreateContext();
        var reader = new CMPlus.Infrastructure.Persistence.ApprovalPolicyReader(context);
        return await reader.GetActiveTenantDefaultPolicyAsync(documentType);
    }
}
