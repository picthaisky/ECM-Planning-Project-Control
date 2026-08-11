using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Features.Projects.Commands.SetEacAdvancedInputs;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Integration.Tests.Vo;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Evm;

/// <summary>
/// S14-BE-03: proves <see cref="SetEacAdvancedInputsCommandHandler"/> (the missing write path this
/// sprint adds) is actually reachable by the five-variant EAC engine Sprint 7 already shipped -
/// "wire the inputs, don't rebuild it" (this task's brief). Runs through the <b>real</b>
/// <see cref="ProjectRepository"/>/<see cref="EvmDataReader"/>/<see cref="EvmComputationService"/> -
/// not the Application-layer hand-written fakes <c>SetEacAdvancedInputsCommandHandlerTests</c> uses -
/// against Fixture A from evm-formulas.md (BAC 1,000,000.00; PV 400,000.00; EV 300,000.00; AC
/// 350,000.00), asserting fixtures A4 (<see cref="EacVariant.BottomUpEtc"/>) and A5
/// (<see cref="EacVariant.CustomPf"/>) exactly, per this task's explicit instruction ("Fixtures
/// A4/A5 in the EVM knowledge are the expected values").
/// </summary>
public class EacAdvancedInputsWiringTests
{
    private static readonly DateTimeOffset DataDate = DateTimeOffset.Parse("2026-08-11T09:00:00+07:00");

    private sealed class FixedActualCostReader(decimal amount) : IActualCostReader
    {
        public Task<ActualCostResult> GetActualCostAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActualCostResult(amount, 1));
    }

    [Fact]
    public async Task Setting_EacManualEtc_And_EacCustomPerformanceFactor_Through_The_Real_Command_Produces_Fixtures_A4_And_A5()
    {
        var harness = new VariationOrderWorkflowHarness(now: DataDate);
        var projectId = await harness.SeedProjectAsync(bac: 1_000_000.00m);

        // Fixture A (evm-formulas.md): BudgetCost 1,000,000.00, planned window sized so
        // plannedPct(DataDate) is exactly 40% via integer tick math (no floating-point rounding,
        // same technique RebaselineTests already establishes) -> PV = 400,000.00. Progress 30% ->
        // EV = 300,000.00.
        using (var context = harness.CreateContext())
        {
            var node = new WBSNode(harness.TenantId, projectId, "C-1", "Structure", 100m);
            context.WBSNodes.Add(node);

            var planStart = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var elapsedTicks = (DataDate - planStart).Ticks;
            var totalTicks = elapsedTicks * 100 / 40;
            var planFinish = planStart + TimeSpan.FromTicks(totalTicks);

            var activity = new Activity(harness.TenantId, node.Id, "A-1", "Foundation", planStart, planFinish, 300, 1_000_000.00m);
            context.Activities.Add(activity);

            var progress = activity.RecordProgress(DataDate, 30m, null, Guid.NewGuid(), ProgressSource.Manual, DataDate);
            context.ActivityProgressLogs.Add(progress);

            await context.SaveChangesAsync();
        }

        // The command under test - SetEacAdvancedInputsCommandHandler against the real
        // ProjectRepository, not a hand-written fake. Both inputs configured together in one
        // full-representation PUT.
        harness.ActAs(Guid.NewGuid(), UserRole.PM);
        using (var context = harness.CreateContext())
        {
            var handler = new SetEacAdvancedInputsCommandHandler(new ProjectRepository(context));
            var result = await handler.Handle(
                new SetEacAdvancedInputsCommand(projectId, EacManualEtc: 760_000.00m, EacCustomPerformanceFactor: 1.20m),
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        }

        // Read back through the real, unmodified Sprint 7 pipeline - EvmDataReader -> EvmEngine ->
        // EacCalculator - never a second/duplicated calculation path.
        using (var context = harness.CreateContext())
        {
            var evmComputationService = new EvmComputationService(new EvmDataReader(context), new FixedActualCostReader(350_000.00m));
            var evm = await evmComputationService.ComputeAsync(projectId, DataDate, CancellationToken.None);

            Assert.NotNull(evm);
            Assert.Equal(1_000_000.00m, evm!.Core.Bac);
            Assert.Equal(400_000.00m, evm.Core.Pv);
            Assert.Equal(300_000.00m, evm.Core.Ev);
            Assert.Equal(350_000.00m, evm.Core.Ac);

            // Fixture A4: BottomUpEtc (ETC_manual = 760,000.00) -> ETC 760,000.00, EAC 1,110,000.00,
            // VAC -110,000.00.
            var a4 = evm.EacResult.GetVariant(EacVariant.BottomUpEtc);
            Assert.True(a4.Computable);
            Assert.Equal(760_000.00m, a4.Etc);
            Assert.Equal(1_110_000.00m, a4.Eac);
            Assert.Equal(-110_000.00m, a4.Vac);

            // Fixture A5: CustomPf (PF_c = 1.20) -> ETC 840,000.00, EAC 1,190,000.00, VAC -190,000.00.
            var a5 = evm.EacResult.GetVariant(EacVariant.CustomPf);
            Assert.True(a5.Computable);
            Assert.Equal(1.20m, a5.PerformanceFactor);
            Assert.Equal(840_000.00m, a5.Etc);
            Assert.Equal(1_190_000.00m, a5.Eac);
            Assert.Equal(-190_000.00m, a5.Vac);
        }
    }

    /// <summary>The staleness interaction this task calls out explicitly, proven this time through
    /// the real ApplyVariationOrderApproval-shaped write plus the real command, rather than a plain
    /// Domain-entity call as <c>ProjectTests</c> already covers.</summary>
    [Fact]
    public async Task Setting_A_Fresh_EacManualEtc_Through_The_Real_Command_Clears_A_Vo_Induced_Staleness_Flag()
    {
        var harness = new VariationOrderWorkflowHarness(now: DataDate);
        var projectId = await harness.SeedProjectAsync(bac: 1_000_000.00m);

        var approvedAt = DataDate.AddDays(-1);
        using (var context = harness.CreateContext())
        {
            var project = await context.Projects.SingleAsync(p => p.Id == projectId);
            project.SetEacManualEtc(700_000.00m);
            project.ApplyVariationOrderApproval(50_000.00m, approvedAt); // simulates the VO effect directly - stamps StaleSince.
            await context.SaveChangesAsync();
        }

        using (var verifyContext = harness.CreateContext())
        {
            var project = await verifyContext.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId);
            Assert.NotNull(project.EacManualEtcStaleSince); // sanity: genuinely stale before the fix.
        }

        harness.ActAs(Guid.NewGuid(), UserRole.QS);
        using (var context = harness.CreateContext())
        {
            var handler = new SetEacAdvancedInputsCommandHandler(new ProjectRepository(context));
            var result = await handler.Handle(
                new SetEacAdvancedInputsCommand(projectId, EacManualEtc: 760_000.00m, EacCustomPerformanceFactor: null),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Null(result.Value.EacManualEtcStaleSince);
        }

        using (var verifyContext = harness.CreateContext())
        {
            var project = await verifyContext.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId);
            Assert.Null(project.EacManualEtcStaleSince);
            Assert.Equal(760_000.00m, project.EacManualEtc);
        }
    }
}
