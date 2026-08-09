using CMPlus.Application.Features.Dashboard.Queries.GetDashboard;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Integration.Tests.Persistence;

namespace CMPlus.Integration.Tests.Dashboard;

/// <summary>
/// S8-QA-01, independent look at <c>WbsProgressRollupCalculator</c>'s two resolved (not domain-ruled)
/// ambiguities, requested explicitly by backend-developer: the mixed-scope rule (a node with both
/// children and its own direct activities) and weight-warning propagation. Both are already
/// exhaustively unit-tested against the pure calculator directly in
/// <c>CMPlus.Application.Tests.Services.Wbs.WbsProgressRollupCalculatorTests</c> - this file's job is
/// different: prove those same behaviours survive the *real* wiring (EF-backed
/// <c>IWbsTreeReader</c>/<c>IWbsProgressReader</c> -&gt; the calculator -&gt; <c>DashboardResponseDto</c>'s
/// wire shape), which nothing before this change actually exercised end to end.
///
/// <para><b>QA's own opinion on the two ambiguities (asked for explicitly), recorded here next to the
/// behaviour it pins:</b></para>
/// <list type="bullet">
/// <item><b>Leaf-level budget-weighted average, equal-weight fallback on all-zero budget:</b> agree.
/// It mirrors <c>EV = sum(BudgetCost x Pct/100)</c>'s own weighting principle exactly, and the
/// fallback is the only rule that keeps a fully-complete zero-budget scope from misreporting as 0%
/// (a genuinely worse failure mode than an equal-weight approximation). No objection.</item>
/// <item><b>Mixed-scope: subtree wins, direct activities excluded and flagged:</b> agree with the
/// outcome, but flag the failure mode as worth strengthening later. "Exclude and flag via
/// <c>MixedScopeWbsNodeIds</c>" is the only mathematically defensible choice given the data model has
/// no weight for "this node's own work outside its subtree" to blend against - the alternative
/// (silently folding the direct activities in at some ad-hoc weight) would be *more* wrong, not less.
/// But today <c>MixedScopeWbsNodeIds</c> is a bare list of ids with no progress/budget figure
/// attached, so a PM has no way to see *how much* work is being silently excluded from the rollup
/// without a separate lookup - this is a UX/completeness gap for a later sprint (e.g. surfacing the
/// excluded activities' own budget/progress alongside the flag), not a correctness defect in the
/// rollup math itself, and out of scope for this change to fix.</item>
/// </list>
/// </summary>
public class WbsProgressRollupWiringTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset DataDate = DateTimeOffset.Parse("2026-06-30T00:00:00+07:00");

    [Fact]
    public async Task A_Node_With_Both_Children_And_Its_Own_Direct_Activities_Excludes_The_Direct_Activities_And_Is_Flagged_Through_The_Real_Stack()
    {
        var factory = new TestDbContextFactory(TenantId);
        var project = Project.Create(
            TenantId, "Mixed Scope Fixture", "MIXED-1", "Owner",
            DataDate.AddYears(-1), DataDate.AddYears(1), bac: 100_000.00m, dataDate: DataDate);

        var parent = new WBSNode(TenantId, project.Id, "1", "Parent (non-standard: also has direct work)", weightPercentage: 100.00m);
        var child = new WBSNode(TenantId, project.Id, "1.1", "Child", weightPercentage: 100.00m, parentWbsNodeId: parent.Id);

        // The child's own, real scope - this is what should end up as the parent's rollup figure.
        var childActivity = new Activity(TenantId, child.Id, "C-1", "Child Work", DataDate.AddMonths(-2), DataDate.AddDays(-1), 60, 1_000.00m);
        // A direct activity on the non-leaf parent node - non-standard, must be excluded from the
        // subtree rollup (deliberately given a wildly different progress value so an accidental
        // inclusion would be obvious).
        var parentDirectActivity = new Activity(TenantId, parent.Id, "P-1", "Parent's Own Direct Work", DataDate.AddMonths(-2), DataDate.AddDays(-1), 60, 1_000.00m);

        var recordedBy = Guid.NewGuid();
        var childProgress = childActivity.RecordProgress(DataDate, 30.00m, null, recordedBy, ProgressSource.Manual, DataDate);
        var parentDirectProgress = parentDirectActivity.RecordProgress(DataDate, 99.00m, null, recordedBy, ProgressSource.Manual, DataDate);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.AddRange(parent, child);
            seedContext.Activities.AddRange(childActivity, parentDirectActivity);
            seedContext.ActivityProgressLogs.AddRange(childProgress, parentDirectProgress);
            await seedContext.SaveChangesAsync();
        }

        var dashboard = await RealHandlerFactory.GetDashboardAsync(factory, new GetDashboardQuery(project.Id, DataDate));

        Assert.True(dashboard.IsSuccess);
        // Subtree (child, 30%) wins; the parent's own direct 99% activity is excluded, not blended in
        // (there is no weight for "the parent's own work outside its subtree" to blend it against).
        Assert.Equal(30.00m, dashboard.Value.ProgressRollup.ProgressPercentage);
        Assert.Contains(parent.Id, dashboard.Value.ProgressRollup.MixedScopeWbsNodeIds);
        Assert.DoesNotContain(child.Id, dashboard.Value.ProgressRollup.MixedScopeWbsNodeIds);
    }

    [Fact]
    public async Task A_Level_Whose_Weights_Do_Not_Sum_To_100_Warns_But_Still_Returns_200_With_A_Computed_Rollup_Through_The_Real_Stack()
    {
        var factory = new TestDbContextFactory(TenantId);
        var project = Project.Create(
            TenantId, "Weight Warning Fixture", "WEIGHT-1", "Owner",
            DataDate.AddYears(-1), DataDate.AddYears(1), bac: 100_000.00m, dataDate: DataDate);

        // 60 + 30 = 90, not 100 - a real misconfiguration a PM could actually leave in place.
        var nodeA = new WBSNode(TenantId, project.Id, "1", "A", weightPercentage: 60.00m);
        var nodeB = new WBSNode(TenantId, project.Id, "2", "B", weightPercentage: 30.00m);
        var activityA = new Activity(TenantId, nodeA.Id, "A-1", "Work A", DataDate.AddMonths(-2), DataDate.AddDays(-1), 60, 1_000.00m);
        var activityB = new Activity(TenantId, nodeB.Id, "B-1", "Work B", DataDate.AddMonths(-2), DataDate.AddDays(-1), 60, 1_000.00m);

        var recordedBy = Guid.NewGuid();
        var progressA = activityA.RecordProgress(DataDate, 80.00m, null, recordedBy, ProgressSource.Manual, DataDate);
        var progressB = activityB.RecordProgress(DataDate, 20.00m, null, recordedBy, ProgressSource.Manual, DataDate);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.AddRange(nodeA, nodeB);
            seedContext.Activities.AddRange(activityA, activityB);
            seedContext.ActivityProgressLogs.AddRange(progressA, progressB);
            await seedContext.SaveChangesAsync();
        }

        var dashboard = await RealHandlerFactory.GetDashboardAsync(factory, new GetDashboardQuery(project.Id, DataDate));

        Assert.True(dashboard.IsSuccess); // warn, never block (S8-BE-02 DoD: "น้ำหนักไม่ครบ 100 → เตือน ไม่บล็อก").
        // The formula still divides by the *actual* weight sum (90), per evm-formulas.md:
        // (80x60 + 20x30) / 90 = (4,800 + 600) / 90 = 60.00.
        Assert.Equal(60.00m, dashboard.Value.ProgressRollup.ProgressPercentage);
        var warning = Assert.Single(dashboard.Value.ProgressRollup.WeightWarnings);
        Assert.Null(warning.WbsNodeId); // root/top-level siblings, not a named parent.
        Assert.Equal(2, warning.ChildCount);
        Assert.Equal(90.00m, warning.WeightSum);
    }
}
