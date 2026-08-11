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

    /// <summary>
    /// <b>N-03 FIX REGRESSION TEST (formerly a KNOWN-OPEN-DEFECT PIN).</b> Until this fix, this test
    /// asserted the *broken* behaviour on purpose (<c>Assert.True(savedByB)</c> - i.e. the second,
    /// uncoordinated first-voter silently co-committed instead of losing a race), specifically so the
    /// gap had a durable, enforced trace rather than going silently unverified. N-03 is now fixed
    /// (<see cref="PaymentCertificate.LastVoteAt"/>, stamped unconditionally by every accepted
    /// <see cref="PaymentCertificate.Approve"/> vote - advancing or not - so the aggregate is always
    /// <see cref="EntityState.Modified"/> and <see cref="PaymentCertificate.RowVersion"/> always has
    /// something to disagree about), so the assertions below are flipped to match: the second save now
    /// fails cleanly with a concurrency conflict, exactly like this file's advancing-case sibling
    /// <see cref="Two_Concurrent_Approvers_Loaded_Before_Either_Saves_The_Second_Repository_Save_Reports_A_Conflict_Not_A_Double_Advance"/>.
    /// Confirmed this is a genuine regression test, not a vacuous one, by re-running it against a
    /// deliberate mutation of the production fix (temporarily removing the <c>LastVoteAt = actedAt;</c>
    /// stamp from <see cref="PaymentCertificate.Approve"/>) - it fails exactly the way the old,
    /// unflipped assertions used to pass; see this task's handoff report for the transcript.
    ///
    /// <para>S9-QA-03 gap closure. Security review sprint-09.md §9.5 N-03: a non-advancing quorum vote</para>
    /// (<see cref="PaymentCertificate.Approve"/>'s <c>quorumSatisfied: false</c> early return) used to
    /// call no setter at all on the aggregate, so EF Core's change tracker left it
    /// <see cref="EntityState.Unchanged"/> - <see cref="RowVersionSaveChangesInterceptor"/> only
    /// stamps <c>Added</c>/<c>Modified</c> entries (see its own source), so no UPDATE, and therefore
    /// no optimistic-concurrency check, was ever issued against the certificate row for that
    /// <c>SaveChanges</c> call. The review labelled the consequence "not reproducible under InMemory,
    /// stated as inference" - that was incorrect for this specific race: it needs no real threads, only
    /// two contexts that both load-then-decide before either persists, exactly the same
    /// "load twice, save twice" idiom <see cref="Two_Concurrent_Approvers_Loaded_Before_Either_Saves_The_Second_Repository_Save_Reports_A_Conflict_Not_A_Double_Advance"/>
    /// above already uses for the *advancing* case - this test proved the race by actually running it
    /// against the real handler-equivalent read/decide/write sequence on real InMemory contexts, both
    /// before the fix (documenting the gap) and now (proving the fix).
    /// </summary>
    [Fact]
    public async Task Two_Concurrent_First_Voters_On_A_Quorum_Of_Two_Step_The_Second_Uncoordinated_Vote_Now_Fails_With_A_Concurrency_Conflict_N03()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.UpdatePolicyAsync(
            ApprovalDocumentType.PaymentCertificate, false, null, null,
            [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.QS, QuorumCount: 2)]);

        var certificateId = await harness.SeedDraftCertificateAsync(Guid.NewGuid(), 5_000_000.00m, Guid.NewGuid());
        var submitResult = await harness.SubmitAsync(certificateId, Guid.NewGuid(), UserRole.QS);
        Assert.True(submitResult.IsSuccess);
        Assert.Equal(1, submitResult.Value.TotalSteps);

        // Two independent contexts both load the certificate AND its (at this point still empty)
        // ApprovalAction history before either writes anything back - exactly what "two simultaneous
        // first voters" means in practice, mirroring this file's other concurrency test's own
        // "load twice, save twice" structure.
        using var contextForVoterA = harness.CreateContext();
        using var contextForVoterB = harness.CreateContext();

        var certificateRepositoryA = new PaymentCertificateRepository(contextForVoterA);
        var certificateRepositoryB = new PaymentCertificateRepository(contextForVoterB);
        var actionRepositoryA = new ApprovalActionRepository(contextForVoterA);
        var actionRepositoryB = new ApprovalActionRepository(contextForVoterB);

        var certificateSeenByA = (await certificateRepositoryA.FindAsync(certificateId))!;
        var certificateSeenByB = (await certificateRepositoryB.FindAsync(certificateId))!;

        var historySeenByA = await actionRepositoryA.GetHistoryAsync(ApprovalDocumentType.PaymentCertificate, certificateId);
        var historySeenByB = await actionRepositoryB.GetHistoryAsync(ApprovalDocumentType.PaymentCertificate, certificateId);

        // Reproduces ApprovePaymentCertificateCommandHandler's own quorum computation (lines 83-88)
        // verbatim, once per independent context - both see zero prior Approve actions for step 1,
        // because neither has saved yet.
        static bool QuorumSatisfied(IReadOnlyList<ApprovalAction> history, PaymentCertificate certificate, Guid actorId)
        {
            var priorDistinct = history
                .Where(a => a.RevisionNo == certificate.RevisionNo && a.StepNo == certificate.CurrentStepNo && a.Action == ApprovalActionType.Approve)
                .Select(a => a.ActorUserId)
                .Distinct()
                .Count();
            var quorumCount = certificate.ApprovalSteps.Single(s => s.RevisionNo == certificate.RevisionNo && s.StepNo == certificate.CurrentStepNo).QuorumCount;
            return priorDistinct + 1 >= quorumCount;
        }

        var voterA = Guid.NewGuid();
        var voterB = Guid.NewGuid();
        var quorumSatisfiedForA = QuorumSatisfied(historySeenByA, certificateSeenByA, voterA);
        var quorumSatisfiedForB = QuorumSatisfied(historySeenByB, certificateSeenByB, voterB);
        Assert.False(quorumSatisfiedForA); // each believes itself to be the first (and only) vote so far
        Assert.False(quorumSatisfiedForB);

        certificateSeenByA.Approve(voterA, UserRole.QS, UserRole.QS, allowSelfApproval: false, Now, quorumSatisfiedForA);
        certificateSeenByB.Approve(voterB, UserRole.QS, UserRole.QS, allowSelfApproval: false, Now, quorumSatisfiedForB);

        actionRepositoryA.Add(new ApprovalAction(
            harness.TenantId, ApprovalDocumentType.PaymentCertificate, certificateId, certificateSeenByA.RevisionNo, 1,
            voterA, UserRole.QS, ApprovalActionType.Approve, comment: null, Now, submitResult.Value.ApprovalPolicyId!.Value, submitResult.Value.ApprovalPolicyVersion!.Value));
        actionRepositoryB.Add(new ApprovalAction(
            harness.TenantId, ApprovalDocumentType.PaymentCertificate, certificateId, certificateSeenByB.RevisionNo, 1,
            voterB, UserRole.QS, ApprovalActionType.Approve, comment: null, Now, submitResult.Value.ApprovalPolicyId!.Value, submitResult.Value.ApprovalPolicyVersion!.Value));

        var savedByA = await certificateRepositoryA.TrySaveChangesAsync();
        var savedByB = await certificateRepositoryB.TrySaveChangesAsync();

        // THE ACTUAL N-03 FIX, PROVEN: A wins - LastVoteAt made its entry Modified, so it persists
        // cleanly and regenerates RowVersion. B's SaveChanges then races against the ALREADY-CHANGED
        // row and loses with a DbUpdateConcurrencyException, translated to `false` by
        // TrySaveChangesAsync - never a silent co-commit on the certificate's own row. This is the
        // exact defect N-03 named ("the aggregate stays Unchanged, so RowVersion never moves"), and it
        // is what makes the certificate itself unstrandable: it advances from exactly one effective
        // vote at a time, never two, regardless of how the two attempts interleave.
        Assert.True(savedByA);
        Assert.False(savedByB);

        var persisted = await harness.LoadCertificateAsync(certificateId);
        var actions = await harness.LoadActionsAsync(certificateId);

        // The certificate row reflects exactly ONE effective vote - not silently advanced twice - and,
        // critically, it is not stuck (proven by the recovery step below), unlike the pre-fix defect
        // where both votes co-committed and then NEITHER actor could ever vote again.
        Assert.Equal(PaymentCertificateStatus.PendingApproval, persisted.Status);
        Assert.Equal(1, persisted.CurrentStepNo);

        // KNOWN EF CORE IN-MEMORY LIMITATION (confirmed this task, not a production defect - see the
        // handoff report): B's ApprovalAction Add is a same-DbContext, same-SaveChanges-call sibling
        // of B's failed certificate Modified entry. On a real relational provider, EF Core wraps an
        // entire SaveChanges call's statements in one implicit transaction, so B's insert would roll
        // back together with its failed UPDATE - "exactly one ApprovalAction survives the losing
        // transaction". The in-memory provider does not support transactions AT ALL - confirmed
        // directly: `context.Database.BeginTransactionAsync()` throws "Transactions are not supported
        // by the in-memory store" under this solution's (default) warnings configuration - so it
        // commits B's Add unconditionally, independent of the sibling entity's concurrency outcome.
        // Both rows are therefore genuinely present here; asserting otherwise would make this test
        // lie about verified behaviour. This divergence from real-database semantics is called out
        // explicitly in this task's handoff report as unverifiable without SQL Server.
        var approveActorIds = actions.Where(a => a.Action == ApprovalActionType.Approve).Select(a => a.ActorUserId).ToList();
        Assert.Equal(2, approveActorIds.Count);
        Assert.Contains(voterA, approveActorIds);
        Assert.Contains(voterB, approveActorIds);

        // Prove the step is not permanently stuck regardless. Because the (in-memory-specific) evidence
        // trail already shows both A's and B's votes, `DuplicateChainVoter` (ADR-0016; was
        // `DuplicateChainApprover`) would bar either of them from voting again through the real
        // handler - so the realistic recovery actor is a THIRD,
        // distinct QS, whose very first vote already observes 2 prior distinct approvers and clears the
        // step on the spot. (On real SQL Server, where B's losing attempt would have left no trace at
        // all, the realistic recovery actor is B itself retrying - the production-facing guarantee is
        // identical either way: the step always remains recoverable by a legitimate role-holder who has
        // not yet successfully voted, never permanently stuck the way the pre-fix defect left it.)
        var voterC = Guid.NewGuid();
        using var contextForRetry = harness.CreateContext();
        var certificateRepositoryRetry = new PaymentCertificateRepository(contextForRetry);
        var actionRepositoryRetry = new ApprovalActionRepository(contextForRetry);

        var certificateSeenByRetry = (await certificateRepositoryRetry.FindAsync(certificateId))!;
        var historySeenByRetry = await actionRepositoryRetry.GetHistoryAsync(ApprovalDocumentType.PaymentCertificate, certificateId);
        var quorumSatisfiedForRetry = QuorumSatisfied(historySeenByRetry, certificateSeenByRetry, voterC);
        Assert.True(quorumSatisfiedForRetry); // sees both A's and B's already-persisted votes

        certificateSeenByRetry.Approve(voterC, UserRole.QS, UserRole.QS, allowSelfApproval: false, Now, quorumSatisfiedForRetry);
        actionRepositoryRetry.Add(new ApprovalAction(
            harness.TenantId, ApprovalDocumentType.PaymentCertificate, certificateId, certificateSeenByRetry.RevisionNo, 1,
            voterC, UserRole.QS, ApprovalActionType.Approve, comment: null, Now, submitResult.Value.ApprovalPolicyId!.Value, submitResult.Value.ApprovalPolicyVersion!.Value));
        Assert.True(await certificateRepositoryRetry.TrySaveChangesAsync());

        var finalCertificate = await harness.LoadCertificateAsync(certificateId);
        Assert.Equal(PaymentCertificateStatus.Certified, finalCertificate.Status); // quorum of 2 genuinely reached, recoverable
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
