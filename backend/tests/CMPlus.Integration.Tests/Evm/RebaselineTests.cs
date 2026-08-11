using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Features.Evm.Commands.CloseEvmPeriod;
using CMPlus.Application.Features.Evm.Queries.GetEvm;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Integration.Tests.Vo;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Evm;

/// <summary>
/// S10-QA-02 (docs/10 §8's DoD: "หลังอนุมัติ VO: BAC/EAC/VAC ใหม่ถูกต้องทุก variant; EvmPeriodSnapshot
/// ก่อนหน้าไม่เปลี่ยนแม้แต่แถวเดียว"). This file did not previously exist despite being the DoD's own
/// named artifact path - <c>VariationOrderMoneyEffectsIntegrationTests</c> proves the BAC/Activity
/// scope-budget invariant but never calls into <see cref="EvmComputationService"/>/<c>EacCalculator</c>
/// at all, so nothing anywhere previously exercised domain-rules.md §5.7's V-9 fixture (every EAC
/// variant across the rebaseline boundary) or combined a real <see cref="EvmPeriodSnapshot"/> close
/// with a real VO approval to prove the snapshot survives untouched.
///
/// <para>Independent QA verification (S10-QA-02): the numbers below are re-derived directly from
/// domain-rules.md §5.7's worked example (not copied from any existing test - none existed), and
/// cross-checked by hand against the closed-form deltas that section states
/// (`dEAC = PF . dBAC`, `dVAC = (1-PF) . dBAC`) before being encoded as assertions.</para>
/// </summary>
public class RebaselineTests
{
    private static readonly DateTimeOffset EffectiveFrom = DateTimeOffset.Parse("2025-01-01T00:00:00+07:00");

    /// <summary>domain-rules.md §5.7's own data date - VO-021 is evaluated "immediately before and
    /// immediately after" approval at this instant.</summary>
    private static readonly DateTimeOffset DataDate = DateTimeOffset.Parse("2026-07-15T00:00:00Z");

    private sealed class FixedActualCostReader(decimal amount) : IActualCostReader
    {
        public Task<ActualCostResult> GetActualCostAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActualCostResult(amount, amount == 0m ? 0 : 1));
    }

    [Fact]
    public async Task V9_Every_Eac_Variant_Moves_Correctly_Across_The_Vo_Rebaseline_Boundary_And_The_Prior_Snapshot_Is_Untouched()
    {
        var harness = new VariationOrderWorkflowHarness(now: DataDate);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 100_000_000.00m);

        Guid earningActivityId;
        Guid dormantActivityId;
        using (var context = harness.CreateContext())
        {
            var node = new WBSNode(harness.TenantId, projectId, "C-1", "Structure", 100m);
            context.WBSNodes.Add(node);

            // Planned window sized so plannedPct(DataDate) is EXACTLY 40% via integer tick math (no
            // floating point rounding) - domain-rules.md §5.7: PV = 40,000,000.00 on a
            // 100,000,000.00 budget.
            var planStart = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var elapsedTicks = (DataDate - planStart).Ticks;
            var totalTicks = elapsedTicks * 100 / 40;
            var planFinish = planStart + TimeSpan.FromTicks(totalTicks);

            var earningActivity = new Activity(
                harness.TenantId, node.Id, "A-1", "Foundation", planStart, planFinish, 300, 100_000_000.00m);
            context.Activities.Add(earningActivity);
            earningActivityId = earningActivity.Id;

            // The VO's scope target. Planned to start AFTER DataDate (2026-08-01, matching V-9's own
            // framing: "the VO's scope is planned to start 2026-08-01") so it contributes exactly 0 to
            // PV/EV at DataDate both before and after its budget moves - isolating the BAC-only effect
            // V-9 exists to test.
            var dormantActivity = new Activity(
                harness.TenantId, node.Id, "A-2", "New scope",
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), DateTimeOffset.Parse("2026-10-01T00:00:00Z"), 60, 0.00m);
            context.Activities.Add(dormantActivity);
            dormantActivityId = dormantActivity.Id;

            var progress = earningActivity.RecordProgress(DataDate, 36m, null, Guid.NewGuid(), ProgressSource.Manual, DataDate);
            context.ActivityProgressLogs.Add(progress);

            var trackedProject = await context.Projects.SingleAsync(p => p.Id == projectId);
            trackedProject.SetEacManualEtc(70_000_000.00m);
            trackedProject.SetEacCustomPerformanceFactor(1.20m);

            await context.SaveChangesAsync();
        }

        var actualCostReader = new FixedActualCostReader(40_000_000.00m);

        // --- Close a snapshot at DataDate BEFORE the VO, to prove immutability across approval. ---
        Guid snapshotId;
        object[] snapshotBefore;
        using (var context = harness.CreateContext())
        {
            var handler = new CloseEvmPeriodCommandHandler(
                new EvmComputationService(new EvmDataReader(context), actualCostReader),
                new EvmSnapshotRepository(context),
                harness.TenantProvider,
                harness.CurrentUser,
                harness.Clock);
            var result = await handler.Handle(new CloseEvmPeriodCommand(projectId, DataDate), CancellationToken.None);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
            snapshotId = result.Value.SnapshotId;
        }

        using (var context = harness.CreateContext())
        {
            var s = await context.EvmPeriodSnapshots.AsNoTracking().SingleAsync(x => x.Id == snapshotId);
            snapshotBefore = [s.Bac, s.Pv, s.Ev, s.Ac, s.EacVariant, s.PerformanceFactor!, s.Eac!, s.Etc!, s.Vac!, s.CreatedAt, s.CreatedByUserId!];
            Assert.Equal(100_000_000.00m, s.Bac);
        }

        // --- Live EVM BEFORE the VO. ---
        EvmComputation before;
        using (var context = harness.CreateContext())
        {
            var service = new EvmComputationService(new EvmDataReader(context), actualCostReader);
            before = (await service.ComputeAsync(projectId, DataDate))!;
        }

        Assert.Equal(100_000_000.00m, before.Core.Bac);
        Assert.Equal(40_000_000.00m, before.Core.Pv);
        Assert.Equal(36_000_000.00m, before.Core.Ev);
        Assert.Equal(40_000_000.00m, before.Core.Ac);
        Assert.Equal(-4_000_000.00m, before.Core.Sv);
        Assert.Equal(-4_000_000.00m, before.Core.Cv);
        Assert.Equal(0.900000m, Math.Round(before.Core.Spi!.Value, 6));
        Assert.Equal(0.900000m, Math.Round(before.Core.Cpi!.Value, 6));

        // --- Approve VO-021 Add +10,000,000.00 (domain-rules.md §5.7's own fixture). 10,000,000.00
        // sits in TH-Default-VO's >=5,000,000 band -> [PM, ProjectDirector, Executive], 3 steps. ---
        var creatorId = Guid.NewGuid();
        var voId = await harness.CreateDraftAsync(
            projectId, 10_000_000.00m, creatorId, [new VariationOrderScopeItemInput(dormantActivityId, 10_000_000.00m)], voNumber: "VO-021");
        var submit = await harness.SubmitAsync(voId, creatorId, UserRole.QS);
        Assert.True(submit.IsSuccess, submit.IsFailure ? submit.Error : string.Empty);
        Assert.Equal(3, submit.Value.TotalSteps);

        Assert.True((await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.PM)).IsSuccess);
        Assert.True((await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.ProjectDirector)).IsSuccess);
        var finalApproval = await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.Executive);
        Assert.True(finalApproval.IsSuccess, finalApproval.IsFailure ? finalApproval.Error : string.Empty);
        Assert.Equal(VariationOrderStatus.Approved, finalApproval.Value.Status);

        // --- Live EVM AFTER the VO, same DataDate. ---
        EvmComputation after;
        using (var context = harness.CreateContext())
        {
            var service = new EvmComputationService(new EvmDataReader(context), actualCostReader);
            after = (await service.ComputeAsync(projectId, DataDate))!;
        }

        // Core metrics: BAC steps; everything else is frozen (domain-rules.md §5.7: "Assert this. SV,
        // CV, SPI, CPI contain no BAC term, so performance measurement is continuous across the
        // rebaseline boundary.").
        Assert.Equal(110_000_000.00m, after.Core.Bac);
        Assert.Equal(before.Core.Pv, after.Core.Pv);
        Assert.Equal(before.Core.Ev, after.Core.Ev);
        Assert.Equal(before.Core.Ac, after.Core.Ac);
        Assert.Equal(before.Core.Sv, after.Core.Sv);
        Assert.Equal(before.Core.Cv, after.Core.Cv);
        Assert.Equal(before.Core.Spi, after.Core.Spi);
        Assert.Equal(before.Core.Cpi, after.Core.Cpi);

        var beforeDto = EvmResponseDto.From(projectId, DataDate, before.Core, before.EacResult, EacVariant.CpiBased);
        var afterDto = EvmResponseDto.From(projectId, DataDate, after.Core, after.EacResult, EacVariant.CpiBased);

        AssertVariant(beforeDto, EacVariant.CpiBased, 1.111111m, 71_111_111.11m, 111_111_111.11m, -11_111_111.11m);
        AssertVariant(afterDto, EacVariant.CpiBased, 1.111111m, 82_222_222.22m, 122_222_222.22m, -12_222_222.22m);

        AssertVariant(beforeDto, EacVariant.Atypical, 1.000000m, 64_000_000.00m, 104_000_000.00m, -4_000_000.00m);
        AssertVariant(afterDto, EacVariant.Atypical, 1.000000m, 74_000_000.00m, 114_000_000.00m, -4_000_000.00m); // VAC == CV, survives the move.

        AssertVariant(beforeDto, EacVariant.CpiSpiBased, 1.234568m, 79_012_345.68m, 119_012_345.68m, -19_012_345.68m);
        AssertVariant(afterDto, EacVariant.CpiSpiBased, 1.234568m, 91_358_024.69m, 131_358_024.69m, -21_358_024.69m);

        // BottomUpEtc: the ⚠ danger domain-rules.md §5.7 calls out - EAC does NOT move (the manual ETC
        // does not know about the VO) while VAC improves by the FULL VO amount, purely because the
        // estimate went stale, not because anything real improved.
        AssertVariant(beforeDto, EacVariant.BottomUpEtc, null, 70_000_000.00m, 110_000_000.00m, -10_000_000.00m);
        AssertVariant(afterDto, EacVariant.BottomUpEtc, null, 70_000_000.00m, 110_000_000.00m, 0.00m);

        AssertVariant(beforeDto, EacVariant.CustomPf, 1.20m, 76_800_000.00m, 116_800_000.00m, -16_800_000.00m);
        AssertVariant(afterDto, EacVariant.CustomPf, 1.20m, 88_800_000.00m, 128_800_000.00m, -18_800_000.00m);

        // TCPI_EAC (measured against CpiBased) == CPI exactly, both sides of the boundary.
        Assert.Equal(0.900000m, Math.Round(beforeDto.TcpiEac!.Value, 6));
        Assert.Equal(0.900000m, Math.Round(afterDto.TcpiEac!.Value, 6));

        // --- Snapshot immutability: re-read from a brand-new context, compare every column
        // (domain-rules.md §5.5(a): "including RowVersion/CreatedAt, not just Bac"). ---
        using (var context = harness.CreateContext())
        {
            var s = await context.EvmPeriodSnapshots.AsNoTracking().SingleAsync(x => x.Id == snapshotId);
            object[] snapshotAfter = [s.Bac, s.Pv, s.Ev, s.Ac, s.EacVariant, s.PerformanceFactor!, s.Eac!, s.Etc!, s.Vac!, s.CreatedAt, s.CreatedByUserId!];
            Assert.Equal(snapshotBefore, snapshotAfter);
            Assert.Equal(100_000_000.00m, s.Bac); // still the pre-VO figure - never rewritten.
        }
    }

    private static void AssertVariant(EvmResponseDto dto, EacVariant variant, decimal? pf, decimal etc, decimal eac, decimal vac)
    {
        var actual = dto.Variants.Single(v => v.Variant == variant);
        Assert.True(actual.Computable, $"{variant} expected computable but was not (reason: {actual.Reason}).");
        if (pf is not null)
        {
            Assert.Equal(pf.Value, actual.PerformanceFactor);
        }
        else
        {
            Assert.Null(actual.PerformanceFactor);
        }

        Assert.Equal(etc, actual.Etc);
        Assert.Equal(eac, actual.Eac);
        Assert.Equal(vac, actual.Vac);
    }

    /// <summary>
    /// KNOWN GAP found during S10-QA-02 independent verification - NOT a regression in previously
    /// passing behaviour (no test anywhere previously asserted this). domain-rules.md §5.7's explicit
    /// rule: "While stale [<see cref="Project.EacManualEtcStaleSince"/> set], the EVM response must
    /// return BottomUpEtc with a data-quality warning ManualEtcPredatesBacChange (carrying the VO
    /// number and the BAC delta), and the frontend must render the tile in a warning state." Only HALF
    /// of this is implemented: <see cref="Project.ApplyVariationOrderApproval"/> correctly stamps
    /// <see cref="Project.EacManualEtcStaleSince"/> (asserted below, and it passes) - but nothing
    /// downstream ever reads it back: <c>ProjectEvmSettings</c> does not carry the field,
    /// <c>EacCalculator</c> never emits the warning, and <c>EvmWarningCodes</c> has no
    /// <c>ManualEtcPredatesBacChange</c> constant at all, so the warning cannot structurally ever be
    /// produced. Left failing deliberately so a fix is verifiable by this test going green, rather than
    /// re-litigated from prose - see the QA handoff report for the code citations.
    /// </summary>
    [Fact]
    public async Task KnownGap_Evm_Response_Does_Not_Surface_ManualEtcPredatesBacChange_After_A_Vo_Moves_Bac()
    {
        var approvalInstant = DateTimeOffset.Parse("2026-07-15T00:00:00Z");
        var harness = new VariationOrderWorkflowHarness(now: approvalInstant);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 100_000_000.00m);
        var activityId = await harness.SeedActivityAsync(projectId, budgetCost: 100_000_000.00m);

        using (var context = harness.CreateContext())
        {
            var project = await context.Projects.SingleAsync(p => p.Id == projectId);
            project.SetEacManualEtc(70_000_000.00m);
            await context.SaveChangesAsync();
        }

        var creatorId = Guid.NewGuid();
        var voId = await harness.CreateDraftAsync(
            projectId, 10_000_000.00m, creatorId, [new VariationOrderScopeItemInput(activityId, 10_000_000.00m)], voNumber: "VO-STALE-1");
        Assert.True((await harness.SubmitAsync(voId, creatorId, UserRole.QS)).IsSuccess);
        Assert.True((await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.PM)).IsSuccess);
        Assert.True((await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.ProjectDirector)).IsSuccess);
        var finalApproval = await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.Executive);
        Assert.True(finalApproval.IsSuccess, finalApproval.IsFailure ? finalApproval.Error : string.Empty);

        using var verifyContext = harness.CreateContext();
        var projectAfter = await verifyContext.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId);
        Assert.Equal(approvalInstant, projectAfter.EacManualEtcStaleSince); // the half that DOES work today.

        var actualCostReader = new FixedActualCostReader(40_000_000.00m);
        var service = new EvmComputationService(new EvmDataReader(verifyContext), actualCostReader);
        var computation = await service.ComputeAsync(projectId, approvalInstant);

        // This is the half that does NOT work today - expected to fail.
        Assert.Contains("ManualEtcPredatesBacChange", computation!.EacResult.Warnings);
    }

    /// <summary>
    /// KNOWN GAP found during S10-QA-02 independent verification - NOT a regression (no test anywhere
    /// previously asserted this). domain-rules.md §5.5(c): "BAC(t) must be a function of t, not a
    /// scalar read... Required: add Project.OriginalBac decimal(18,2); compute BAC(t) for every
    /// historical read." No such field or reconstruction exists anywhere in this codebase - <c>Project</c>
    /// has no <c>OriginalBac</c>, and <c>EvmDataReader.GetProjectSettingsAsync</c> selects the LIVE
    /// <c>Project.BAC</c> unconditionally (it is not even given the <c>asOf</c> parameter
    /// <c>GetActivityInputsAsync</c> receives). This test proves the observable consequence with a
    /// real VO approval: a data date that predates the VO's own <c>ApprovedAt</c> silently starts
    /// reporting the POST-VO BAC the instant the VO is approved, even though nothing about that
    /// historical period changed - exactly the "two different answers depending on whether that period
    /// happened to be closed" failure mode the domain doc warns about. Left failing deliberately so a
    /// fix is verifiable by this test going green.
    /// </summary>
    [Fact]
    public async Task KnownGap_A_Live_Query_At_A_Data_Date_Before_The_Vos_ApprovedAt_Reflects_The_Vos_Bac_Move()
    {
        var approvalInstant = DateTimeOffset.Parse("2026-07-15T00:00:00Z");
        var pastDataDate = DateTimeOffset.Parse("2026-03-01T00:00:00Z"); // well before the VO exists.

        var harness = new VariationOrderWorkflowHarness(now: approvalInstant);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 100_000_000.00m);
        var activityId = await harness.SeedActivityAsync(projectId, budgetCost: 100_000_000.00m);

        var actualCostReader = new FixedActualCostReader(0m);

        decimal bacAtPastDateBeforeVo;
        using (var context = harness.CreateContext())
        {
            var service = new EvmComputationService(new EvmDataReader(context), actualCostReader);
            var computation = await service.ComputeAsync(projectId, pastDataDate);
            bacAtPastDateBeforeVo = computation!.Core.Bac;
        }

        Assert.Equal(100_000_000.00m, bacAtPastDateBeforeVo);

        var creatorId = Guid.NewGuid();
        var voId = await harness.CreateDraftAsync(
            projectId, 10_000_000.00m, creatorId, [new VariationOrderScopeItemInput(activityId, 10_000_000.00m)], voNumber: "VO-BACT-1");
        Assert.True((await harness.SubmitAsync(voId, creatorId, UserRole.QS)).IsSuccess);
        Assert.True((await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.PM)).IsSuccess);
        Assert.True((await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.ProjectDirector)).IsSuccess);
        var finalApproval = await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.Executive);
        Assert.True(finalApproval.IsSuccess, finalApproval.IsFailure ? finalApproval.Error : string.Empty);

        decimal bacAtPastDateAfterVo;
        using (var context = harness.CreateContext())
        {
            var service = new EvmComputationService(new EvmDataReader(context), actualCostReader);
            var computation = await service.ComputeAsync(projectId, pastDataDate);
            bacAtPastDateAfterVo = computation!.Core.Bac;
        }

        // What domain-rules.md §5.5(c) requires: a data date before ApprovedAt must still read the
        // OLD BAC. Expected to fail against today's implementation, which returns 110,000,000.00.
        Assert.Equal(100_000_000.00m, bacAtPastDateAfterVo);
    }
}
