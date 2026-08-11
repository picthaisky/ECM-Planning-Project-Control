using CMPlus.Application.Features.Dashboard.Queries.GetDashboard;
using CMPlus.Application.Features.Wbs.Queries.GetNodeActivities;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Integration.Tests.Persistence;

namespace CMPlus.Integration.Tests.Dashboard;

/// <summary>
/// S8-QA-01 / subtlety flagged by backend-developer: the Dashboard's WBS progress rollup
/// deliberately reads <b>current</b> <c>Activity.ProgressPercentage</c> (via <c>IWbsProgressReader</c>,
/// which takes no date parameter at all - see that interface's own remarks), never the
/// <c>?dataDate=</c>-parameterized ADR-0009 step-function value <c>IEvmDataReader</c> uses for every
/// other EVM-sourced figure on the same response. This is <b>deliberate, not a bug</b>: the WBS screen
/// this rollup must match (<c>GetNodeActivitiesQuery</c> / <c>ActivityForProgressDto.CurrentProgressPercentage</c>)
/// has no date parameter of its own at all - it always shows "now". If the rollup instead used the
/// dated reconstruction, it would only coincidentally agree with the WBS screen when the requested
/// <c>dataDate</c> happened to equal "today" for every activity, which would violate S8-BE-02's DoD
/// ("rollup ความคืบหน้าตรงกับหน้า WBS ที่ทศนิยม 2 ตำแหน่ง" - must always agree, not "usually").
///
/// <para>This test proves the asymmetry holds by constructing a case where the two values are
/// deliberately different (a later progress entry has been recorded since the requested
/// <c>dataDate</c>), so a future reader who "fixes" this to also use <c>dataDate</c> would see this
/// test fail immediately with the exact figures that would silently diverge from the WBS screen.</para>
/// </summary>
public class DashboardProgressRollupDataDateIndependenceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset OlderDataDate = DateTimeOffset.Parse("2026-01-31T00:00:00+07:00");
    private static readonly DateTimeOffset CurrentDataDate = DateTimeOffset.Parse("2026-03-31T00:00:00+07:00");

    private sealed record Fixture(Guid ProjectId, Guid NodeId);

    /// <summary>
    /// One activity (BudgetCost 200,000, fully elapsed by <see cref="OlderDataDate"/> so PV never
    /// needs interpolation), with <b>two</b> <see cref="ActivityProgressLog"/> entries: 20% as of
    /// <see cref="OlderDataDate"/>, then 90% as of <see cref="CurrentDataDate"/> (a later period,
    /// recorded after the fact) - so <c>Activity.ProgressPercentage</c>'s current cache ends at 90%,
    /// while the ADR-0009 step-function reconstruction *as of <see cref="OlderDataDate"/>* is 20%.
    /// </summary>
    private static async Task<Fixture> SeedAsync(TestDbContextFactory factory)
    {
        var project = Project.Create(
            TenantId, "Rollup Date-Independence Fixture", "ROLLUP-DD-1", "Owner",
            OlderDataDate.AddYears(-1), CurrentDataDate.AddYears(1), bac: 200_000.00m, dataDate: CurrentDataDate);
        var node = new WBSNode(TenantId, project.Id, "1", "Sole Scope", weightPercentage: 100.00m);
        var activity = new Activity(
            TenantId, node.Id, "A-1", "Only Activity",
            OlderDataDate.AddMonths(-6), OlderDataDate.AddDays(-1), durationDays: 179, budgetCost: 200_000.00m);

        var recordedBy = Guid.NewGuid();
        var olderProgress = activity.RecordProgress(OlderDataDate, 20.00m, null, recordedBy, ProgressSource.Manual, OlderDataDate);
        var currentProgress = activity.RecordProgress(CurrentDataDate, 90.00m, null, recordedBy, ProgressSource.Manual, CurrentDataDate);

        using var seedContext = factory.CreateContext();
        seedContext.Projects.Add(project);
        seedContext.WBSNodes.Add(node);
        seedContext.Activities.Add(activity);
        seedContext.ActivityProgressLogs.AddRange(olderProgress, currentProgress);
        await seedContext.SaveChangesAsync();

        return new Fixture(project.Id, node.Id);
    }

    [Fact]
    public async Task Requesting_An_Older_DataDate_Still_Shows_Current_Progress_In_The_Rollup_While_Ev_Uses_The_Older_Value()
    {
        var factory = new TestDbContextFactory(TenantId);
        var fixture = await SeedAsync(factory);

        var dashboard = await RealHandlerFactory.GetDashboardAsync(factory, new GetDashboardQuery(fixture.ProjectId, OlderDataDate));
        Assert.True(dashboard.IsSuccess);

        // Ev IS dataDate-parameterized: as of OlderDataDate, only the 20% entry qualifies (PeriodEndDate
        // <= OlderDataDate) - 200,000 x 20% = 40,000.00, not the 90% current value.
        Assert.Equal(40_000.00m, dashboard.Value.Ev);

        // ProgressRollup is NOT dataDate-parameterized - it reads the current cache (90%) regardless
        // of the OlderDataDate this same request asked for. One activity, one node, weight 100 ->
        // leaf = project = 90.00 exactly.
        Assert.Equal(90.00m, dashboard.Value.ProgressRollup.ProgressPercentage);

        // Cross-checked against the real WBS-screen query itself (no date parameter exists on this
        // query at all - structurally cannot vary with `dataDate`) - confirms 90.00 is genuinely
        // "what the WBS screen shows right now", not a coincidence of this fixture's numbers.
        var nodeActivities = await RealHandlerFactory.GetNodeActivitiesAsync(factory, new GetNodeActivitiesQuery(fixture.ProjectId, fixture.NodeId));
        Assert.True(nodeActivities.IsSuccess);
        Assert.Equal(90.00m, Assert.Single(nodeActivities.Value).CurrentProgressPercentage);
    }

    [Fact]
    public async Task Requesting_The_Current_DataDate_Also_Shows_The_Same_Rollup_Proving_It_Is_Not_Merely_Coincidentally_Correct()
    {
        var factory = new TestDbContextFactory(TenantId);
        var fixture = await SeedAsync(factory);

        var dashboard = await RealHandlerFactory.GetDashboardAsync(factory, new GetDashboardQuery(fixture.ProjectId, CurrentDataDate));
        Assert.True(dashboard.IsSuccess);

        // At CurrentDataDate, Ev legitimately picks up the 90% entry too (PeriodEndDate <=
        // CurrentDataDate now includes both entries, latest wins) - so here Ev and the rollup
        // coincide. That coincidence is exactly why a single-dataDate test could not, by itself,
        // distinguish "the rollup is dateless by design" from "the rollup happens to use the same
        // date" - this pair of tests together is what proves the former.
        Assert.Equal(180_000.00m, dashboard.Value.Ev); // 200,000 x 90%.
        Assert.Equal(90.00m, dashboard.Value.ProgressRollup.ProgressPercentage);
    }
}
