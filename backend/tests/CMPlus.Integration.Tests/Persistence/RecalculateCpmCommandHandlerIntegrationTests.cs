using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Cpm.Commands.RecalculateCpm;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S5-BE-04 DoD, verified end to end against a <b>real SQL Server</b> - not the EF Core InMemory
/// provider every other Integration test in this solution deliberately uses (see
/// <c>MigrationAndSeedSmokeTests</c>'s remarks on why InMemory is the norm here). This one cannot
/// follow that norm: <see cref="CpmScheduleRepository.SaveResultsAsync"/> issues a real
/// <c>UPDATE ... FROM ... JOIN OPENJSON(...)</c> T-SQL statement (the fix for the ~90-second
/// per-row-UPDATE defect measured against the S4-DB-02 10,000-activity dataset - see
/// <c>ICpmScheduleRepository</c>'s remarks and the Sprint 5 backend-developer report), which the
/// InMemory provider cannot execute at all (<c>ExecuteSqlInterpolatedAsync</c> is relational-provider-
/// only). Follows <c>MigrationAndSeedSmokeTests</c>'s own soft-skip convention: only runs when
/// <c>CMPLUS_TEST_MSSQL_CONNECTION</c>/<c>ConnectionStrings__CmPlusDatabase</c> points at a real
/// database (the same env vars that CI's <c>migration-smoke</c> job and this sprint's manual perf
/// run both use) - a normal <c>dotnet test</c> run with no database reachable stays fast/hermetic.
/// </summary>
public class RecalculateCpmCommandHandlerIntegrationTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("CMPLUS_TEST_MSSQL_CONNECTION")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__CmPlusDatabase");

    private sealed class FixedDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedCurrentUserContext(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.PM;
    }

    private static CmPlusDbContext CreateContext(
        string connectionString, FakeTenantProvider tenantProvider, Guid? actorUserId, DateTimeOffset now)
    {
        // The real AuditSaveChangesInterceptor, wired in exactly like production's
        // CmPlusDbContext registration (CMPlus.Infrastructure.DependencyInjection) - without it,
        // CmPlusDbContext.SuppressPerEntityAudit would have nothing to suppress and this test would
        // prove nothing about it (same reasoning as BatchRecordProgressCommandHandlerIntegrationTests).
        var interceptor = new AuditSaveChangesInterceptor(tenantProvider, new FixedCurrentUserContext(actorUserId), new FixedDateTimeProvider(now));
        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(interceptor)
            .Options;

        return new CmPlusDbContext(options, tenantProvider);
    }

    [Fact]
    public async Task Handle_Recomputes_The_Canonical_Network_Against_Real_SqlServer_And_Writes_Exactly_One_Summarizing_AuditLog_Row()
    {
        var connectionString = ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return; // Soft-skip: see class remarks. Only runs against a real SQL Server.
        }

        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var seedTime = DateTimeOffset.Parse("2026-07-29T08:00:00Z");
        var recalcTime = DateTimeOffset.Parse("2026-07-29T09:00:00Z");

        Project project;
        Activity a, b, c, d;
        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(connectionString, tenantProvider, actorUserId, seedTime);
            await context.Database.MigrateAsync();

            project = Project.Create(
                tenantId, "CPM Recalc Integration Test Project", $"P-CPM-IT-{Guid.NewGuid():N}", "Owner",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
                bac: 5_000_000m, dataDate: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
            context.Projects.Add(project);

            var wbsNode = new WBSNode(tenantId, project.Id, "C1", "Civil Works", 100m);
            context.WBSNodes.Add(wbsNode);

            a = new Activity(tenantId, wbsNode.Id, "A", "Activity A", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), DateTimeOffset.Parse("2026-07-06T00:00:00Z"), 5, 100_000m);
            b = new Activity(tenantId, wbsNode.Id, "B", "Activity B", DateTimeOffset.Parse("2026-07-06T00:00:00Z"), DateTimeOffset.Parse("2026-07-09T00:00:00Z"), 3, 100_000m);
            c = new Activity(tenantId, wbsNode.Id, "C", "Activity C", DateTimeOffset.Parse("2026-07-06T00:00:00Z"), DateTimeOffset.Parse("2026-07-12T00:00:00Z"), 6, 100_000m);
            d = new Activity(tenantId, wbsNode.Id, "D", "Activity D", DateTimeOffset.Parse("2026-07-12T00:00:00Z"), DateTimeOffset.Parse("2026-07-16T00:00:00Z"), 4, 100_000m);
            context.Activities.AddRange(a, b, c, d);

            context.ActivityRelations.AddRange(
                new ActivityRelation(tenantId, a.Id, b.Id, RelationType.FS, 0),
                new ActivityRelation(tenantId, a.Id, c.Id, RelationType.FS, 0),
                new ActivityRelation(tenantId, b.Id, d.Id, RelationType.FS, 0),
                new ActivityRelation(tenantId, c.Id, d.Id, RelationType.FS, 0));

            await context.SaveChangesAsync();
        }

        int auditLogCountBeforeRecalc;
        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(connectionString, tenantProvider, actorUserId, recalcTime);
            auditLogCountBeforeRecalc = await context.AuditLogs.CountAsync();
        }

        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(connectionString, tenantProvider, actorUserId, recalcTime);
            var repository = new CpmScheduleRepository(
                context, tenantProvider, new FixedCurrentUserContext(actorUserId), new FixedDateTimeProvider(recalcTime));
            var handler = new RecalculateCpmCommandHandler(repository);

            var result = await handler.Handle(new RecalculateCpmCommand(project.Id), CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
            Assert.Equal(4, result.Value.ActivitiesProcessed);
            Assert.Equal(3, result.Value.CriticalActivityCount);
            Assert.Equal(15, result.Value.ProjectDurationDays);
        }

        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var verify = CreateContext(connectionString, tenantProvider, actorUserId, recalcTime);
            var reloaded = await verify.Activities.AsNoTracking().ToDictionaryAsync(x => x.ActivityCode);

            Assert.True(reloaded["A"].IsCritical);
            Assert.Equal(0, reloaded["A"].TotalFloat);
            Assert.Equal(0, reloaded["A"].FreeFloat);

            Assert.False(reloaded["B"].IsCritical);
            Assert.Equal(3, reloaded["B"].TotalFloat);
            Assert.Equal(3, reloaded["B"].FreeFloat);

            Assert.True(reloaded["C"].IsCritical);
            Assert.Equal(0, reloaded["C"].TotalFloat);
            Assert.Equal(0, reloaded["C"].FreeFloat);

            Assert.True(reloaded["D"].IsCritical);
            Assert.Equal(0, reloaded["D"].TotalFloat);
            Assert.Equal(0, reloaded["D"].FreeFloat);

            var auditLogCountAfterRecalc = await verify.AuditLogs.CountAsync();
            // Exactly one new AuditLog row for the whole recalculation - not 4 (one per Activity) -
            // proving SuppressPerEntityAudit really suppressed the interceptor's own default
            // per-entity behaviour for the transaction's SaveChangesAsync call (the bulk Activity
            // update itself is raw SQL and never touches the interceptor at all).
            Assert.Equal(1, auditLogCountAfterRecalc - auditLogCountBeforeRecalc);
        }
    }
}
