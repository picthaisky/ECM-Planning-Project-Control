using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// ADR-0019 (domain-rules.md weather-eot §4.1): <see cref="CpmRunHistoryReader.GetGoverningRunAsync"/>
/// implements $r(d) = \arg\max\{r.\mathrm{CalculatedAt} \le d\}$ - the piece of "was this activity
/// critical on the stoppage date" that the not-yet-built EotEvaluator (S11-BE-02) will consume.
/// Entirely ordinary LINQ (no raw SQL), so this runs against the EF Core InMemory provider like the
/// rest of this project's Integration tests (unlike <c>RecalculateCpmCommandHandlerIntegrationTests</c>,
/// which needs a real SQL Server for the Activity write-back's raw <c>OPENJSON</c> statement).
/// </summary>
public class CpmRunHistoryReaderTests
{
    private static CmPlusDbContext CreateContext(string databaseName, FakeTenantProvider tenantProvider)
    {
        var options = new DbContextOptionsBuilder<CmPlusDbContext>().UseInMemoryDatabase(databaseName).Options;
        return new CmPlusDbContext(options, tenantProvider);
    }

    private static CpmRun CreateRun(Guid tenantId, Guid projectId, DateTimeOffset calculatedAt, int projectDurationDays) =>
        CpmRun.Capture(
            tenantId, projectId, calculatedAt, dataDate: null, projectDurationDays, Guid.NewGuid(), CpmRunTrigger.Manual,
            [], []);

    /// <summary>The load-bearing case: several runs exist on both sides of the query date - the
    /// reader must pick the most recent one that is still <c>&lt;=</c> the date, never the closest
    /// overall and never the latest run unconditionally.</summary>
    [Fact]
    public async Task GetGoverningRunAsync_Picks_The_Most_Recent_Run_At_Or_Before_The_Date_When_Several_Exist_On_Both_Sides()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        var runJuly1 = CreateRun(tenantId, projectId, DateTimeOffset.Parse("2026-07-01T08:00:00Z"), 10);
        var runJuly15 = CreateRun(tenantId, projectId, DateTimeOffset.Parse("2026-07-15T08:00:00Z"), 12);
        var runAugust1 = CreateRun(tenantId, projectId, DateTimeOffset.Parse("2026-08-01T08:00:00Z"), 15);
        var runAugust20 = CreateRun(tenantId, projectId, DateTimeOffset.Parse("2026-08-20T08:00:00Z"), 20);

        using (var seedContext = CreateContext(databaseName, tenantProvider))
        {
            // Seeded out of chronological order deliberately - the reader must sort correctly, not
            // rely on insertion order.
            seedContext.CpmRuns.AddRange(runAugust20, runJuly1, runAugust1, runJuly15);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = CreateContext(databaseName, tenantProvider);
        var reader = new CpmRunHistoryReader(readContext);

        // A stoppage on 2026-07-20 falls strictly between runJuly15 and runAugust1 - the governing
        // run must be runJuly15 (the most recent one at or before the date), not runAugust1 (closer
        // in absolute terms but AFTER the date) and not runAugust20 (the latest run overall).
        var governing = await reader.GetGoverningRunAsync(projectId, DateTimeOffset.Parse("2026-07-20T00:00:00Z"));

        Assert.NotNull(governing);
        Assert.Equal(runJuly15.Id, governing!.Id);
        Assert.Equal(12, governing.ProjectDurationDays);
    }

    [Fact]
    public async Task GetGoverningRunAsync_Returns_The_Exact_Run_When_CalculatedAt_Equals_The_Date_Inclusive()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();
        var exactMoment = DateTimeOffset.Parse("2026-07-15T08:00:00Z");

        using (var seedContext = CreateContext(databaseName, tenantProvider))
        {
            seedContext.CpmRuns.Add(CreateRun(tenantId, projectId, exactMoment, 12));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = CreateContext(databaseName, tenantProvider);
        var reader = new CpmRunHistoryReader(readContext);

        var governing = await reader.GetGoverningRunAsync(projectId, exactMoment);

        Assert.NotNull(governing);
        Assert.Equal(exactMoment, governing!.CalculatedAt);
    }

    [Fact]
    public async Task GetGoverningRunAsync_Returns_Null_When_The_Date_Is_Before_Every_Run()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using (var seedContext = CreateContext(databaseName, tenantProvider))
        {
            seedContext.CpmRuns.Add(CreateRun(tenantId, projectId, DateTimeOffset.Parse("2026-08-01T08:00:00Z"), 15));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = CreateContext(databaseName, tenantProvider);
        var reader = new CpmRunHistoryReader(readContext);

        var governing = await reader.GetGoverningRunAsync(projectId, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        Assert.Null(governing);
    }

    [Fact]
    public async Task GetGoverningRunAsync_Returns_Null_When_The_Project_Has_No_Runs_At_All()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using var readContext = CreateContext(databaseName, tenantProvider);
        var reader = new CpmRunHistoryReader(readContext);

        var governing = await reader.GetGoverningRunAsync(Guid.NewGuid(), DateTimeOffset.Parse("2026-08-10T00:00:00Z"));

        Assert.Null(governing);
    }

    [Fact]
    public async Task GetGoverningRunAsync_Ignores_Runs_From_A_Different_Project_In_The_Same_Tenant()
    {
        var tenantId = Guid.NewGuid();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using (var seedContext = CreateContext(databaseName, tenantProvider))
        {
            // A later, otherwise-perfectly-matching run belonging to a DIFFERENT project must never
            // leak into project A's governing-run answer.
            seedContext.CpmRuns.Add(CreateRun(tenantId, projectA, DateTimeOffset.Parse("2026-07-01T08:00:00Z"), 10));
            seedContext.CpmRuns.Add(CreateRun(tenantId, projectB, DateTimeOffset.Parse("2026-07-10T08:00:00Z"), 999));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = CreateContext(databaseName, tenantProvider);
        var reader = new CpmRunHistoryReader(readContext);

        var governing = await reader.GetGoverningRunAsync(projectA, DateTimeOffset.Parse("2026-08-01T00:00:00Z"));

        Assert.NotNull(governing);
        Assert.Equal(10, governing!.ProjectDurationDays);
    }

    /// <summary>ADR-0002: a cross-tenant run must be invisible even if the project id happens to
    /// match, exactly as every other tenant-scoped read in this codebase already asserts.</summary>
    [Fact]
    public async Task GetGoverningRunAsync_Ignores_Runs_From_A_Different_Tenant_Even_With_The_Same_ProjectId()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var sharedProjectId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        using (var seedContext = CreateContext(databaseName, new FakeTenantProvider(tenantB)))
        {
            seedContext.CpmRuns.Add(CreateRun(tenantB, sharedProjectId, DateTimeOffset.Parse("2026-07-01T08:00:00Z"), 999));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = CreateContext(databaseName, new FakeTenantProvider(tenantA));
        var reader = new CpmRunHistoryReader(readContext);

        var governing = await reader.GetGoverningRunAsync(sharedProjectId, DateTimeOffset.Parse("2026-08-01T00:00:00Z"));

        Assert.Null(governing);
    }

    [Fact]
    public async Task GetGoverningRunAsync_Includes_The_Runs_Activities_And_Relations()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();
        var activityAId = Guid.NewGuid();
        var activityBId = Guid.NewGuid();

        using (var seedContext = CreateContext(databaseName, tenantProvider))
        {
            var run = CpmRun.Capture(
                tenantId, projectId, DateTimeOffset.Parse("2026-08-01T08:00:00Z"), null, 8, Guid.NewGuid(), CpmRunTrigger.Manual,
                [
                    new CpmRunActivityInput(activityAId, 5, 0, 5, 0, 5, 0, 0, true),
                    new CpmRunActivityInput(activityBId, 3, 5, 8, 5, 8, 0, 0, true),
                ],
                [new CpmRunRelationInput(activityAId, activityBId, RelationType.FS, 0)]);
            seedContext.CpmRuns.Add(run);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = CreateContext(databaseName, tenantProvider);
        var reader = new CpmRunHistoryReader(readContext);

        var governing = await reader.GetGoverningRunAsync(projectId, DateTimeOffset.Parse("2026-08-10T00:00:00Z"));

        Assert.NotNull(governing);
        Assert.Equal(2, governing!.Activities.Count);
        Assert.Single(governing.Relations);
    }
}
