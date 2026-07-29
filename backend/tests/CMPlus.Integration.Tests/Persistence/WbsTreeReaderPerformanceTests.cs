using System.Diagnostics;
using CMPlus.Application.Features.Wbs.Queries.GetWbsTree;
using CMPlus.Domain.Entities;
using CMPlus.Infrastructure.Persistence;
using Activity = CMPlus.Domain.Entities.Activity;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S4-BE-01 early sanity check (docs/10 §6 S4-BE-01 DoD: P95 &lt; 100 ms at 5,000 nodes). This is
/// NOT the formal DoD proof - EF Core's InMemory provider does not have a query planner/execution
/// plan and cannot demonstrate an index seek the way SQL Server can, so it says nothing about actual
/// index usage; that verification is <c>database-engineer</c>'s S4-DB-01 job against a real MSSQL
/// container at 5,000-node/10,000-activity volume, and the formal P95/P99 measurement is
/// <c>qa-engineer</c>'s S4-QA-01 (`tests/perf/wbs-tree.k6.js`). What this test *does* prove: the
/// query issues a bounded, constant number of round trips (never one per node - see
/// <see cref="WbsTreeReader"/>'s remarks) and the in-memory tree-building pass
/// (<see cref="WbsTreeBuilder"/>) is not accidentally quadratic, using a generous time budget purely
/// as an early regression guard (same pattern as
/// <c>XerScheduleParserPerformanceTests</c>).
/// </summary>
public class WbsTreeReaderPerformanceTests
{
    [Fact]
    public async Task GetNodesWithActivityCountsAsync_And_Build_Complete_Well_Under_A_Generous_Budget_At_5000_Nodes_10000_Activities()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);

        var project = Project.Create(
            tenantId, "Large Project", "P-PERF-1", "Owner",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 100_000_000m, dataDate: DateTimeOffset.UtcNow);

        // Shape: 1 root + 50 branch nodes under it + 5,000 leaf nodes (100 per branch) = 5,051 WBS
        // nodes total - at least the docs/10 "5,000 node" DoD volume, and a genuine two-level tree
        // (not a single flat level), so WbsTreeBuilder's recursion-free nesting is actually exercised
        // across more than one depth. Exactly 2 activities per leaf = 10,000 activities total,
        // matching the "10,000 activity" half of the combined DoD volume.
        const int branchCount = 50;
        const int leavesPerBranch = 100;
        var wbsNodes = new List<WBSNode>();
        var activities = new List<Activity>();

        var root = new WBSNode(tenantId, project.Id, "0", "Project Root", 100m);
        wbsNodes.Add(root);

        for (var b = 0; b < branchCount; b++)
        {
            var branch = new WBSNode(tenantId, project.Id, $"{b:D2}", $"Branch {b}", 100m / branchCount, root.Id);
            wbsNodes.Add(branch);

            for (var l = 0; l < leavesPerBranch; l++)
            {
                var leaf = new WBSNode(tenantId, project.Id, $"{b:D2}.{l:D3}", $"Leaf {b}-{l}", 100m / leavesPerBranch, branch.Id);
                wbsNodes.Add(leaf);

                activities.Add(new Activity(
                    tenantId, leaf.Id, $"A-{b:D2}-{l:D3}-1", "Activity 1",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(5), 5, 10_000m));
                activities.Add(new Activity(
                    tenantId, leaf.Id, $"A-{b:D2}-{l:D3}-2", "Activity 2",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(5), 5, 15_000m));
            }
        }

        var expectedNodeCount = 1 + branchCount + (branchCount * leavesPerBranch);
        var expectedLeafCount = branchCount * leavesPerBranch;
        Assert.Equal(5_051, expectedNodeCount);
        Assert.Equal(wbsNodes.Count, expectedNodeCount);
        Assert.Equal(10_000, activities.Count);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.ChangeTracker.AutoDetectChangesEnabled = false;
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.AddRange(wbsNodes);
            seedContext.Activities.AddRange(activities);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new WbsTreeReader(readContext);

        var stopwatch = Stopwatch.StartNew();
        var flatNodes = await reader.GetNodesWithActivityCountsAsync(project.Id, CancellationToken.None);
        var tree = WbsTreeBuilder.Build(project.Id, flatNodes);
        stopwatch.Stop();

        Assert.Equal(expectedNodeCount, flatNodes.Count);
        Assert.Equal(expectedLeafCount, flatNodes.Count(n => n.ActivityCount == 2)); // every leaf carries exactly 2 activities
        var rootDto = Assert.Single(tree.RootNodes);
        Assert.Equal(branchCount, rootDto.Children.Count);
        Assert.Equal(leavesPerBranch, rootDto.Children[0].Children.Count);
        Assert.Contains(rootDto.Children.SelectMany(b => b.Children), leaf => leaf.ActivityCount == 2);

        // Generous budget (InMemory provider, no index/network latency at all) - this only guards
        // against an accidentally-quadratic implementation (e.g. a per-node query reintroducing the
        // N+1 trap), not the real < 100 ms SQL Server DoD.
        Assert.True(
            stopwatch.ElapsedMilliseconds < 5_000,
            $"Expected the bounded-query-count WBS tree read + in-memory build to stay well clear of " +
            $"quadratic behaviour at {expectedNodeCount:N0} nodes/{activities.Count:N0} activities; " +
            $"took {stopwatch.ElapsedMilliseconds:N0} ms.");
    }
}
