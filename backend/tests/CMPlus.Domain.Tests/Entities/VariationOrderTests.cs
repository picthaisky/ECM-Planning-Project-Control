using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>
/// S10-BE-01: the five-state machine, the field-freeze matrix, and the VO-specific rules
/// domain-rules.md §2.2 lists as differences from <see cref="PaymentCertificate"/> (no
/// <c>NotDue</c>/<c>Paid</c>, quorum-bound <c>Reject</c> from day one, <see cref="VariationOrderType"/>
/// derived from <c>Amount</c>'s sign, the $\Delta B_{scope}=A$ invariant). Mirrors
/// <c>PaymentCertificateTests</c>'s structure and coverage shape closely.
/// </summary>
public class VariationOrderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

    private static VariationOrder CreateDraft(
        decimal amount = 300_000.00m,
        int timeImpactDays = 0,
        IReadOnlyList<VariationOrderScopeItemInput>? scopeItems = null,
        Guid? createdByUserId = null) =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), $"VO-{Guid.NewGuid():N}", createdByUserId ?? Guid.NewGuid(),
            amount, "Additional works", "Site instruction #4", timeImpactDays,
            scopeItems ?? [new VariationOrderScopeItemInput(Guid.NewGuid(), amount)]);

    private static IReadOnlyList<VariationOrderApprovalStepInput> TwoStepChain() =>
        [new(1, UserRole.PM, 1), new(2, UserRole.ProjectDirector, 1)];

    // ---- Construction / Type derivation ----

    [Fact]
    public void Constructor_Starts_At_Draft_With_RevisionNo_One_And_No_Chain()
    {
        var vo = CreateDraft();

        Assert.Equal(VariationOrderStatus.Draft, vo.Status);
        Assert.Equal(1, vo.RevisionNo);
        Assert.Equal(0, vo.CurrentStepNo);
        Assert.Equal(0, vo.TotalSteps);
        Assert.Empty(vo.ApprovalSteps);
        Assert.Null(vo.ApprovedAt);
        Assert.Null(vo.BacBefore);
    }

    [Theory]
    [InlineData(2_400_000.00, VariationOrderType.Add)]
    [InlineData(0, VariationOrderType.Add)] // domain-rules.md §1: zero is grouped with Add - "it omits nothing"
    [InlineData(-800_000.00, VariationOrderType.Deduct)]
    public void Type_Is_Derived_From_The_Sign_Of_Amount_Never_Independently_Settable(decimal amount, VariationOrderType expected)
    {
        var scopeItems = amount == 0m
            ? [] // EmptyVariation would otherwise throw; TimeImpactDays > 0 keeps this a real, if unusual, time-only VO.
            : new List<VariationOrderScopeItemInput> { new(Guid.NewGuid(), amount) };
        var vo = CreateDraft(amount, timeImpactDays: amount == 0m ? 5 : 0, scopeItems: scopeItems);

        Assert.Equal(expected, vo.Type);
    }

    [Fact]
    public void Constructor_Rejects_Empty_ProjectId_And_Empty_CreatedByUserId()
    {
        Assert.Throws<DomainException>(() => new VariationOrder(
            Guid.NewGuid(), Guid.Empty, "VO-1", Guid.NewGuid(), 100m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 100m)]));

        Assert.Throws<DomainException>(() => new VariationOrder(
            Guid.NewGuid(), Guid.NewGuid(), "VO-1", Guid.Empty, 100m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 100m)]));
    }

    [Fact]
    public void Constructor_Rejects_Blank_VoNumber()
    {
        Assert.Throws<DomainException>(() => new VariationOrder(
            Guid.NewGuid(), Guid.NewGuid(), "   ", Guid.NewGuid(), 100m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 100m)]));
    }

    [Fact]
    public void Constructor_Rejects_More_Than_Two_Decimal_Places_On_Amount()
    {
        Assert.Throws<DomainException>(() => new VariationOrder(
            Guid.NewGuid(), Guid.NewGuid(), "VO-1", Guid.NewGuid(), 100.005m, null, null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 100.005m)]));
    }

    /// <summary>domain-rules.md §5.2's hard invariant: $\Delta B_{scope} = A$ to the cent, enforced
    /// continuously (constructor AND <see cref="VariationOrder.SetVariationContent"/>), not only at
    /// submit time.</summary>
    [Fact]
    public void Constructor_Rejects_A_Scope_Payload_Whose_Net_Delta_Does_Not_Equal_Amount()
    {
        var ex = Assert.Throws<DomainException>(() => new VariationOrder(
            Guid.NewGuid(), Guid.NewGuid(), "VO-1", Guid.NewGuid(), amount: 500_000.00m, null, null, 0,
            scopeItems: [new VariationOrderScopeItemInput(Guid.NewGuid(), 499_999.99m)]));

        Assert.Contains("VoScopeBudgetMismatch", ex.Message);
    }

    [Fact]
    public void Constructor_Rejects_A_Genuinely_Empty_Variation_Zero_Amount_Zero_TimeImpact_Empty_Scope()
    {
        var ex = Assert.Throws<DomainException>(() => new VariationOrder(
            Guid.NewGuid(), Guid.NewGuid(), "VO-1", Guid.NewGuid(), amount: 0m, null, null, timeImpactDays: 0,
            scopeItems: []));

        Assert.Contains("EmptyVariation", ex.Message);
    }

    [Fact]
    public void Constructor_Permits_A_Time_Only_Variation_Zero_Amount_With_Nonzero_TimeImpactDays()
    {
        var vo = new VariationOrder(
            Guid.NewGuid(), Guid.NewGuid(), "VO-1", Guid.NewGuid(), amount: 0m, "EOT only", null, timeImpactDays: 14,
            scopeItems: []);

        Assert.Equal(0m, vo.Amount);
        Assert.Equal(VariationOrderType.Add, vo.Type);
        Assert.Equal(14, vo.TimeImpactDays);
    }

    // ---- SetVariationContent / freeze ----

    [Fact]
    public void SetVariationContent_Works_While_Draft_And_Replaces_The_Whole_Scope_Collection()
    {
        var vo = CreateDraft(amount: 400_000.00m);
        var newActivityId = Guid.NewGuid();

        vo.SetVariationContent(600_000.00m, "Re-priced", "Updated justification", 3, [new VariationOrderScopeItemInput(newActivityId, 600_000.00m)]);

        Assert.Equal(600_000.00m, vo.Amount);
        Assert.Equal(3, vo.TimeImpactDays);
        Assert.Equal(newActivityId, Assert.Single(vo.ScopeItems).ActivityId);
    }

    [Fact]
    public void SetVariationContent_Throws_Once_The_Vo_Leaves_Draft_Money_And_Scope_Are_Frozen()
    {
        var vo = CreateDraft();
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        Assert.Throws<DomainException>(() => vo.SetVariationContent(
            999_999.00m, "x", "y", 1, [new VariationOrderScopeItemInput(Guid.NewGuid(), 999_999.00m)]));
    }

    // ---- Submit ----

    [Fact]
    public void Submit_Moves_Draft_To_PendingApproval_Step_One_And_Snapshots_The_Chain()
    {
        var vo = CreateDraft();
        var policyId = Guid.NewGuid();

        vo.Submit(TwoStepChain(), policyId, 3, allowSelfApproval: true, Guid.NewGuid(), Now);

        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status);
        Assert.Equal(1, vo.CurrentStepNo);
        Assert.Equal(2, vo.TotalSteps);
        Assert.Equal(policyId, vo.ApprovalPolicyId);
        Assert.Equal(3, vo.ApprovalPolicyVersion);
        Assert.True(vo.AllowSelfApproval);
        Assert.Equal(Now, vo.SubmittedAt);
        Assert.Equal(2, vo.ApprovalSteps.Count);
        Assert.All(vo.ApprovalSteps, s => Assert.Equal(1, s.RevisionNo));
    }

    [Fact]
    public void Submit_Throws_When_Not_Draft()
    {
        var vo = CreateDraft();
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        Assert.Throws<DomainException>(() => vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now));
    }

    [Fact]
    public void Submit_Rejects_An_Empty_Chain_ApprovalPolicyGap()
    {
        var vo = CreateDraft();

        var ex = Assert.Throws<DomainException>(() => vo.Submit([], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now));
        Assert.Contains("ApprovalPolicyGap", ex.Message);
    }

    [Fact]
    public void Submit_Rejects_A_Null_SubmittedByUserId()
    {
        var vo = CreateDraft();

        Assert.Throws<DomainException>(() => vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.Empty, Now));
    }

    // ---- Approve / WouldFinalize ----

    [Fact]
    public void Approve_On_A_Single_Step_Chain_Goes_Straight_To_Approved_And_Stamps_ApprovedAt()
    {
        var vo = CreateDraft();
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, Now);

        Assert.Equal(VariationOrderStatus.Approved, vo.Status);
        Assert.Equal(Now, vo.ApprovedAt);
    }

    [Fact]
    public void Approve_On_A_Multi_Step_Chain_Advances_StepNo_Without_Leaving_PendingApproval()
    {
        var vo = CreateDraft();
        vo.Submit(TwoStepChain(), Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, Now);

        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status);
        Assert.Equal(2, vo.CurrentStepNo);
    }

    [Fact]
    public void WouldFinalize_Is_True_Only_When_Quorum_Is_Satisfied_And_This_Is_The_Last_Step()
    {
        var vo = CreateDraft();
        vo.Submit(TwoStepChain(), Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        Assert.False(vo.WouldFinalize(quorumSatisfied: true)); // step 1 of 2
        vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, Now);

        Assert.False(vo.WouldFinalize(quorumSatisfied: false)); // step 2 of 2, but quorum not yet met
        Assert.True(vo.WouldFinalize(quorumSatisfied: true)); // step 2 of 2, quorum met -> this vote finalizes
    }

    [Fact]
    public void Approve_A_Quorum_Two_Step_Does_Not_Advance_On_The_First_Vote_But_Still_Stamps_LastVoteAt()
    {
        var vo = CreateDraft();
        vo.Submit([new(1, UserRole.PM, QuorumCount: 2)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, Now, quorumSatisfied: false);

        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status);
        Assert.Equal(1, vo.CurrentStepNo);
        Assert.Equal(Now, vo.LastVoteAt);
    }

    [Fact]
    public void Approve_Blocks_The_Creator_From_Self_Approving_By_Default()
    {
        var creatorId = Guid.NewGuid();
        var vo = CreateDraft(createdByUserId: creatorId);
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        Assert.Throws<DomainException>(() => vo.Approve(creatorId, UserRole.PM, UserRole.PM, allowSelfApproval: false, Now));
    }

    [Fact]
    public void Approve_Allows_Self_Approval_When_The_Pinned_Policy_Opted_In()
    {
        var creatorId = Guid.NewGuid();
        var vo = CreateDraft(createdByUserId: creatorId);
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, allowSelfApproval: true, creatorId, Now);

        vo.Approve(creatorId, UserRole.PM, UserRole.PM, allowSelfApproval: true, Now);

        Assert.Equal(VariationOrderStatus.Approved, vo.Status);
    }

    [Fact]
    public void Approve_Rejects_An_Actor_Whose_Role_Does_Not_Match_The_Current_Steps_Required_Role()
    {
        var vo = CreateDraft();
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        Assert.Throws<DomainException>(() => vo.Approve(Guid.NewGuid(), UserRole.Site, UserRole.PM, allowSelfApproval: false, Now));
    }

    [Fact]
    public void Approve_Throws_When_Not_PendingApproval()
    {
        var vo = CreateDraft();

        Assert.Throws<DomainException>(() => vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, Now));
    }

    // ---- RecordApprovalEffects ----

    [Fact]
    public void RecordApprovalEffects_Stamps_The_Immutable_Before_After_Figures_Once_Approved()
    {
        var vo = CreateDraft(amount: 2_400_000.00m);
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, Now);

        vo.RecordApprovalEffects(
            bacBefore: 100_000_000.00m, bacAfter: 102_400_000.00m,
            contractValueBefore: 100_000_000.00m, contractValueAfter: 102_400_000.00m,
            cumulativeVoPctAtApproval: 9.7320m, escalationBasisContractValue: 485_000_000.00m);

        Assert.Equal(100_000_000.00m, vo.BacBefore);
        Assert.Equal(102_400_000.00m, vo.BacAfter);
        Assert.Equal(100_000_000.00m, vo.ContractValueBefore);
        Assert.Equal(102_400_000.00m, vo.ContractValueAfter);
        Assert.Equal(9.7320m, vo.CumulativeVoPctAtApproval);
        Assert.Equal(485_000_000.00m, vo.EscalationBasisContractValue);
    }

    [Fact]
    public void RecordApprovalEffects_Throws_Unless_Approved()
    {
        var vo = CreateDraft();
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        // Not yet Approved (still PendingApproval).

        Assert.Throws<DomainException>(() => vo.RecordApprovalEffects(0m, 0m, 0m, 0m, null, null));
    }

    [Fact]
    public void RecordApprovalEffects_Cannot_Be_Called_Twice_Idempotency_Guard()
    {
        var vo = CreateDraft();
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, Now);
        vo.RecordApprovalEffects(1m, 2m, 1m, 2m, null, null);

        Assert.Throws<DomainException>(() => vo.RecordApprovalEffects(1m, 2m, 1m, 2m, null, null));
    }

    // ---- Reject: quorum-bound from day one (ADR-0016 / domain-rules.md §8) ----

    [Fact]
    public void Reject_From_The_Final_Step_With_QuorumCount_One_Is_Terminal_Immediately()
    {
        var vo = CreateDraft();
        vo.Submit(TwoStepChain(), Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, Now); // step 1 -> 2

        vo.Reject(UserRole.ProjectDirector, UserRole.ProjectDirector, Now);

        Assert.Equal(VariationOrderStatus.Rejected, vo.Status);
    }

    [Fact]
    public void Reject_Is_Refused_From_An_Intermediate_Step_Must_ReturnForRevision_Instead()
    {
        var vo = CreateDraft();
        vo.Submit(TwoStepChain(), Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        // Still step 1 of 2 - not the final step.

        Assert.Throws<DomainException>(() => vo.Reject(UserRole.PM, UserRole.PM, Now));
    }

    [Fact]
    public void Reject_With_QuorumCount_Two_Does_Not_Terminate_On_The_First_Vote_But_Stamps_LastVoteAt()
    {
        var vo = CreateDraft();
        vo.Submit([new(1, UserRole.ProjectDirector, QuorumCount: 2)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        vo.Reject(UserRole.ProjectDirector, UserRole.ProjectDirector, Now, rejectQuorumSatisfied: false);

        Assert.Equal(VariationOrderStatus.PendingApproval, vo.Status);
        Assert.Equal(Now, vo.LastVoteAt);
    }

    [Fact]
    public void Reject_With_QuorumCount_Two_Terminates_Once_The_Second_Distinct_Rejector_Votes()
    {
        var vo = CreateDraft();
        vo.Submit([new(1, UserRole.ProjectDirector, QuorumCount: 2)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        vo.Reject(UserRole.ProjectDirector, UserRole.ProjectDirector, Now, rejectQuorumSatisfied: false);

        vo.Reject(UserRole.ProjectDirector, UserRole.ProjectDirector, Now, rejectQuorumSatisfied: true);

        Assert.Equal(VariationOrderStatus.Rejected, vo.Status);
    }

    // ---- ReturnForRevision ----

    [Fact]
    public void ReturnForRevision_Bumps_RevisionNo_Voids_The_Chain_And_Unfreezes_Content()
    {
        var vo = CreateDraft();
        vo.Submit(TwoStepChain(), Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        vo.ReturnForRevision();

        Assert.Equal(VariationOrderStatus.Draft, vo.Status);
        Assert.Equal(2, vo.RevisionNo);
        Assert.Equal(0, vo.CurrentStepNo);
        Assert.Equal(0, vo.TotalSteps);
        Assert.Empty(vo.ApprovalSteps);
        Assert.Null(vo.ApprovalPolicyId);
        Assert.Null(vo.SubmittedAt);
        // Unfrozen: re-pricing must now succeed again.
        vo.SetVariationContent(999_999.00m, "x", "y", 1, [new VariationOrderScopeItemInput(Guid.NewGuid(), 999_999.00m)]);
        Assert.Equal(999_999.00m, vo.Amount);
    }

    [Fact]
    public void ReturnForRevision_Throws_When_Not_PendingApproval()
    {
        var vo = CreateDraft();

        Assert.Throws<DomainException>(() => vo.ReturnForRevision());
    }

    // ---- Withdraw ----

    [Fact]
    public void Withdraw_By_The_Submitter_Before_Any_Approval_Returns_To_Draft_RevisionNo_Unchanged()
    {
        var submitterId = Guid.NewGuid();
        var vo = CreateDraft();
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, submitterId, Now);

        vo.Withdraw(submitterId);

        Assert.Equal(VariationOrderStatus.Draft, vo.Status);
        Assert.Equal(1, vo.RevisionNo); // unchanged, unlike ReturnForRevision
        Assert.Empty(vo.ApprovalSteps);
    }

    [Fact]
    public void Withdraw_Is_Refused_For_Anyone_Other_Than_The_Submitter()
    {
        var vo = CreateDraft();
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        Assert.Throws<DomainException>(() => vo.Withdraw(Guid.NewGuid()));
    }

    [Fact]
    public void Withdraw_Is_Refused_Once_At_Least_One_Step_Has_Cleared()
    {
        var submitterId = Guid.NewGuid();
        var vo = CreateDraft();
        vo.Submit(TwoStepChain(), Guid.NewGuid(), 1, false, submitterId, Now);
        vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, Now); // step 1 -> 2

        Assert.Throws<DomainException>(() => vo.Withdraw(submitterId));
    }

    // ---- Cancel ----

    [Fact]
    public void Cancel_By_The_Creator_Moves_Draft_To_Cancelled_Terminal()
    {
        var creatorId = Guid.NewGuid();
        var vo = CreateDraft(createdByUserId: creatorId);

        vo.Cancel(creatorId, UserRole.QS);

        Assert.Equal(VariationOrderStatus.Cancelled, vo.Status);
    }

    [Fact]
    public void Cancel_By_A_PM_Who_Is_Not_The_Creator_Is_Also_Permitted()
    {
        var vo = CreateDraft(createdByUserId: Guid.NewGuid());

        vo.Cancel(Guid.NewGuid(), UserRole.PM);

        Assert.Equal(VariationOrderStatus.Cancelled, vo.Status);
    }

    [Fact]
    public void Cancel_Is_Refused_For_Neither_Creator_Nor_PM()
    {
        var vo = CreateDraft(createdByUserId: Guid.NewGuid());

        Assert.Throws<DomainException>(() => vo.Cancel(Guid.NewGuid(), UserRole.QS));
    }

    [Fact]
    public void Cancel_Throws_When_Not_Draft()
    {
        var creatorId = Guid.NewGuid();
        var vo = CreateDraft(createdByUserId: creatorId);
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        Assert.Throws<DomainException>(() => vo.Cancel(creatorId, UserRole.QS));
    }

    // ---- Terminal states: nothing leaves them ----

    [Theory]
    [InlineData(VariationOrderStatus.Approved)]
    [InlineData(VariationOrderStatus.Rejected)]
    [InlineData(VariationOrderStatus.Cancelled)]
    public void No_Transition_Method_Succeeds_From_A_Terminal_Status(VariationOrderStatus terminalStatus)
    {
        var vo = CreateDraft();
        vo.Submit([new(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);

        switch (terminalStatus)
        {
            case VariationOrderStatus.Approved:
                vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, Now);
                break;
            case VariationOrderStatus.Rejected:
                vo.Reject(UserRole.PM, UserRole.PM, Now);
                break;
            case VariationOrderStatus.Cancelled:
                // Cancel only fires from Draft - reach it via a fresh Draft VO instead of the
                // submitted one above.
                vo = CreateDraft();
                vo.Cancel(vo.CreatedByUserId, UserRole.PM);
                break;
        }

        Assert.Equal(terminalStatus, vo.Status);
        Assert.Throws<DomainException>(() => vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, Now));
        Assert.Throws<DomainException>(() => vo.Reject(UserRole.PM, UserRole.PM, Now));
        Assert.Throws<DomainException>(() => vo.ReturnForRevision());
        Assert.Throws<DomainException>(() => vo.Withdraw(Guid.NewGuid()));
        Assert.Throws<DomainException>(() => vo.Cancel(Guid.NewGuid(), UserRole.PM));
    }
}
