using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Integration.Tests.Approval;

/// <summary>
/// ADR-0016 / domain-rules.md §8 ("quorum binds rejection") - end-to-end proof against the real
/// <see cref="CMPlus.Infrastructure.Persistence.CmPlusDbContext"/> (EF Core InMemory, per the Docker
/// outage), the real command handlers and the real interceptors, via <see cref="ApprovalWorkflowHarness"/> -
/// the same harness/discipline <c>PaymentCertificateConcurrencyAndAuditIntegrationTests</c> and
/// <c>PaymentCertificateChainSnapshotSecurityTests</c> already use. Fixtures V-11a-f
/// (domain-rules.md §8.5), against policy <c>TH-DualControl-VO</c>'s shape - one band, StepNo 1,
/// <c>QuorumCount = 2</c> - reproduced here for the Payment Certificate (the shipped Sprint 9
/// aggregate this ADR retrofits), not the not-yet-built VariationOrder.
///
/// <para>This is the inverse of security review sprint-09.md §9.5's N-05 probe: before this fix,
/// V-11a's sequence (one actor approves 1-of-2, then rejects) took the certificate straight to the
/// terminal <c>Rejected</c> state alone - execution-verified in that review. Every test below proves
/// that no longer happens.</para>
/// </summary>
public class PaymentCertificateRejectQuorumIntegrationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

    private static async Task<(ApprovalWorkflowHarness Harness, Guid CertificateId)> SeedSubmittedCertificateAsync(int finalStepQuorumCount)
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, false, null, null,
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.ProjectDirector, QuorumCount: finalStepQuorumCount)]);

        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 5_000_000.00m, Guid.NewGuid());
        var submitResult = await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.ProjectDirector);
        Assert.True(submitResult.IsSuccess);
        Assert.Equal(1, submitResult.Value.TotalSteps);

        return (harness, certificateId);
    }

    [Fact]
    public async Task V11a_An_Actor_Who_Approved_1_Of_2_Can_No_Longer_Reject_The_Same_Revision()
    {
        var (harness, certificateId) = await SeedSubmittedCertificateAsync(finalStepQuorumCount: 2);
        var pdA = Guid.NewGuid();

        var approveResult = await harness.ApproveAsync(certificateId, pdA, UserRole.ProjectDirector);
        Assert.True(approveResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, approveResult.Value.Status); // 1 of 2, not cleared

        // THE EXACT N-05 SCENARIO: today (pre-fix) this took the certificate straight to Rejected.
        var rejectResult = await harness.RejectAsync(certificateId, pdA, UserRole.ProjectDirector, "Changed my mind.");

        Assert.True(rejectResult.IsFailure);
        Assert.Equal("PaymentCertificateDuplicateChainVoter", rejectResult.Error);

        var persisted = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, persisted.Status); // still alive, not Rejected
    }

    [Fact]
    public async Task V11b_Two_Distinct_Rejectors_Are_Required_Before_QuorumCount_Two_Terminates()
    {
        var (harness, certificateId) = await SeedSubmittedCertificateAsync(finalStepQuorumCount: 2);
        var pdA = Guid.NewGuid();
        var pdB = Guid.NewGuid();

        var afterFirstReject = await harness.RejectAsync(certificateId, pdA, UserRole.ProjectDirector, "First rejection.");
        Assert.True(afterFirstReject.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, afterFirstReject.Value.Status); // today (pre-fix): already Rejected here

        var persistedAfterFirst = await harness.LoadCertificateAsync(certificateId);
        Assert.NotNull(persistedAfterFirst.LastVoteAt); // N-03 parity

        var afterSecondReject = await harness.RejectAsync(certificateId, pdB, UserRole.ProjectDirector, "Second rejection.");
        Assert.True(afterSecondReject.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Rejected, afterSecondReject.Value.Status); // now genuinely terminal

        var actions = await harness.LoadActionsAsync(certificateId);
        var rejectActorIds = actions.Where(a => a.Action == ApprovalActionType.Reject).Select(a => a.ActorUserId).ToList();
        Assert.Equal(2, rejectActorIds.Count);
        Assert.Contains(pdA, rejectActorIds);
        Assert.Contains(pdB, rejectActorIds);
    }

    [Fact]
    public async Task V11c_V11d_A_Split_Committee_Deadlocks_Both_Quorums_But_ReturnForRevision_Escapes_It()
    {
        var (harness, certificateId) = await SeedSubmittedCertificateAsync(finalStepQuorumCount: 2);
        var pdA = Guid.NewGuid();
        var pdB = Guid.NewGuid();
        var pdC = Guid.NewGuid();

        var afterApprove = await harness.ApproveAsync(certificateId, pdA, UserRole.ProjectDirector);
        Assert.True(afterApprove.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, afterApprove.Value.Status);

        var afterReject = await harness.RejectAsync(certificateId, pdB, UserRole.ProjectDirector, "I disagree.");
        Assert.True(afterReject.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, afterReject.Value.Status); // neither quorum satisfied - the split

        // Both original voters are now locked out of voting again, in EITHER direction.
        var aTriesReject = await harness.RejectAsync(certificateId, pdA, UserRole.ProjectDirector, "Switching.");
        Assert.True(aTriesReject.IsFailure);
        Assert.Equal("PaymentCertificateDuplicateChainVoter", aTriesReject.Error);

        var bTriesApprove = await harness.ApproveAsync(certificateId, pdB, UserRole.ProjectDirector);
        Assert.True(bTriesApprove.IsFailure);
        Assert.Equal("PaymentCertificateDuplicateChainVoter", bTriesApprove.Error);

        // V-11d: the deadlock is not permanent - ReturnForRevision is deliberately not quorum-bound,
        // so a THIRD holder of the (only, still-pending) step's role can send it back.
        var returnResult = await harness.ReturnForRevisionAsync(certificateId, pdC, UserRole.ProjectDirector, "Split committee - returning.");

        Assert.True(returnResult.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Draft, returnResult.Value.Status);

        var persisted = await harness.LoadCertificateWithStepsAsync(certificateId);
        Assert.Equal(2, persisted.RevisionNo);
        Assert.Empty(persisted.ApprovalSteps); // chain snapshot voided - genuinely deleted, not merely cleared
    }

    [Fact]
    public async Task V11e_A_Split_Committee_Can_Also_Resolve_By_Reaching_Reject_Quorum_Instead_Of_Returning()
    {
        var (harness, certificateId) = await SeedSubmittedCertificateAsync(finalStepQuorumCount: 2);
        var pdA = Guid.NewGuid();
        var pdB = Guid.NewGuid();
        var pdC = Guid.NewGuid();

        await harness.ApproveAsync(certificateId, pdA, UserRole.ProjectDirector); // 1 approval
        await harness.RejectAsync(certificateId, pdB, UserRole.ProjectDirector, "I disagree."); // 1 rejection - split

        // PD-C (a third, never-voted actor) rejects too -> reject-quorum (2) now satisfied
        // notwithstanding PD-A's earlier approval, which stays on the record as evidence.
        var result = await harness.RejectAsync(certificateId, pdC, UserRole.ProjectDirector, "Agreeing with B - rejecting.");

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Rejected, result.Value.Status);

        var actions = await harness.LoadActionsAsync(certificateId);
        Assert.Single(actions, a => a.Action == ApprovalActionType.Approve && a.ActorUserId == pdA);
        Assert.Equal(2, actions.Count(a => a.Action == ApprovalActionType.Reject));
    }

    [Fact]
    public async Task V11f_QuorumCount_One_Is_Completely_Unaffected_A_Single_Rejector_Still_Terminates_Immediately()
    {
        var (harness, certificateId) = await SeedSubmittedCertificateAsync(finalStepQuorumCount: 1);

        var result = await harness.RejectAsync(certificateId, Guid.NewGuid(), UserRole.ProjectDirector, "Not acceptable.");

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentCertificateStatus.Rejected, result.Value.Status);

        var persisted = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(PaymentCertificateStatus.Rejected, persisted.Status);
    }
}
