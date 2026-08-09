using CMPlus.Application.Features.CashFlow;
using CMPlus.Application.Features.CashFlow.Queries.GetCashFlow;
using CMPlus.Application.Features.Dashboard.Queries.GetDashboard;
using CMPlus.Application.Features.Evm.Queries.GetEvm;
using CMPlus.Application.Features.Wbs.Queries.GetNodeActivities;
using CMPlus.Application.Services.Wbs;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Integration.Tests.Persistence;

namespace CMPlus.Integration.Tests.Dashboard;

/// <summary>
/// S8-QA-01 (docs/10 §7 Sprint 8, DoD: "Dashboard = EVM = Cash Flow = WBS rollup - ทุกตัวเลขที่ใช้ร่วมกัน").
/// Seeds one project through the real stack (EF Core InMemory - no Sqlite/SQL Server, per the Docker
/// outage recorded in docs/perf/gantt-frontend-s6.md §3; InMemory proves the Application-layer wiring
/// and arithmetic end-to-end but does not prove SQL-translation correctness against real SQL Server -
/// that remains database-engineer's/the real-DB integration suite's job) with non-trivial WBS/
/// activity/actual-cost/closed-snapshot data, then reads it back through three independent real
/// handler calls (<see cref="RealHandlerFactory"/> - never a shared pre-computed value) and asserts
/// every figure the DoD calls "shared" agrees to the satang (money) / 2dp (the WBS rollup,
/// specifically called out at that precision by S8-BE-02's own DoD line).
///
/// <para><b>Not every EVM-screen figure is "shared" in the DoD's sense</b> - <see cref="CashFlowResponseDto"/>
/// carries no <c>Sv</c>/<c>Cv</c>/<c>Spi</c>/<c>Cpi</c>/EAC fields at all (S8-BE-01's DoD only promises
/// "ตัวเลขรายงวด + สะสม" - period + cumulative BAC/PV/EV/AC - matches the EVM screen), and neither
/// <see cref="CashFlowResponseDto"/> nor <see cref="DashboardResponseDto"/> carries <c>TcpiBac</c>/
/// <c>TcpiEac</c> (EVM-screen-only, per that screen's own "12-metric table"). This suite therefore
/// cross-checks BAC/PV/EV/AC/ActualCostEntryCount across all three, and SV/CV/SPI/CPI/EAC/ETC/VAC
/// across EVM/Dashboard only - asserting the *absence* of a field on a DTO would be a compile error,
/// not a runtime one, so there is nothing to additionally assert there beyond this comment recording
/// that the omission was checked and is intentional, not a coverage gap.</para>
/// </summary>
public class CrossScreenNumericConsistencyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // The single "as of" date every handler call below requests explicitly - deliberately not
    // Project.DataDate's own default-fallback path (that resolution is already covered by
    // GetEvmQueryHandlerTests/GetCashFlowQueryHandlerTests/GetDashboardQueryHandlerTests), so this
    // fixture's numbers are never sensitive to which fallback rule ran.
    private static readonly DateTimeOffset DataDate = DateTimeOffset.Parse("2026-06-30T00:00:00+07:00");

    private sealed record SeededFixture(Guid ProjectId, Guid StructureNodeId, Guid ArchitecturalNodeId);

    /// <summary>
    /// Structure (weight 60): STR-1 budget 500,000/progress 60%, STR-2 budget 300,000/progress 0%
    /// (no <c>ActivityProgressLog</c> entry at all - deliberately exercises the "no entry yet -&gt; 0"
    /// path for both EV and the WBS rollup simultaneously). Architectural (weight 40): ARC-1 budget
    /// 200,000/progress 50%, planned to start *after* <see cref="DataDate"/> so its planned-% is
    /// exactly 0 - chosen so PV never needs the day-fraction interpolation branch of
    /// <c>EvmEngine.ComputePlannedPercentage</c> (that branch has its own dedicated coverage in
    /// <c>EvmEngineTests</c>; this fixture only needs PV to be an exact, hand-checkable sum).
    ///
    /// <para>Hand-worked ground truth (independently re-derivable by any reader, the same discipline
    /// as evm-formulas.md's own worked examples):</para>
    /// <list type="bullet">
    /// <item>BAC = 500,000 + 300,000 + 200,000 = 1,000,000.00 (also set directly as <c>Project.BAC</c>).</item>
    /// <item>PV = 500,000×100% + 300,000×100% + 200,000×0% = 800,000.00.</item>
    /// <item>EV = 500,000×60% + 300,000×0% + 200,000×50% = 300,000 + 0 + 100,000 = 400,000.00.</item>
    /// <item>AC = 300,000.00 + 200,000.00 (two <c>ActualCostEntry</c> rows) = 500,000.00, count 2.</item>
    /// <item>SV = EV−PV = −400,000.00; CV = EV−AC = −100,000.00; SPI = 400,000/800,000 = 0.500000;
    /// CPI = 400,000/500,000 = 0.800000; TCPI_BAC = (1,000,000−400,000)/(1,000,000−500,000) =
    /// 600,000/500,000 = 1.200000.</item>
    /// <item>CpiBased: PF = 1/CPI = 1.250000; ETC = 1.25×600,000 = 750,000.00;
    /// EAC = 500,000+750,000 = 1,250,000.00 (= BAC/CPI = 1,000,000/0.8, confirming the identity);
    /// VAC = 1,000,000−1,250,000 = −250,000.00; TCPI_EAC = 600,000/750,000 = 0.800000 = CPI (the
    /// evm-formulas.md invariant).</item>
    /// <item>WBS rollup: leaf(Structure) = (500,000×60 + 300,000×0)/800,000 = 37.50; leaf(Architectural)
    /// = 50.00 (single activity); project = (37.50×60 + 50.00×40)/100 = (2,250+2,000)/100 = 42.50.
    /// Weights sum to 100 at the only level that exists, so no weight warning and no mixed-scope
    /// node (both WBS nodes are plain leaves) - this fixture's job is the happy-path numeric
    /// agreement; weight-warning/mixed-scope wiring has its own dedicated coverage in
    /// <c>WbsProgressRollupWiringTests</c>.</item>
    /// </list>
    ///
    /// <para>Also seeds one closed <see cref="EvmPeriodSnapshot"/> well before <see cref="DataDate"/>
    /// (satisfies this suite's "at least one closed snapshot" requirement) so the Cash Flow period
    /// bars exercise both a closed bucket and a trailing live bucket - its own date never coincides
    /// with the query's effective data date, so <c>CashFlowWarningCodes.PeriodRestated</c> can never
    /// fire from this fixture (that is deliberately its own, isolated scenario - see
    /// <c>CashFlowPeriodRestatementTests</c>).</para>
    /// </summary>
    private static async Task<SeededFixture> SeedAsync(TestDbContextFactory factory)
    {
        var project = Project.Create(
            TenantId, "Cross-Screen Fixture", "XSCR-1", "Owner",
            DataDate.AddYears(-1), DataDate.AddYears(1), bac: 1_000_000.00m, dataDate: DataDate);

        var structureNode = new WBSNode(TenantId, project.Id, "1", "Structure", weightPercentage: 60.00m);
        var architecturalNode = new WBSNode(TenantId, project.Id, "2", "Architectural", weightPercentage: 40.00m);

        var str1 = new Activity(
            TenantId, structureNode.Id, "STR-1", "Foundation",
            DateTimeOffset.Parse("2025-06-01T00:00:00+07:00"), DateTimeOffset.Parse("2026-05-01T00:00:00+07:00"),
            durationDays: 334, budgetCost: 500_000.00m); // fully elapsed by DataDate -> planned% = 100.
        var str2 = new Activity(
            TenantId, structureNode.Id, "STR-2", "Columns",
            DateTimeOffset.Parse("2025-08-01T00:00:00+07:00"), DateTimeOffset.Parse("2026-04-01T00:00:00+07:00"),
            durationDays: 243, budgetCost: 300_000.00m); // fully elapsed too; deliberately no progress entry.
        var arc1 = new Activity(
            TenantId, architecturalNode.Id, "ARC-1", "Finishes",
            DateTimeOffset.Parse("2026-08-01T00:00:00+07:00"), DateTimeOffset.Parse("2026-12-01T00:00:00+07:00"),
            durationDays: 122, budgetCost: 200_000.00m); // starts AFTER DataDate -> planned% = 0.

        var recordedBy = Guid.NewGuid();
        var str1Progress = str1.RecordProgress(DataDate, 60.00m, null, recordedBy, ProgressSource.Manual, DataDate);
        var arc1Progress = arc1.RecordProgress(DataDate, 50.00m, null, recordedBy, ProgressSource.Manual, DataDate);

        var costEntry1 = new ActualCostEntry(
            TenantId, project.Id, structureNode.Id, null, CostCategory.Subcontract, ActualCostEntryType.Actual,
            ActualCostSource.ManualEntry, amount: 300_000.00m, incurredDate: DateTimeOffset.Parse("2026-06-01T00:00:00+07:00"),
            postedAt: DateTimeOffset.Parse("2026-06-02T00:00:00+07:00"), postedByUserId: recordedBy,
            reversesEntryId: null, documentReference: "INV-001", costCode: null, vendorName: "Vendor A",
            note: null, fileImportJobId: null, paidDate: null, quantity: null, unitOfMeasure: null);
        var costEntry2 = new ActualCostEntry(
            TenantId, project.Id, architecturalNode.Id, null, CostCategory.Material, ActualCostEntryType.Actual,
            ActualCostSource.ManualEntry, amount: 200_000.00m, incurredDate: DateTimeOffset.Parse("2026-06-20T00:00:00+07:00"),
            postedAt: DateTimeOffset.Parse("2026-06-21T00:00:00+07:00"), postedByUserId: recordedBy,
            reversesEntryId: null, documentReference: "INV-002", costCode: null, vendorName: "Vendor B",
            note: null, fileImportJobId: null, paidDate: null, quantity: null, unitOfMeasure: null);

        // Closed period a month prior: Bac 1,000,000/Pv 100,000/Ev 80,000/Ac 60,000 -> PF = AC/EV =
        // 0.75; ETC = 0.75×920,000 = 690,000.00; EAC = 750,000.00; VAC = 250,000.00. These are never
        // asserted directly by name in this test (only via the Periods[]/cumulative structural
        // identities below) - correctness of EAC arithmetic itself is EacCalculatorTests' job.
        var closedSnapshot = new EvmPeriodSnapshot(
            TenantId, project.Id, DateTimeOffset.Parse("2026-04-30T00:00:00+07:00"),
            bac: 1_000_000.00m, pv: 100_000.00m, ev: 80_000.00m, ac: 60_000.00m,
            eacVariant: EacVariant.CpiBased, performanceFactor: 0.750000m, eac: 750_000.00m,
            etc: 690_000.00m, vac: 250_000.00m,
            createdAt: DateTimeOffset.Parse("2026-04-30T00:00:00+07:00"), createdByUserId: null);

        using var seedContext = factory.CreateContext();
        seedContext.Projects.Add(project);
        seedContext.WBSNodes.AddRange(structureNode, architecturalNode);
        seedContext.Activities.AddRange(str1, str2, arc1);
        seedContext.ActivityProgressLogs.AddRange(str1Progress, arc1Progress);
        seedContext.ActualCostEntries.AddRange(costEntry1, costEntry2);
        seedContext.EvmPeriodSnapshots.Add(closedSnapshot);
        await seedContext.SaveChangesAsync();

        return new SeededFixture(project.Id, structureNode.Id, architecturalNode.Id);
    }

    [Fact]
    public async Task Bac_Pv_Ev_Ac_And_Actual_Cost_Entry_Count_Agree_To_The_Satang_Across_Evm_CashFlow_And_Dashboard()
    {
        var factory = new TestDbContextFactory(TenantId);
        var fixture = await SeedAsync(factory);

        var evm = await RealHandlerFactory.GetEvmAsync(factory, new GetEvmQuery(fixture.ProjectId, DataDate, EacVariant: null));
        var cashFlow = await RealHandlerFactory.GetCashFlowAsync(factory, new GetCashFlowQuery(fixture.ProjectId, DataDate, From: null));
        var dashboard = await RealHandlerFactory.GetDashboardAsync(factory, new GetDashboardQuery(fixture.ProjectId, DataDate));

        Assert.True(evm.IsSuccess);
        Assert.True(cashFlow.IsSuccess);
        Assert.True(dashboard.IsSuccess);

        // Ground truth first (ties every downstream cross-check to an independently-verifiable
        // number, not just to "whatever the code happens to produce" - see SeedAsync's worked math).
        Assert.Equal(1_000_000.00m, evm.Value.Bac);
        Assert.Equal(800_000.00m, evm.Value.Pv);
        Assert.Equal(400_000.00m, evm.Value.Ev);
        Assert.Equal(500_000.00m, evm.Value.Ac);

        // Cross-screen agreement - each figure read through its own screen's real handler call.
        Assert.Equal(evm.Value.Bac, cashFlow.Value.Bac);
        Assert.Equal(evm.Value.Pv, cashFlow.Value.PvCumulative);
        Assert.Equal(evm.Value.Ev, cashFlow.Value.EvCumulative);
        Assert.Equal(evm.Value.Ac, cashFlow.Value.AcCumulative);

        Assert.Equal(evm.Value.Bac, dashboard.Value.Bac);
        Assert.Equal(evm.Value.Pv, dashboard.Value.Pv);
        Assert.Equal(evm.Value.Ev, dashboard.Value.Ev);
        Assert.Equal(evm.Value.Ac, dashboard.Value.Ac);

        // ActualCostEntryCount: only carried on CashFlow/Dashboard - EvmResponseDto deliberately
        // omits it (see EvmComputation.ActualCostEntryCount's own remarks: "deliberately not surfaced
        // on EvmResponseDto in this change"), so there is no third figure to compare it against here.
        Assert.Equal(2, cashFlow.Value.ActualCostEntryCount);
        Assert.Equal(2, dashboard.Value.ActualCostEntryCount);
    }

    [Fact]
    public async Task Sv_Cv_Spi_Cpi_And_The_Default_Eac_Variant_Agree_To_The_Satang_Between_Evm_And_Dashboard()
    {
        var factory = new TestDbContextFactory(TenantId);
        var fixture = await SeedAsync(factory);

        var evm = await RealHandlerFactory.GetEvmAsync(factory, new GetEvmQuery(fixture.ProjectId, DataDate, EacVariant: null));
        var dashboard = await RealHandlerFactory.GetDashboardAsync(factory, new GetDashboardQuery(fixture.ProjectId, DataDate));

        Assert.True(evm.IsSuccess);
        Assert.True(dashboard.IsSuccess);

        Assert.Equal(-400_000.00m, evm.Value.Sv);
        Assert.Equal(-100_000.00m, evm.Value.Cv);
        Assert.Equal(0.500000m, evm.Value.Spi);
        Assert.Equal(0.800000m, evm.Value.Cpi);
        Assert.Equal(1.200000m, evm.Value.TcpiBac);
        Assert.Equal(0.800000m, evm.Value.TcpiEac); // TCPI measured against CpiBased EAC always equals CPI exactly.

        var cpiBased = evm.Value.Variants.Single(v => v.Variant == EacVariant.CpiBased);
        Assert.True(cpiBased.Computable);
        Assert.Equal(1.250000m, cpiBased.PerformanceFactor);
        Assert.Equal(750_000.00m, cpiBased.Etc);
        Assert.Equal(1_250_000.00m, cpiBased.Eac);
        Assert.Equal(-250_000.00m, cpiBased.Vac);

        // The project's own EacVariantDefault was never overridden (seeded default = CpiBased, and
        // neither query passed an override), so EVM's selected variant and Dashboard's own (which has
        // no override parameter at all - see GetDashboardQuery's remarks) must be this same variant.
        Assert.Equal(EacVariant.CpiBased, evm.Value.SelectedVariant);
        Assert.Equal(EacVariant.CpiBased, dashboard.Value.EacVariant);

        Assert.Equal(evm.Value.Sv, dashboard.Value.Sv);
        Assert.Equal(evm.Value.Cv, dashboard.Value.Cv);
        Assert.Equal(evm.Value.Spi, dashboard.Value.Spi);
        Assert.Equal(evm.Value.Cpi, dashboard.Value.Cpi);
        Assert.Equal(cpiBased.PerformanceFactor, dashboard.Value.PerformanceFactor);
        Assert.Equal(cpiBased.Etc, dashboard.Value.Etc);
        Assert.Equal(cpiBased.Eac, dashboard.Value.Eac);
        Assert.Equal(cpiBased.Vac, dashboard.Value.Vac);
        Assert.Equal(cpiBased.Computable, dashboard.Value.EacComputable);
        Assert.Equal(cpiBased.Reason, dashboard.Value.EacNullReason);
    }

    [Fact]
    public async Task The_Wbs_Progress_Rollup_Agrees_To_Two_Decimal_Places_Between_Dashboard_And_The_Wbs_Screens_Own_Readers()
    {
        var factory = new TestDbContextFactory(TenantId);
        var fixture = await SeedAsync(factory);

        var dashboard = await RealHandlerFactory.GetDashboardAsync(factory, new GetDashboardQuery(fixture.ProjectId, DataDate));
        Assert.True(dashboard.IsSuccess);

        // Ground truth (worked by hand in SeedAsync's remarks): 42.50.
        Assert.Equal(42.50m, dashboard.Value.ProgressRollup.ProgressPercentage);
        Assert.Empty(dashboard.Value.ProgressRollup.WeightWarnings);
        Assert.Empty(dashboard.Value.ProgressRollup.MixedScopeWbsNodeIds);

        // Independently recompute via the real EF-backed WBS readers (IWbsTreeReader/
        // IWbsProgressReader) plus a *second*, separately-invoked call to the same pure calculator -
        // through a brand-new DbContext, never the Dashboard handler's own result object. This proves
        // the wiring (real reader -> real reader -> WbsProgressRollupCalculator) end to end rather
        // than merely re-asserting a captured value: WbsProgressRollupCalculator itself is a pure,
        // stateless function (like Math.Round), so calling it twice from independently-fetched inputs
        // is not the "same object reference" trap - it is the same proof shape as calling GetEvmAsync
        // twice for the money figures above.
        using (var checkContext = factory.CreateContext())
        {
            var treeReader = new WbsTreeReader(checkContext);
            var progressReader = new WbsProgressReader(checkContext);
            var nodes = await treeReader.GetNodesWithActivityCountsAsync(fixture.ProjectId);
            var activityRows = await progressReader.GetActivityProgressByNodeAsync(fixture.ProjectId);

            var independentRollup = WbsProgressRollupCalculator.Compute(
                nodes.Select(n => new WbsRollupNodeInput(n.Id, n.ParentWbsNodeId, n.WeightPercentage)).ToList(),
                activityRows);

            Assert.Equal(42.50m, RoundingRules.ToPercentage(independentRollup.ProgressPercentage));
        }

        // And cross-check against the literal WBS-screen query (GetNodeActivitiesQueryHandler /
        // ActivityForProgressDto.CurrentProgressPercentage) that GetDashboardQueryHandler's own
        // remarks cite as "the WBS screen this rollup must match" - confirming the leaf-level inputs
        // Dashboard's rollup used are exactly what a user browsing the WBS screen's node-activity
        // grid would see, not some other value.
        var structureActivities = await RealHandlerFactory.GetNodeActivitiesAsync(
            factory, new GetNodeActivitiesQuery(fixture.ProjectId, fixture.StructureNodeId));
        Assert.True(structureActivities.IsSuccess);
        Assert.Equal(60.00m, structureActivities.Value.Single(a => a.ActivityCode == "STR-1").CurrentProgressPercentage);
        Assert.Equal(0.00m, structureActivities.Value.Single(a => a.ActivityCode == "STR-2").CurrentProgressPercentage);

        var architecturalActivities = await RealHandlerFactory.GetNodeActivitiesAsync(
            factory, new GetNodeActivitiesQuery(fixture.ProjectId, fixture.ArchitecturalNodeId));
        Assert.True(architecturalActivities.IsSuccess);
        Assert.Equal(50.00m, Assert.Single(architecturalActivities.Value).CurrentProgressPercentage);
    }

    [Fact]
    public async Task Cash_Flow_Period_Bars_Sum_Back_To_The_Live_Cumulative_With_No_Restatement_Warning()
    {
        var factory = new TestDbContextFactory(TenantId);
        var fixture = await SeedAsync(factory);

        var cashFlow = await RealHandlerFactory.GetCashFlowAsync(factory, new GetCashFlowQuery(fixture.ProjectId, DataDate, From: null));
        Assert.True(cashFlow.IsSuccess);

        // One closed bucket (the seeded April snapshot) + one trailing live bucket up to DataDate.
        Assert.Equal(2, cashFlow.Value.Periods.Count);
        Assert.True(cashFlow.Value.Periods[0].IsClosed);
        Assert.Equal(DateTimeOffset.Parse("2026-04-30T00:00:00+07:00"), cashFlow.Value.Periods[0].PeriodEnd);
        Assert.False(cashFlow.Value.Periods[^1].IsClosed);
        Assert.Equal(DataDate, cashFlow.Value.Periods[^1].PeriodEnd);

        // Structural identity (never a re-summed ledger query): period deltas sum back to the
        // cumulative total, for all three series.
        Assert.Equal(cashFlow.Value.PvCumulative, cashFlow.Value.Periods.Sum(p => p.PvPeriod));
        Assert.Equal(cashFlow.Value.EvCumulative, cashFlow.Value.Periods.Sum(p => p.EvPeriod));
        Assert.Equal(cashFlow.Value.AcCumulative, cashFlow.Value.Periods.Sum(p => p.AcPeriod));

        // The closed snapshot's own date (Apr 30) never coincides with the effective data date
        // (Jun 30 - see GetCashFlowQueryHandler.Handle), so the "does the live headline still agree
        // with the last closed bucket" check is never even reached here; a genuine same-date
        // restatement scenario is CashFlowPeriodRestatementTests' own, isolated fixture.
        Assert.DoesNotContain(CashFlowWarningCodes.PeriodRestated, cashFlow.Value.Warnings);
    }
}
