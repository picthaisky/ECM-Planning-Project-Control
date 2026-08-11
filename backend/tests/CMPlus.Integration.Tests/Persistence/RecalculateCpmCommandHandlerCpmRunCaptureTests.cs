using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Cpm.Commands.RecalculateCpm;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// ADR-0019: proves, against a real <see cref="CmPlusDbContext"/> (not a fake/mock repository) that
/// <c>RecalculateCpmCommandHandler</c> actually persists a <see cref="CpmRun"/> - "a recalculation
/// actually writes a run rather than silently not capturing" - and, symmetrically, that a rejected
/// recalculation persists none at all.
///
/// <para>Deliberately EF Core InMemory (not the real-SQL-Server-only
/// <c>RecalculateCpmCommandHandlerIntegrationTests</c>): every scenario here is chosen specifically
/// because it never reaches <see cref="CpmScheduleRepository.SaveResultsAsync"/>'s raw
/// <c>ExecuteSqlInterpolatedAsync</c> Activity write-back, which InMemory cannot execute at all -
/// either because the project has zero activities (the raw-SQL block is skipped entirely - see
/// that method's own <c>if (results.Count &gt; 0)</c> guard) or because the handler returns before
/// ever calling <c>SaveResultsAsync</c> (a rejected graph, or a null actor). A genuine multi-activity
/// SUCCESSFUL recalculation's real-repository persistence is exercised only by
/// <c>RecalculateCpmCommandHandlerIntegrationTests</c> (real SQL Server, not executed in this
/// session - see its own remarks); the handler's own construction of a multi-activity
/// <see cref="CpmRun"/> is exercised in this session by
/// <c>CMPlus.Application.Tests.Features.Cpm.RecalculateCpmCommandHandlerTests</c> against a fake
/// repository. This file is the real-persistence proof for the two edge cases that do not require
/// SQL Server, run and passing in this session.</para>
/// </summary>
public class RecalculateCpmCommandHandlerCpmRunCaptureTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T09:00:00Z");

    private sealed class FixedDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedCurrentUserContext(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.PM;
    }

    private static CmPlusDbContext CreateContext(string databaseName, FakeTenantProvider tenantProvider)
    {
        var interceptor = new AppendOnlyGuardInterceptor();
        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseInMemoryDatabase(databaseName)
            // CpmScheduleRepository.SaveResultsAsync unconditionally opens a transaction (even on
            // the zero-activity path this file exercises), and InMemory does not support
            // transactions at all - by default it throws rather than silently ignoring, per
            // PaymentCertificateConcurrencyAndAuditIntegrationTests' own remarks on the identical
            // exception elsewhere in this suite. That prior case chose NOT to suppress it, because
            // suppressing there would have hidden a real semantic divergence in concurrent-conflict
            // behaviour under test. Nothing here tests concurrency or cross-statement rollback - only
            // "does SaveChangesAsync persist the CpmRun graph correctly" - so ignoring the warning
            // (the standard, Microsoft-documented technique the exception message itself points at)
            // is honest: InMemory's single SaveChangesAsync call is already atomic on its own,
            // transaction wrapper or not.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;

        return new CmPlusDbContext(options, tenantProvider);
    }

    private static (RecalculateCpmCommandHandler Handler, CpmScheduleRepository Repository) CreateHandler(
        CmPlusDbContext context, FakeTenantProvider tenantProvider, Guid? actorUserId, DateTimeOffset now)
    {
        var repository = new CpmScheduleRepository(
            context, tenantProvider, new FixedCurrentUserContext(actorUserId), new FixedDateTimeProvider(now));
        var handler = new RecalculateCpmCommandHandler(
            repository, tenantProvider, new FixedCurrentUserContext(actorUserId), new FixedDateTimeProvider(now));
        return (handler, repository);
    }

    [Fact]
    public async Task Handle_On_A_Zero_Activity_Project_Persists_A_Real_CpmRun_Row_With_No_Children()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var dataDate = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        Guid projectId;

        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(databaseName, tenantProvider);
            var project = Project.Create(
                tenantId, "Zero-Activity Project", $"P-CPMRUN-{Guid.NewGuid():N}", "Owner",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
                bac: 1_000_000m, dataDate: dataDate);
            context.Projects.Add(project);
            await context.SaveChangesAsync();
            projectId = project.Id;
        }

        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(databaseName, tenantProvider);
            var (handler, _) = CreateHandler(context, tenantProvider, actorUserId, Now);

            var result = await handler.Handle(new RecalculateCpmCommand(projectId), CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
            Assert.Equal(0, result.Value.ActivitiesProcessed);
        }

        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var verify = CreateContext(databaseName, tenantProvider);

            var run = await verify.CpmRuns.AsNoTracking().Include(r => r.Activities).Include(r => r.Relations)
                .SingleAsync(r => r.ProjectId == projectId);
            Assert.Equal(tenantId, run.TenantId);
            Assert.Equal(Now, run.CalculatedAt);
            Assert.Equal(dataDate, run.DataDate);
            Assert.Equal(0, run.ProjectDurationDays);
            Assert.Equal(actorUserId, run.TriggeredByUserId);
            Assert.Equal(CpmRunTrigger.Manual, run.Trigger);
            Assert.Empty(run.Activities);
            Assert.Empty(run.Relations);

            // Bonus: the same "exactly one summarizing AuditLog row" DoD
            // RecalculateCpmCommandHandlerIntegrationTests proves against real SQL Server, proven
            // here against InMemory instead - not doubled by the CpmRun insert (SuppressPerEntityAudit).
            Assert.Equal(1, await verify.AuditLogs.CountAsync(a => a.EntityId == projectId));
        }
    }

    [Fact]
    public async Task Handle_On_A_Cyclic_Graph_Persists_No_CpmRun_At_All()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        Guid projectId;
        Guid activityAId, activityBId;

        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(databaseName, tenantProvider);
            var project = Project.Create(
                tenantId, "Cyclic Graph Project", $"P-CPMRUN-CYCLE-{Guid.NewGuid():N}", "Owner",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
                bac: 1_000_000m, dataDate: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
            context.Projects.Add(project);
            var wbsNode = new WBSNode(tenantId, project.Id, "C1", "Civil Works", 100m);
            context.WBSNodes.Add(wbsNode);

            var a = new Activity(tenantId, wbsNode.Id, "A", "Activity A", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 5, 0m);
            var b = new Activity(tenantId, wbsNode.Id, "B", "Activity B", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 3, 0m);
            context.Activities.AddRange(a, b);
            context.ActivityRelations.AddRange(
                new ActivityRelation(tenantId, a.Id, b.Id, RelationType.FS, 0),
                new ActivityRelation(tenantId, b.Id, a.Id, RelationType.FS, 0)); // the cycle.
            await context.SaveChangesAsync();

            projectId = project.Id;
            activityAId = a.Id;
            activityBId = b.Id;
        }

        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(databaseName, tenantProvider);
            var (handler, _) = CreateHandler(context, tenantProvider, actorUserId, Now);

            var result = await handler.Handle(new RecalculateCpmCommand(projectId), CancellationToken.None);

            Assert.True(result.IsFailure);
        }

        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var verify = CreateContext(databaseName, tenantProvider);

            // The load-bearing negative assertion: a rejected recalculation must be silent, not just
            // on Activity (already covered by the fake-repository Application test) but on the real
            // CpmRuns table too - no partial/orphaned run from a failed calculation.
            Assert.Equal(0, await verify.CpmRuns.CountAsync());

            var reloadedA = await verify.Activities.AsNoTracking().SingleAsync(a => a.Id == activityAId);
            var reloadedB = await verify.Activities.AsNoTracking().SingleAsync(a => a.Id == activityBId);
            Assert.False(reloadedA.IsCritical);
            Assert.Null(reloadedA.TotalFloat);
            Assert.False(reloadedB.IsCritical);
            Assert.Null(reloadedB.TotalFloat);
        }
    }

    [Fact]
    public async Task Handle_With_No_Resolvable_Actor_Persists_No_CpmRun_And_Never_Loads_Or_Mutates_The_Graph()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        Guid projectId;

        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(databaseName, tenantProvider);
            var project = Project.Create(
                tenantId, "No Actor Project", $"P-CPMRUN-NOACTOR-{Guid.NewGuid():N}", "Owner",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
                bac: 1_000_000m, dataDate: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
            context.Projects.Add(project);
            var wbsNode = new WBSNode(tenantId, project.Id, "C1", "Civil Works", 100m);
            context.WBSNodes.Add(wbsNode);
            context.Activities.Add(new Activity(
                tenantId, wbsNode.Id, "A", "Activity A", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 5, 0m));
            await context.SaveChangesAsync();

            projectId = project.Id;
        }

        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(databaseName, tenantProvider);
            var (handler, _) = CreateHandler(context, tenantProvider, actorUserId: null, Now);

            var result = await handler.Handle(new RecalculateCpmCommand(projectId), CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(CMPlus.Application.Features.Cpm.CpmErrorCodes.ActorRequired, result.Error);
        }

        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var verify = CreateContext(databaseName, tenantProvider);
            Assert.Equal(0, await verify.CpmRuns.CountAsync());
        }
    }
}
