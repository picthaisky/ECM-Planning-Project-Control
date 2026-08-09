using CMPlus.Application.Features.CashFlow;
using CMPlus.Application.Features.CashFlow.Queries.GetCashFlow;
using CMPlus.Application.Features.Dashboard.Queries.GetDashboard;
using CMPlus.Application.Features.Evm.Queries.GetEvm;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Integration.Tests.Persistence;

namespace CMPlus.Integration.Tests.Dashboard;

/// <summary>
/// S8-QA-01 / subtlety flagged by backend-developer: Cash Flow's period bars come only from closed
/// <see cref="EvmPeriodSnapshot"/> rows (plus one trailing live bucket), while Cash Flow's top-level
/// cumulative is always live (to match <c>GetEvmQuery</c>'s own always-live headline -
/// <c>GetCashFlowQueryHandler</c>'s remarks). When the effective data date coincides exactly with a
/// closed snapshot <b>and</b> a backdated <see cref="ActualCostEntry"/> has since restated it, those
/// two legitimately disagree - the handler raises <see cref="CashFlowWarningCodes.PeriodRestated"/>
/// for exactly this. Both directions are tested here: a warning that is always on is exactly as
/// useless as one that never fires, so a "no restatement -&gt; no warning" fixture matters as much as
/// the "restatement -&gt; warning" one.
/// </summary>
public class CashFlowPeriodRestatementTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset SnapshotDataDate = DateTimeOffset.Parse("2026-02-28T00:00:00+07:00");

    private sealed record BaseFixture(Guid ProjectId, Guid ActivityId);

    /// <summary>
    /// One project, one fully-elapsed activity (BudgetCost 1,000,000, so PV(SnapshotDataDate) =
    /// 1,000,000.00) progressed to 40% as of <see cref="SnapshotDataDate"/> (EV = 400,000.00), one
    /// <see cref="ActualCostEntry"/> of 350,000.00 incurred before that date (AC = 350,000.00) - then
    /// closes an <see cref="EvmPeriodSnapshot"/> at exactly <see cref="SnapshotDataDate"/> whose own
    /// stored figures are <i>exactly</i> what a live recomputation at that date produces right now
    /// (CpiBased: PF = AC/EV = 350,000/400,000 = 0.875; ETC = 0.875×600,000 = 525,000.00;
    /// EAC = 875,000.00; VAC = 125,000.00) - i.e. a period that has not (yet) been restated.
    /// </summary>
    private static async Task<BaseFixture> SeedClosedPeriodWithNoRestatementYetAsync(TestDbContextFactory factory)
    {
        var project = Project.Create(
            TenantId, "Restatement Fixture", "RESTATE-1", "Owner",
            SnapshotDataDate.AddYears(-1), SnapshotDataDate.AddYears(1), bac: 1_000_000.00m, dataDate: SnapshotDataDate);
        var node = new WBSNode(TenantId, project.Id, "1", "Main Works", weightPercentage: 100.00m);
        var activity = new Activity(
            TenantId, node.Id, "A-1", "Main Works",
            SnapshotDataDate.AddMonths(-6), SnapshotDataDate.AddDays(-1), durationDays: 179, budgetCost: 1_000_000.00m);

        var recordedBy = Guid.NewGuid();
        var progress = activity.RecordProgress(SnapshotDataDate, 40.00m, null, recordedBy, ProgressSource.Manual, SnapshotDataDate);

        var costEntry = new ActualCostEntry(
            TenantId, project.Id, node.Id, null, CostCategory.Subcontract, ActualCostEntryType.Actual,
            ActualCostSource.ManualEntry, amount: 350_000.00m, incurredDate: SnapshotDataDate.AddDays(-10),
            postedAt: SnapshotDataDate.AddDays(-9), postedByUserId: recordedBy, reversesEntryId: null,
            documentReference: "INV-100", costCode: null, vendorName: "Vendor A", note: null,
            fileImportJobId: null, paidDate: null, quantity: null, unitOfMeasure: null);

        var closedSnapshot = new EvmPeriodSnapshot(
            TenantId, project.Id, SnapshotDataDate,
            bac: 1_000_000.00m, pv: 1_000_000.00m, ev: 400_000.00m, ac: 350_000.00m,
            eacVariant: EacVariant.CpiBased, performanceFactor: 0.875000m, eac: 875_000.00m,
            etc: 525_000.00m, vac: 125_000.00m, createdAt: SnapshotDataDate, createdByUserId: recordedBy);

        using var seedContext = factory.CreateContext();
        seedContext.Projects.Add(project);
        seedContext.WBSNodes.Add(node);
        seedContext.Activities.Add(activity);
        seedContext.ActivityProgressLogs.Add(progress);
        seedContext.ActualCostEntries.Add(costEntry);
        seedContext.EvmPeriodSnapshots.Add(closedSnapshot);
        await seedContext.SaveChangesAsync();

        return new BaseFixture(project.Id, activity.Id);
    }

    [Fact]
    public async Task No_Warning_Fires_When_The_Live_Recomputation_At_The_Snapshot_Date_Genuinely_Still_Agrees()
    {
        var factory = new TestDbContextFactory(TenantId);
        var fixture = await SeedClosedPeriodWithNoRestatementYetAsync(factory);

        var cashFlow = await RealHandlerFactory.GetCashFlowAsync(
            factory, new GetCashFlowQuery(fixture.ProjectId, SnapshotDataDate, From: null));

        Assert.True(cashFlow.IsSuccess);
        Assert.DoesNotContain(CashFlowWarningCodes.PeriodRestated, cashFlow.Value.Warnings);

        // The snapshot sits exactly at the effective data date -> no trailing live bucket, and the
        // one (closed) bucket's own cumulative equals the always-live top-level headline, because
        // nothing has restated it yet.
        var period = Assert.Single(cashFlow.Value.Periods);
        Assert.True(period.IsClosed);
        Assert.Equal(350_000.00m, period.AcCumulative);
        Assert.Equal(350_000.00m, cashFlow.Value.AcCumulative);

        // Cross-checked against a fully independent handler call (EVM) at the same date - both
        // "live" readings agree with each other and with the frozen snapshot, as they should when no
        // restatement has happened.
        var evm = await RealHandlerFactory.GetEvmAsync(
            factory, new GetEvmQuery(fixture.ProjectId, SnapshotDataDate, EacVariant: null));
        Assert.True(evm.IsSuccess);
        Assert.Equal(350_000.00m, evm.Value.Ac);
    }

    [Fact]
    public async Task Warning_Fires_When_A_Backdated_Actual_Cost_Entry_Restates_A_Period_Already_Closed()
    {
        var factory = new TestDbContextFactory(TenantId);
        var fixture = await SeedClosedPeriodWithNoRestatementYetAsync(factory);

        // A backdated correction lands *after* the period closed - IncurredDate is still <=
        // SnapshotDataDate (ADR-0013: valid time drives AC(t)), so the live AC(SnapshotDataDate) is
        // no longer 350,000.00; the already-closed EvmPeriodSnapshot itself is never touched
        // (append-only, immutable - ADR-0009).
        using (var backdateContext = factory.CreateContext())
        {
            var lateEntry = new ActualCostEntry(
                TenantId, fixture.ProjectId, null, null, CostCategory.Material, ActualCostEntryType.Adjustment,
                ActualCostSource.ManualEntry, amount: 90_000.00m, incurredDate: SnapshotDataDate.AddDays(-3),
                postedAt: SnapshotDataDate.AddDays(5), postedByUserId: Guid.NewGuid(), reversesEntryId: null,
                documentReference: "ADJ-001", costCode: null, vendorName: null,
                note: "Late-arriving subcontractor invoice discovered after period close.",
                fileImportJobId: null, paidDate: null, quantity: null, unitOfMeasure: null);
            backdateContext.ActualCostEntries.Add(lateEntry);
            await backdateContext.SaveChangesAsync();
        }

        var cashFlow = await RealHandlerFactory.GetCashFlowAsync(
            factory, new GetCashFlowQuery(fixture.ProjectId, SnapshotDataDate, From: null));

        Assert.True(cashFlow.IsSuccess);
        Assert.Contains(CashFlowWarningCodes.PeriodRestated, cashFlow.Value.Warnings);

        // Top-level cumulative reflects the new, live truth (350,000 + 90,000 = 440,000.00) - stays
        // consistent with GetEvmQuery's own always-live headline (checked below) - while the closed
        // bucket keeps showing exactly what was frozen at close time (350,000.00). This is the
        // "legitimate disagreement" the warning exists to explain, never silently hidden.
        Assert.Equal(440_000.00m, cashFlow.Value.AcCumulative);
        var period = Assert.Single(cashFlow.Value.Periods);
        Assert.True(period.IsClosed);
        Assert.Equal(350_000.00m, period.AcCumulative);

        // The restatement is visible identically from EVM's and Dashboard's own independent handler
        // calls at the same date - the "always live" top-level figure is the same live figure
        // everywhere, only Cash Flow's own historical Periods[] bucket intentionally lags (with the
        // warning explaining why), never a different, unexplained number on a third screen.
        var evm = await RealHandlerFactory.GetEvmAsync(
            factory, new GetEvmQuery(fixture.ProjectId, SnapshotDataDate, EacVariant: null));
        Assert.True(evm.IsSuccess);
        Assert.Equal(440_000.00m, evm.Value.Ac);

        var dashboard = await RealHandlerFactory.GetDashboardAsync(
            factory, new GetDashboardQuery(fixture.ProjectId, SnapshotDataDate));
        Assert.True(dashboard.IsSuccess);
        Assert.Equal(440_000.00m, dashboard.Value.Ac);
    }
}
