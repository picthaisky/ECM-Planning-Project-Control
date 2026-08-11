using CMPlus.Domain.Entities;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

// Deliberately NOT "CMPlus.Integration.Tests.Baseline" - see
// BaselineActivationOrderingSqliteTests' own top-of-namespace remarks for why (CS0118 collision
// with TenantIsolationTests' bare `Baseline` usage under the shared ancestor namespace).
namespace CMPlus.Integration.Tests.BaselineComparison;

/// <summary>
/// S14-QA-01: closes a gap the handler-level fixture tests (<c>CompareBaselineQueryHandlerTests</c>)
/// cannot reach - those feed <see cref="Application.Abstractions.BaselineActivityComparisonRow"/>
/// rows to the handler directly via a hand-written fake, so they prove the handler's DTO-mapping
/// logic (variance sign, the removed-row shape) but never actually exercise
/// <see cref="BaselineComparisonReader.GetActivityComparisonAsync"/>'s own LINQ - a snapshot-anchored
/// left join (<c>from s in ... join a in ... into ... from a in ...DefaultIfEmpty()</c>) - against a
/// real EF Core provider. This file runs that exact query against a real (InMemory)
/// <see cref="CmPlusDbContext"/> for the two shapes this task calls out explicitly: an activity added
/// to the project after the baseline was captured (never in the snapshot set), and a baseline
/// snapshot whose <c>ActivityId</c> has no live <see cref="Activity"/> counterpart (the left join's
/// null side - <c>IBaselineComparisonReader</c>'s own remarks note this is not reachable through any
/// UI flow today, since no delete path exists for <see cref="Activity"/> anywhere in this codebase,
/// but the query must still behave correctly if a row ever gets into that shape by any other means -
/// a future delete feature, a data-migration, a manual DB fix).
/// </summary>
public class BaselineComparisonReaderShapeTests
{
    private static CmPlusDbContext CreateContext(string databaseName, FakeTenantProvider tenantProvider)
    {
        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new CmPlusDbContext(options, tenantProvider);
    }

    [Fact]
    public async Task An_Activity_Added_After_The_Baseline_Was_Captured_Never_Appears_In_The_Comparison()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var tenantProvider = new FakeTenantProvider(tenantId);
        Guid baselineId;
        Guid originalActivityId;

        await using (var context = CreateContext(databaseName, tenantProvider))
        {
            var node = new WBSNode(tenantId, projectId, "C-1", "Structure", 100m);
            context.WBSNodes.Add(node);

            var originalActivity = new Activity(
                tenantId, node.Id, "A-ORIGINAL", "Captured before baseline",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 500_000.00m);
            context.Activities.Add(originalActivity);
            originalActivityId = originalActivity.Id;

            var baseline = Domain.Entities.Baseline.Capture(
                tenantId, projectId, "BL-0", DateTimeOffset.UtcNow, Guid.NewGuid(), 500_000.00m,
                [new BaselineActivitySnapshotInput(
                    originalActivityId, originalActivity.PlannedStart, originalActivity.PlannedFinish,
                    originalActivity.DurationDays, originalActivity.BudgetCost)]);
            context.Baselines.Add(baseline);
            baselineId = baseline.Id;

            // The new activity: added to the project AFTER the baseline snapshot above was built -
            // no BaselineActivitySnapshot row references it at all.
            var newActivity = new Activity(
                tenantId, node.Id, "A-NEW", "Added after baseline capture",
                DateTimeOffset.Parse("2026-02-01T00:00:00Z"), DateTimeOffset.Parse("2026-02-10T00:00:00Z"), 9, 200_000.00m);
            context.Activities.Add(newActivity);

            await context.SaveChangesAsync();
        }

        await using var verifyContext = CreateContext(databaseName, tenantProvider);
        var reader = new BaselineComparisonReader(verifyContext);

        var rows = await reader.GetActivityComparisonAsync(projectId, baselineId);

        // Snapshot-anchored: exactly one row (the ORIGINAL activity), the new one is invisible by
        // construction - confirming that is genuinely what the real query does, not merely what the
        // handler's own DTO-mapping fixtures assume.
        var row = Assert.Single(rows);
        Assert.Equal(originalActivityId, row.ActivityId);
        Assert.Equal("A-ORIGINAL", row.CurrentActivityCode);
    }

    [Fact]
    public async Task A_Baseline_Snapshot_Whose_Activity_No_Longer_Exists_Still_Returns_A_Row_With_Only_The_Baseline_Side_Populated()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var tenantProvider = new FakeTenantProvider(tenantId);
        Guid baselineId;
        var orphanActivityId = Guid.NewGuid(); // never inserted into Activities - simulates a delete.

        await using (var context = CreateContext(databaseName, tenantProvider))
        {
            var baseline = Domain.Entities.Baseline.Capture(
                tenantId, projectId, "BL-0", DateTimeOffset.UtcNow, Guid.NewGuid(), 250_000.00m,
                [new BaselineActivitySnapshotInput(
                    orphanActivityId, DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    DateTimeOffset.Parse("2026-01-10T00:00:00Z"), 9, 250_000.00m)]);
            context.Baselines.Add(baseline);
            baselineId = baseline.Id;

            await context.SaveChangesAsync();
        }

        await using var verifyContext = CreateContext(databaseName, tenantProvider);
        var reader = new BaselineComparisonReader(verifyContext);

        var rows = await reader.GetActivityComparisonAsync(projectId, baselineId);

        var row = Assert.Single(rows);
        Assert.Equal(orphanActivityId, row.ActivityId);
        Assert.Equal(250_000.00m, row.BaselineBudgetCost);
        Assert.Null(row.CurrentActivityCode);
        Assert.Null(row.CurrentName);
        Assert.Null(row.CurrentPlannedStart);
        Assert.Null(row.CurrentPlannedFinish);
        Assert.Null(row.CurrentDurationDays);
        Assert.Null(row.CurrentBudgetCost);
        Assert.Null(row.CurrentIsCritical);
    }
}
