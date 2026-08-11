using CMPlus.Application.Approval;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Integration.Tests.Vo;

/// <summary>
/// S15-BE-01, at the real-persistence level (EF Core InMemory, real repositories, real interceptors -
/// per the Docker outage): proves the simulator (a) agrees with a real
/// <c>SubmitVariationOrderCommandHandler</c> round trip through two INDEPENDENT
/// <see cref="Infrastructure.Persistence.CmPlusDbContext"/> instances against the same InMemory
/// database (not two calls sharing one in-memory fixture, which
/// <c>CMPlus.Application.Tests.Features.Approval.SimulateApprovalRoutingQueryHandlerTests</c> already
/// covers), and (b) genuinely writes nothing - table row counts are identical before and after.
/// </summary>
public class ApprovalRoutingSimulatorIntegrationTests
{
    private static readonly DateTimeOffset EffectiveFrom = DateTimeOffset.Parse("2025-01-01T00:00:00+07:00");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-11T09:00:00+07:00");

    [Fact]
    public async Task Simulate_Resolves_The_Same_Chain_A_Real_Submit_Would_Through_Independent_DbContexts()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 485_000_000.00m);

        // Simulate FIRST, deliberately - if it wrote anything, the real Submit below (on a fresh
        // Draft VO, in a fresh DbContext) would be corrupted by it.
        var simulation = await harness.SimulateAsync(ApprovalDocumentType.VariationOrder, projectId, 2_400_000.00m);
        Assert.True(simulation.IsSuccess, simulation.IsFailure ? simulation.Error : string.Empty);

        var creatorId = Guid.NewGuid();
        var voId = await harness.CreateDraftAsync(projectId, 2_400_000.00m, creatorId);
        var submitResult = await harness.SubmitAsync(voId, creatorId, UserRole.QS);
        Assert.True(submitResult.IsSuccess, submitResult.IsFailure ? submitResult.Error : string.Empty);

        var persisted = await harness.LoadAsync(voId);

        Assert.Equal(persisted.TotalSteps, simulation.Value.Steps.Count);
        Assert.Equal(
            persisted.ApprovalSteps.OrderBy(s => s.StepNo).Select(s => (s.StepNo, s.RequiredRole, s.QuorumCount)),
            simulation.Value.Steps.OrderBy(s => s.StepNo).Select(s => (s.StepNo, s.RequiredRole, s.QuorumCount)));
        Assert.Equal(persisted.ApprovalPolicyId, simulation.Value.ApprovalPolicyId);
        Assert.Equal(persisted.ApprovalPolicyVersion, simulation.Value.ApprovalPolicyVersion);
    }

    [Fact]
    public async Task Simulate_Leaves_Every_Row_Count_Unchanged()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 485_000_000.00m);

        var before = await harness.CountAllRowsAsync();

        var simulation = await harness.SimulateAsync(ApprovalDocumentType.VariationOrder, projectId, 2_400_000.00m);
        Assert.True(simulation.IsSuccess, simulation.IsFailure ? simulation.Error : string.Empty);

        // Also exercise the failure (not-found project) path - a failed simulation must be exactly
        // as inert as a successful one.
        var notFoundAttempt = await harness.SimulateAsync(ApprovalDocumentType.VariationOrder, Guid.NewGuid(), 300_000.00m);
        Assert.True(notFoundAttempt.IsFailure);

        var after = await harness.CountAllRowsAsync();

        Assert.Equal(before, after);
    }

    /// <summary>
    /// ADR-0021 at the real persistence layer: EF Core InMemory does not enforce the filtered unique
    /// index at all (matching real SQL Server's own proven gap for the <c>ProjectId IS NULL</c> group -
    /// ADR-0021's own point is that the constraint never fires there even on a real, constraint-
    /// enforcing engine), so two simultaneously-active tenant-wide policies for the same document type
    /// persist without error - reproducing the corruption directly, not merely asserting it in a fake.
    /// </summary>
    [Fact]
    public async Task Simulate_Detects_Two_Simultaneously_Active_Tenant_Wide_Policies_Persisted_Directly()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        var projectId = await harness.SeedProjectAsync(bac: 485_000_000.00m);

        await using (var context = harness.CreateContext())
        {
            var policyA = ApprovalPolicy.CreateInitialVersion(
                harness.TenantId, projectId: null, ApprovalDocumentType.VariationOrder, EffectiveFrom,
                [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.PM)]);
            var policyB = ApprovalPolicy.CreateInitialVersion(
                harness.TenantId, projectId: null, ApprovalDocumentType.VariationOrder, EffectiveFrom,
                [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.ProjectDirector)]);
            context.ApprovalPolicies.AddRange(policyA, policyB);
            await context.SaveChangesAsync(); // both succeed - ADR-0021, reproduced directly
        }

        var simulation = await harness.SimulateAsync(ApprovalDocumentType.VariationOrder, projectId, 300_000.00m);

        Assert.True(simulation.IsSuccess, simulation.IsFailure ? simulation.Error : string.Empty);
        Assert.True(simulation.Value.MultipleActivePoliciesDetected);
        Assert.Equal(2, simulation.Value.AmbiguousActivePolicies.Count);
    }
}
