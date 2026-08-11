using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Vo;

/// <summary>
/// S10-BE-03, against real EF Core persistence (not fakes): the BAC invariant (domain-rules.md §5.2)
/// and the zero-<see cref="ProjectFinanceLedger"/>-rows rule (§6.4), both counted from a genuinely
/// fresh <see cref="Infrastructure.Persistence.CmPlusDbContext"/> read after the real
/// <c>ApproveVariationOrderCommandHandler</c> has run and committed.
/// </summary>
public class VariationOrderMoneyEffectsIntegrationTests
{
    private static readonly DateTimeOffset EffectiveFrom = DateTimeOffset.Parse("2025-01-01T00:00:00+07:00");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

    [Fact]
    public async Task Approving_A_Vo_Moves_Every_Scope_Activitys_BudgetCost_By_Its_Own_Delta_Summing_To_Exactly_The_Bac_Delta()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 100_000_000.00m);
        var activityAId = await harness.SeedActivityAsync(projectId, budgetCost: 1_000_000.00m);
        var activityBId = await harness.SeedActivityAsync(projectId, budgetCost: 2_000_000.00m);
        var creatorId = Guid.NewGuid();

        // Net Amount = 500,000.00: +800,000.00 on A, -300,000.00 on B. 500,000.00 sits in TH-Default-VO's
        // [500k, 5M) band, which needs BOTH PM and ProjectDirector (R6's own boundary fixture) - only
        // the second (final) vote actually moves any money.
        var voId = await harness.CreateDraftAsync(
            projectId, 500_000.00m, creatorId,
            [new VariationOrderScopeItemInput(activityAId, 800_000.00m), new VariationOrderScopeItemInput(activityBId, -300_000.00m)]);
        var submitResult = await harness.SubmitAsync(voId, creatorId, UserRole.QS);
        Assert.True(submitResult.IsSuccess);
        Assert.Equal(2, submitResult.Value.TotalSteps);

        Assert.True((await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.PM)).IsSuccess);
        var approveResult = await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.ProjectDirector);
        Assert.True(approveResult.IsSuccess, approveResult.IsFailure ? approveResult.Error : string.Empty);
        Assert.Equal(VariationOrderStatus.Approved, approveResult.Value.Status);

        var activityA = await harness.LoadActivityAsync(activityAId);
        var activityB = await harness.LoadActivityAsync(activityBId);
        var project = await harness.LoadProjectAsync(projectId);

        Assert.Equal(1_800_000.00m, activityA.BudgetCost);
        Assert.Equal(1_700_000.00m, activityB.BudgetCost);
        Assert.Equal(100_500_000.00m, project.BAC);

        var totalActivityDelta = (activityA.BudgetCost - 1_000_000.00m) + (activityB.BudgetCost - 2_000_000.00m);
        var bacDelta = project.BAC - 100_000_000.00m;
        Assert.Equal(totalActivityDelta, bacDelta); // the invariant itself, over real persisted rows
    }

    [Fact]
    public async Task Approving_A_Vo_Writes_Zero_ProjectFinanceLedger_Rows_Even_With_An_InFlight_Certificate()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 100_000_000.00m);
        var activityId = await harness.SeedActivityAsync(projectId, budgetCost: 5_000_000.00m);
        var creatorId = Guid.NewGuid();

        // Seed an in-flight (PendingApproval) certificate for the same project (domain-rules.md §6.2).
        using (var context = harness.CreateContext())
        {
            var certificate = new PaymentCertificate(harness.TenantId, projectId, 1, "Period 1", 5_000_000.00m, 0m, creatorId);
            certificate.SetPeriodClaim(50m, null, null, 5_000_000.00m, 250_000.00m, 500_000.00m, 4_250_000.00m);
            certificate.Submit([new PaymentCertificateApprovalStepInput(1, UserRole.QS, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
            context.PaymentCertificates.Add(certificate);
            await context.SaveChangesAsync();
        }

        var voId = await harness.CreateDraftAsync(
            projectId, 1_000_000.00m, creatorId, [new VariationOrderScopeItemInput(activityId, 1_000_000.00m)]);
        Assert.True((await harness.SubmitAsync(voId, creatorId, UserRole.QS)).IsSuccess);

        var before = await harness.CountProjectFinanceLedgerRowsAsync(projectId);
        Assert.True((await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.PM)).IsSuccess); // 1 of 2 (band)
        var approveResult = await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.ProjectDirector); // final
        Assert.True(approveResult.IsSuccess, approveResult.IsFailure ? approveResult.Error : string.Empty);
        Assert.Equal(VariationOrderStatus.Approved, approveResult.Value.Status);
        var after = await harness.CountProjectFinanceLedgerRowsAsync(projectId);

        Assert.Equal(0, before);
        Assert.Equal(0, after); // the absolute rule, domain-rules.md §6.4 - not conditionally, zero

        // And the mandatory disclosure DID fire (not silent) - a real AuditLog row, from a real read.
        var certificateId = await GetOnlyCertificateIdAsync(harness, projectId);
        var disclosures = await harness.LoadAuditLogsAsync("VoApprovedWithCertificateInFlight", certificateId);
        Assert.Single(disclosures);
    }

    /// <summary>Deduct-VO variant of the same zero-ledger-rows proof - the direction the "never
    /// auto-refund retention" rule (domain-rules.md §5.3 Limit 1) actually worries about.</summary>
    [Fact]
    public async Task Approving_A_Deduct_Vo_Also_Writes_Zero_ProjectFinanceLedger_Rows_Never_Auto_Refunds_Retention()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 100_000_000.00m);
        var activityId = await harness.SeedActivityAsync(projectId, budgetCost: 5_000_000.00m);
        var creatorId = Guid.NewGuid();

        var voId = await harness.CreateDraftAsync(
            projectId, -1_000_000.00m, creatorId, [new VariationOrderScopeItemInput(activityId, -1_000_000.00m)]);
        Assert.True((await harness.SubmitAsync(voId, creatorId, UserRole.QS)).IsSuccess);

        Assert.True((await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.PM)).IsSuccess); // 1 of 2 (band, R2's shape)
        var approveResult = await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.ProjectDirector); // final
        Assert.True(approveResult.IsSuccess, approveResult.IsFailure ? approveResult.Error : string.Empty);
        Assert.Equal(VariationOrderStatus.Approved, approveResult.Value.Status);

        Assert.Equal(0, await harness.CountProjectFinanceLedgerRowsAsync(projectId));
        var project = await harness.LoadProjectAsync(projectId);
        Assert.Equal(99_000_000.00m, project.BAC); // moved down, signed - and still zero ledger rows
    }

    private static async Task<Guid> GetOnlyCertificateIdAsync(VariationOrderWorkflowHarness harness, Guid projectId)
    {
        using var context = harness.CreateContext();
        var certificate = await context.PaymentCertificates
            .Where(c => c.ProjectId == projectId)
            .SingleAsync();
        return certificate.Id;
    }
}
