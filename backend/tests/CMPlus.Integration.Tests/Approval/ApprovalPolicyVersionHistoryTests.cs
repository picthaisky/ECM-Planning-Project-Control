using CMPlus.Domain.Enums;

namespace CMPlus.Integration.Tests.Approval;

/// <summary>
/// S15-BE-01: "ประวัติเวอร์ชันดูจาก AuditLog + ApprovalPolicy.Version โดยไม่เพิ่มที่เก็บใหม่" - proven
/// against a real <c>CmPlusDbContext</c> (EF Core InMemory, per the Docker outage) wired with the
/// real <c>AuditSaveChangesInterceptor</c>, so the "who/when" fields genuinely come from audit rows a
/// real mutation produced, not a hand-authored fixture. No new table/entity is introduced anywhere in
/// this feature - the reader composes <c>ApprovalPolicy</c> (already existing, never-deleted rows)
/// with <c>AuditLog</c> (already written for every Create/Update).
/// </summary>
public class ApprovalPolicyVersionHistoryTests
{
    private static readonly DateTimeOffset EffectiveFrom = DateTimeOffset.Parse("2025-01-01T00:00:00+07:00");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-11T09:00:00+07:00");

    [Fact]
    public async Task An_Unconfigured_Document_Type_Returns_An_Empty_History_Not_A_Failure()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);

        var result = await harness.GetVersionHistoryAsync(ApprovalDocumentType.VariationOrder);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    /// <summary>
    /// The load-bearing case: seed v1 (via the production seeder, itself a real mutation), then edit
    /// it via the real <c>UpdateApprovalPolicyCommandHandler</c> (creates v2, deactivates v1) as a
    /// specific, known Admin actor. Asserts v1 shows as superseded with a real deactivation
    /// timestamp/actor and v2 shows as the live version with a real creation timestamp/actor -
    /// entirely reconstructed from <c>ApprovalPolicy</c> + <c>AuditLog</c>.
    /// </summary>
    [Fact]
    public async Task Editing_A_Policy_Produces_A_Two_Entry_History_With_Real_Audit_Provenance()
    {
        var harness = new ApprovalWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);

        var adminId = Guid.NewGuid();
        harness.ActAs(adminId, UserRole.Admin);
        var updateResult = await harness.UpdatePolicyAsync(
            ApprovalDocumentType.VariationOrder,
            allowSelfApproval: true,
            cumulativeVoEscalationPct: 15.00m,
            cumulativeVoEscalationRole: UserRole.Executive,
            [new(1, 0.00m, null, UserRole.PM)]);
        Assert.True(updateResult.IsSuccess, updateResult.IsFailure ? updateResult.Error : string.Empty);

        var history = await harness.GetVersionHistoryAsync(ApprovalDocumentType.VariationOrder);
        Assert.True(history.IsSuccess);
        Assert.Equal(2, history.Value.Count);

        var v1 = history.Value.Single(v => v.Version == 1);
        var v2 = history.Value.Single(v => v.Version == 2);

        // v1: superseded - still readable (never deleted), IsActive now false, and a real
        // deactivation ("last modified") timestamp/actor recorded purely from the AuditLog Updated
        // row the edit produced.
        Assert.False(v1.IsActive);
        Assert.NotNull(v1.LastModifiedAt);
        Assert.Equal(adminId, v1.LastModifiedByUserId);
        Assert.NotNull(v1.EffectiveTo);

        // v2: live, created by the same real Admin actor, at the same real clock time - both facts
        // exist ONLY in AuditLog, never on ApprovalPolicy itself.
        Assert.True(v2.IsActive);
        Assert.True(v2.AllowSelfApproval);
        Assert.Equal(15.00m, v2.CumulativeVoEscalationPct);
        Assert.Equal(adminId, v2.CreatedByUserId);
        Assert.Equal(Now, v2.CreatedAt);
        Assert.Null(v2.LastModifiedAt); // never itself edited yet
        Assert.Equal(1, v2.RuleCount); // the single rule the edit above supplied
    }

    [Fact]
    public async Task History_Is_Scoped_Per_Tenant_A_Second_Tenants_Edits_Never_Appear()
    {
        var harnessA = new ApprovalWorkflowHarness(now: Now);
        await harnessA.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        harnessA.ActAs(Guid.NewGuid(), UserRole.Admin);
        Assert.True((await harnessA.UpdatePolicyAsync(
            ApprovalDocumentType.VariationOrder, false, null, null, [new(1, 0.00m, null, UserRole.PM)])).IsSuccess);

        var harnessB = new ApprovalWorkflowHarness(now: Now); // different TenantId, different InMemory database
        var historyB = await harnessB.GetVersionHistoryAsync(ApprovalDocumentType.VariationOrder);

        Assert.True(historyB.IsSuccess);
        Assert.Empty(historyB.Value); // tenant B has no policy at all - never tenant A's
    }
}
