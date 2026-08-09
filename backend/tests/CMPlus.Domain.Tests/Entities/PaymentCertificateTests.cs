using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>
/// S9-BE-01 DoD: every transition matches approval-workflow.md §4's table; money fields freeze from
/// <c>PendingApproval</c> onward; <c>Certified</c> is immutable forever; <see cref="PaymentCertificate.RowVersion"/>
/// exists as the optimistic-concurrency token (the actual 409-on-conflict behaviour is proven against
/// a real DbContext in <c>CMPlus.Integration.Tests.Persistence.PaymentCertificateConcurrencyTests</c>,
/// since EF Core is the layer that raises <c>DbUpdateConcurrencyException</c>, not this entity).
/// </summary>
public class PaymentCertificateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();

    private static PaymentCertificate CreateDraft(decimal previousCumulativeApprovePct = 0m, Guid? createdBy = null) =>
        new(TenantId, ProjectId, milestoneNo: 1, "Structural works - period 1", milestoneValue: 21_600_000.00m,
            previousCumulativeApprovePct, createdBy ?? CreatorId);

    private static void ClaimFullPeriod(PaymentCertificate certificate, decimal approvePct = 100.00m) =>
        certificate.SetPeriodClaim(
            approvePct, claimPct: approvePct, actualProgressPct: approvePct,
            grossCertifiedAmount: 21_600_000.00m, retentionAmount: 1_080_000.00m,
            advanceRecoveryAmount: 2_160_000.00m, netPayment: 18_360_000.00m);

    /// <summary>A resolved chain of <paramref name="count"/> generic steps for <see cref="PaymentCertificate.Submit"/> -
    /// the exact <c>RequiredRole</c> is irrelevant to every test in this class, since
    /// <see cref="PaymentCertificate.Approve"/>/<see cref="PaymentCertificate.Reject"/> take
    /// <c>expectedStepRole</c> as an explicit caller-supplied argument (defense-in-depth, not
    /// resolved internally from <see cref="PaymentCertificate.ApprovalSteps"/> - that resolution is
    /// the Application-layer command handler's job, covered by its own tests). Only the resulting
    /// <see cref="PaymentCertificate.TotalSteps"/> (== <paramref name="count"/>) matters here.</summary>
    private static IReadOnlyList<PaymentCertificateApprovalStepInput> Steps(int count, UserRole role = UserRole.QS) =>
        Enumerable.Range(1, count).Select(stepNo => new PaymentCertificateApprovalStepInput(stepNo, role, QuorumCount: 1)).ToList();

    // ---- Construction ----

    [Fact]
    public void Constructor_Assigns_All_Fields_And_Starts_At_Draft_With_RevisionNo_One()
    {
        var certificate = CreateDraft(previousCumulativeApprovePct: 40.00m);

        Assert.Equal(TenantId, certificate.TenantId);
        Assert.Equal(ProjectId, certificate.ProjectId);
        Assert.Equal(1, certificate.MilestoneNo);
        Assert.Equal(21_600_000.00m, certificate.MilestoneValue);
        Assert.Equal(40.00m, certificate.PreviousCumulativeApprovePct);
        Assert.Equal(CreatorId, certificate.CreatedByUserId);
        Assert.Equal(PaymentCertificateStatus.Draft, certificate.Status);
        Assert.Equal(1, certificate.RevisionNo);
        Assert.Equal(0, certificate.CurrentStepNo);
        Assert.Equal(0, certificate.TotalSteps);
        Assert.Empty(certificate.RowVersion);
    }

    [Fact]
    public void Constructor_Can_Start_At_NotDue()
    {
        var certificate = new PaymentCertificate(
            TenantId, ProjectId, 2, "Future period", 10_000_000.00m, 0m, CreatorId, PaymentCertificateStatus.NotDue);

        Assert.Equal(PaymentCertificateStatus.NotDue, certificate.Status);
    }

    [Theory]
    [InlineData(PaymentCertificateStatus.PendingApproval)]
    [InlineData(PaymentCertificateStatus.Certified)]
    [InlineData(PaymentCertificateStatus.Paid)]
    [InlineData(PaymentCertificateStatus.Rejected)]
    [InlineData(PaymentCertificateStatus.Cancelled)]
    public void Constructor_Rejects_Any_Initial_Status_Other_Than_NotDue_Or_Draft(PaymentCertificateStatus status)
    {
        Assert.Throws<DomainException>(() =>
            new PaymentCertificate(TenantId, ProjectId, 1, "x", 1_000_000m, 0m, CreatorId, status));
    }

    [Fact]
    public void Constructor_Rejects_Empty_ProjectId_And_Empty_CreatedByUserId()
    {
        Assert.Throws<DomainException>(() =>
            new PaymentCertificate(TenantId, Guid.Empty, 1, "x", 1_000_000m, 0m, CreatorId));
        Assert.Throws<DomainException>(() =>
            new PaymentCertificate(TenantId, ProjectId, 1, "x", 1_000_000m, 0m, Guid.Empty));
    }

    // ---- NotDue -> Draft ----

    [Fact]
    public void MarkPeriodReached_Moves_NotDue_To_Draft()
    {
        var certificate = new PaymentCertificate(
            TenantId, ProjectId, 2, "Future period", 10_000_000.00m, 0m, CreatorId, PaymentCertificateStatus.NotDue);

        certificate.MarkPeriodReached();

        Assert.Equal(PaymentCertificateStatus.Draft, certificate.Status);
    }

    [Fact]
    public void MarkPeriodReached_Throws_When_Not_NotDue()
    {
        var certificate = CreateDraft();

        Assert.Throws<DomainException>(certificate.MarkPeriodReached);
    }

    // ---- Money-field freeze ----

    [Fact]
    public void SetPeriodClaim_Works_While_Draft_And_Rejects_A_Regressing_ApprovePct()
    {
        var certificate = CreateDraft(previousCumulativeApprovePct: 40.00m);

        ClaimFullPeriod(certificate, approvePct: 65.00m);
        Assert.Equal(65.00m, certificate.ApprovePct);

        Assert.Throws<DomainException>(() => certificate.SetPeriodClaim(
            30.00m, null, null, 0m, 0m, 0m, 0m)); // 30 < PreviousCumulativeApprovePct (40)
    }

    [Fact]
    public void SetPeriodClaim_Throws_Once_The_Certificate_Leaves_Draft_Money_Fields_Are_Frozen()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => ClaimFullPeriod(certificate));
    }

    [Fact]
    public void SetPeriodClaim_Throws_Once_Certified_Immutable_Forever()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        var submitterId = Guid.NewGuid();
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, submitterId, DateTimeOffset.UtcNow);
        certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, DateTimeOffset.UtcNow);
        Assert.Equal(PaymentCertificateStatus.Certified, certificate.Status);

        Assert.Throws<DomainException>(() => ClaimFullPeriod(certificate));

        // Still frozen after Paid too.
        certificate.RecordPayment("PAY-REF-001", DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => ClaimFullPeriod(certificate));
    }

    // ---- Submit ----

    [Fact]
    public void Submit_Moves_Draft_To_PendingApproval_Step_One_And_Pins_The_Policy_Version()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        var policyId = Guid.NewGuid();
        var submitterId = Guid.NewGuid();
        var submittedAt = DateTimeOffset.Parse("2026-08-09T10:00:00+07:00");

        certificate.Submit(Steps(3), policyId, 2, false, submitterId, submittedAt);

        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status);
        Assert.Equal(1, certificate.CurrentStepNo);
        Assert.Equal(3, certificate.TotalSteps);
        Assert.Equal(policyId, certificate.ApprovalPolicyId);
        Assert.Equal(2, certificate.ApprovalPolicyVersion);
        Assert.Equal(submitterId, certificate.SubmittedByUserId);
        Assert.Equal(submittedAt, certificate.SubmittedAt);
    }

    [Fact]
    public void Submit_Throws_When_Not_Draft()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Submit_Rejects_An_Empty_Chain_ApprovalPolicyGap()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);

        Assert.Throws<DomainException>(() => certificate.Submit(Steps(0), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    // ---- Approve ----

    [Fact]
    public void Approve_On_A_Single_Step_Chain_Goes_Straight_To_Certified()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var certifiedAt = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

        certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, certifiedAt);

        Assert.Equal(PaymentCertificateStatus.Certified, certificate.Status);
        Assert.Equal(certifiedAt, certificate.CertifiedAt);
    }

    [Fact]
    public void Approve_On_A_Multi_Step_Chain_Advances_StepNo_Without_Leaving_PendingApproval()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(3), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);

        certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, DateTimeOffset.UtcNow);

        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status);
        Assert.Equal(2, certificate.CurrentStepNo);

        certificate.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, DateTimeOffset.UtcNow);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status);
        Assert.Equal(3, certificate.CurrentStepNo);

        certificate.Approve(Guid.NewGuid(), UserRole.ProjectDirector, UserRole.ProjectDirector, allowSelfApproval: false, DateTimeOffset.UtcNow);
        Assert.Equal(PaymentCertificateStatus.Certified, certificate.Status);
    }

    [Fact]
    public void Approve_Blocks_The_Creator_From_Self_Approving_By_Default()
    {
        var certificate = CreateDraft(createdBy: CreatorId);
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() =>
            certificate.Approve(CreatorId, UserRole.QS, UserRole.QS, allowSelfApproval: false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Approve_Blocks_The_Submitter_From_Self_Approving_By_Default()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        var submitterId = Guid.NewGuid();
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, submitterId, DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() =>
            certificate.Approve(submitterId, UserRole.QS, UserRole.QS, allowSelfApproval: false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Approve_Allows_Self_Approval_When_The_Policy_Opts_In()
    {
        var certificate = CreateDraft(createdBy: CreatorId);
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);

        certificate.Approve(CreatorId, UserRole.QS, UserRole.QS, allowSelfApproval: true, DateTimeOffset.UtcNow);

        Assert.Equal(PaymentCertificateStatus.Certified, certificate.Status);
    }

    [Fact]
    public void Approve_Rejects_An_Actor_Whose_Role_Does_Not_Match_The_Current_Steps_Required_Role()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() =>
            certificate.Approve(Guid.NewGuid(), UserRole.Site, UserRole.QS, allowSelfApproval: false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Approve_Throws_When_Not_PendingApproval()
    {
        var certificate = CreateDraft();

        Assert.Throws<DomainException>(() =>
            certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, DateTimeOffset.UtcNow));
    }

    // ---- ReturnForRevision ----

    [Fact]
    public void ReturnForRevision_Bumps_RevisionNo_Voids_The_Chain_And_Unfreezes_Money_Fields()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(2), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);
        certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, DateTimeOffset.UtcNow);
        Assert.Equal(2, certificate.CurrentStepNo);

        certificate.ReturnForRevision();

        Assert.Equal(PaymentCertificateStatus.Draft, certificate.Status);
        Assert.Equal(2, certificate.RevisionNo);
        Assert.Equal(0, certificate.CurrentStepNo);
        Assert.Equal(0, certificate.TotalSteps);
        Assert.Null(certificate.ApprovalPolicyId);
        Assert.Null(certificate.SubmittedByUserId);

        // Money fields are editable again (no partial carry-over, approval-workflow.md §6.3).
        ClaimFullPeriod(certificate, approvePct: 80.00m);
        Assert.Equal(80.00m, certificate.ApprovePct);
    }

    [Fact]
    public void ReturnForRevision_Throws_When_Not_PendingApproval()
    {
        var certificate = CreateDraft();

        Assert.Throws<DomainException>(certificate.ReturnForRevision);
    }

    // ---- Reject ----

    [Fact]
    public void Reject_From_The_Final_Step_Is_Terminal()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);

        certificate.Reject(UserRole.QS, UserRole.QS);

        Assert.Equal(PaymentCertificateStatus.Rejected, certificate.Status);
    }

    [Fact]
    public void Reject_Is_Refused_From_An_Intermediate_Step_Must_ReturnForRevision_Instead()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(2), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => certificate.Reject(UserRole.QS, UserRole.QS));
    }

    [Fact]
    public void Reject_Rejects_An_Actor_Whose_Role_Does_Not_Match_The_Final_Steps_Required_Role()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => certificate.Reject(UserRole.Site, UserRole.QS));
    }

    // ---- Withdraw ----

    [Fact]
    public void Withdraw_By_The_Submitter_Before_Any_Approval_Returns_To_Draft()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        var submitterId = Guid.NewGuid();
        certificate.Submit(Steps(2), Guid.NewGuid(), 1, false, submitterId, DateTimeOffset.UtcNow);

        certificate.Withdraw(submitterId);

        Assert.Equal(PaymentCertificateStatus.Draft, certificate.Status);
        Assert.Equal(1, certificate.RevisionNo); // Withdraw does not bump RevisionNo.
        Assert.Equal(0, certificate.TotalSteps);
    }

    [Fact]
    public void Withdraw_Is_Refused_For_Anyone_Other_Than_The_Submitter()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => certificate.Withdraw(Guid.NewGuid()));
    }

    [Fact]
    public void Withdraw_Is_Refused_Once_At_Least_One_Step_Has_Cleared()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        var submitterId = Guid.NewGuid();
        certificate.Submit(Steps(2), Guid.NewGuid(), 1, false, submitterId, DateTimeOffset.UtcNow);
        certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => certificate.Withdraw(submitterId));
    }

    // ---- Cancel ----

    [Fact]
    public void Cancel_By_The_Creator_From_Draft_Is_Terminal()
    {
        var certificate = CreateDraft(createdBy: CreatorId);

        certificate.Cancel(CreatorId, UserRole.QS);

        Assert.Equal(PaymentCertificateStatus.Cancelled, certificate.Status);
    }

    [Fact]
    public void Cancel_By_Any_PM_Is_Allowed_Even_If_Not_The_Creator()
    {
        var certificate = CreateDraft(createdBy: CreatorId);

        certificate.Cancel(Guid.NewGuid(), UserRole.PM);

        Assert.Equal(PaymentCertificateStatus.Cancelled, certificate.Status);
    }

    [Fact]
    public void Cancel_Is_Refused_For_A_Non_Creator_Non_PM_Actor()
    {
        var certificate = CreateDraft(createdBy: CreatorId);

        Assert.Throws<DomainException>(() => certificate.Cancel(Guid.NewGuid(), UserRole.Site));
    }

    [Fact]
    public void Cancel_Throws_When_Not_Draft()
    {
        var certificate = CreateDraft(createdBy: CreatorId);
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => certificate.Cancel(CreatorId, UserRole.QS));
    }

    // ---- RecordPayment ----

    [Fact]
    public void RecordPayment_From_Certified_Moves_To_Paid()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);
        certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, DateTimeOffset.UtcNow);
        var paidAt = DateTimeOffset.Parse("2026-08-15T00:00:00+07:00");

        certificate.RecordPayment("CHQ-000123", paidAt);

        Assert.Equal(PaymentCertificateStatus.Paid, certificate.Status);
        Assert.Equal("CHQ-000123", certificate.PaymentReference);
        Assert.Equal(paidAt, certificate.PaidAt);
    }

    [Fact]
    public void RecordPayment_Requires_A_Non_Blank_Reference()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);
        certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => certificate.RecordPayment("  ", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RecordPayment_Throws_When_Not_Certified()
    {
        var certificate = CreateDraft();

        Assert.Throws<DomainException>(() => certificate.RecordPayment("REF", DateTimeOffset.UtcNow));
    }

    // ---- CreateCertificationLedgerEntries (S9-BE-04) ----

    [Fact]
    public void CreateCertificationLedgerEntries_Throws_Unless_Certified()
    {
        var certificate = CreateDraft();

        Assert.Throws<DomainException>(() => certificate.CreateCertificationLedgerEntries());
    }

    [Fact]
    public void CreateCertificationLedgerEntries_Posts_Retention_Accrual_And_Advance_Recovery()
    {
        var certificate = CreateDraft();
        ClaimFullPeriod(certificate); // retention 1,080,000.00; advance recovery 2,160,000.00
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var certifiedAt = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");
        certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, certifiedAt);

        var entries = certificate.CreateCertificationLedgerEntries();

        Assert.Equal(2, entries.Count);
        var retention = Assert.Single(entries, e => e.Category == FinanceLedgerCategory.Retention);
        Assert.Equal(FinanceLedgerEntryType.Accrual, retention.EntryType);
        Assert.Equal(1_080_000.00m, retention.Amount);
        Assert.Equal(certificate.Id, retention.PaymentCertificateId);
        Assert.Equal(certifiedAt, retention.EffectiveDate);

        var advance = Assert.Single(entries, e => e.Category == FinanceLedgerCategory.Advance);
        Assert.Equal(FinanceLedgerEntryType.Recovery, advance.EntryType);
        Assert.Equal(2_160_000.00m, advance.Amount);
        Assert.Equal(certificate.Id, advance.PaymentCertificateId);
    }

    [Fact]
    public void CreateCertificationLedgerEntries_Posts_Nothing_For_A_Zero_Amount_Line_Fixture_P3b_Style()
    {
        var certificate = CreateDraft(previousCumulativeApprovePct: 100m);
        // P3b-style period: retention fully capped out (0.00) but advance recovery still non-zero.
        certificate.SetPeriodClaim(
            100m, null, null,
            grossCertifiedAmount: 5_000_000.00m, retentionAmount: 0.00m,
            advanceRecoveryAmount: 500_000.00m, netPayment: 4_500_000.00m);
        certificate.Submit(Steps(1), Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);
        certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, DateTimeOffset.UtcNow);

        var entries = certificate.CreateCertificationLedgerEntries();

        var onlyEntry = Assert.Single(entries);
        Assert.Equal(FinanceLedgerCategory.Advance, onlyEntry.Category);
        Assert.Equal(500_000.00m, onlyEntry.Amount);
    }
}
