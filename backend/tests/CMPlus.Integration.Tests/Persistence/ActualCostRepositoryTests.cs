using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>S8 (actual-cost.md §7.6/§9/§6.6, ADR-0013): <see cref="ActualCostRepository"/> against a
/// real <see cref="CmPlusDbContext"/> - the write-path existence checks and the append-only
/// insert.</summary>
public class ActualCostRepositoryTests
{
    private static Project CreateProject(Guid tenantId, string code) =>
        Project.Create(
            tenantId, $"Project {code}", code, "Owner",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 1_000_000m, dataDate: DateTimeOffset.UtcNow);

    [Fact]
    public async Task WbsNodeExistsAsync_Is_False_For_A_Node_Belonging_To_A_Different_Project()
    {
        // actual-cost.md §7.6: "cost posted to a WBS node in another project/tenant" must be
        // rejected - this is the check that makes that rejection possible.
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var projectA = CreateProject(tenantId, "A");
        var projectB = CreateProject(tenantId, "B");
        var nodeInB = new WBSNode(tenantId, projectB.Id, "1", "Structure", 100m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.AddRange(projectA, projectB);
            seedContext.WBSNodes.Add(nodeInB);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var repository = new ActualCostRepository(readContext);

        Assert.False(await repository.WbsNodeExistsAsync(projectA.Id, nodeInB.Id));
        Assert.True(await repository.WbsNodeExistsAsync(projectB.Id, nodeInB.Id));
    }

    [Fact]
    public async Task ActivityExistsAsync_Is_False_For_An_Activity_Belonging_To_A_Different_Project()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var projectA = CreateProject(tenantId, "A");
        var projectB = CreateProject(tenantId, "B");
        var nodeInB = new WBSNode(tenantId, projectB.Id, "1", "Structure", 100m);
        var activityInB = new Activity(
            tenantId, nodeInB.Id, "A-1", "Foundation",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), 30, 500_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.AddRange(projectA, projectB);
            seedContext.WBSNodes.Add(nodeInB);
            seedContext.Activities.Add(activityInB);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var repository = new ActualCostRepository(readContext);

        Assert.False(await repository.ActivityExistsAsync(projectA.Id, activityInB.Id));
        Assert.True(await repository.ActivityExistsAsync(projectB.Id, activityInB.Id));
    }

    [Fact]
    public async Task EntryExistsAsync_Is_False_For_An_Entry_Belonging_To_A_Different_Project()
    {
        var tenantId = Guid.NewGuid();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var entryInB = new ActualCostEntry(
            tenantId, projectB, null, null, CostCategory.Material, ActualCostEntryType.Accrual,
            ActualCostSource.ManualEntry, 250_000.00m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            Guid.NewGuid(), null, null, null, null, null, null, null, null, null);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.ActualCostEntries.Add(entryInB);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var repository = new ActualCostRepository(readContext);

        Assert.False(await repository.EntryExistsAsync(projectA, entryInB.Id));
        Assert.True(await repository.EntryExistsAsync(projectB, entryInB.Id));
    }

    [Fact]
    public async Task ClosedPeriodExistsOnOrAfterAsync_Is_True_Only_When_A_Snapshot_Covers_The_Incurred_Date()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var closedDataDate = DateTimeOffset.Parse("2026-03-31T00:00:00+07:00");

        using (var seedContext = factory.CreateContext())
        {
            seedContext.EvmPeriodSnapshots.Add(new EvmPeriodSnapshot(
                tenantId, projectId, closedDataDate,
                bac: 1_000_000m, pv: 400_000m, ev: 300_000m, ac: 350_000m,
                eacVariant: EacVariant.CpiBased, performanceFactor: 1.166667m, eac: 1_166_666.67m,
                etc: 816_666.67m, vac: -166_666.67m, createdAt: DateTimeOffset.UtcNow, createdByUserId: Guid.NewGuid()));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var repository = new ActualCostRepository(readContext);

        // A cost incurred inside the closed period (or exactly on its data date) -> true.
        Assert.True(await repository.ClosedPeriodExistsOnOrAfterAsync(projectId, closedDataDate.AddDays(-10)));
        Assert.True(await repository.ClosedPeriodExistsOnOrAfterAsync(projectId, closedDataDate));

        // A cost incurred after the closed period -> false (nothing has been closed that covers it).
        Assert.False(await repository.ClosedPeriodExistsOnOrAfterAsync(projectId, closedDataDate.AddDays(1)));
    }

    [Fact]
    public async Task AddAsync_Persists_The_Entry()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var entry = new ActualCostEntry(
            tenantId, projectId, null, null, CostCategory.Subcontract, ActualCostEntryType.Actual,
            ActualCostSource.ManualEntry, 640_000.00m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            Guid.NewGuid(), null, "INV-1", null, null, null, null, null, null, null);

        using (var writeContext = factory.CreateContext())
        {
            await new ActualCostRepository(writeContext).AddAsync(entry);
        }

        using var verifyContext = factory.CreateContext();
        var stored = await verifyContext.ActualCostEntries.SingleAsync(e => e.Id == entry.Id);
        Assert.Equal(640_000.00m, stored.Amount);
    }

    [Fact]
    public async Task AddAsync_Writes_Exactly_One_Audit_Row_Via_The_Default_Interceptor_Behaviour()
    {
        // CLAUDE.md: every mutating domain operation writes an audit log entry.
        // ActualCostRepository.AddAsync carries no bespoke audit code and never sets
        // CmPlusDbContext.SuppressPerEntityAudit (unlike e.g. BatchProgressRepository's bulk path)
        // - this proves the default one-row-per-changed-entity AuditSaveChangesInterceptor
        // behaviour genuinely applies here. TestDbContextFactory (used by every other test in this
        // file) does not wire the interceptor in at all, so this one test builds its own
        // interceptor-attached context directly, mirroring AuditSaveChangesInterceptorTests'
        // own pattern.
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var tenantProvider = new FakeTenantProvider(tenantId);

        CmPlusDbContext CreateInterceptedContext()
        {
            var interceptor = new AuditSaveChangesInterceptor(
                tenantProvider, new FixedCurrentUserContextForAudit(actorUserId), new FixedClockForAudit(DateTimeOffset.UtcNow));
            var options = new DbContextOptionsBuilder<CmPlusDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(interceptor)
                .Options;
            return new CmPlusDbContext(options, tenantProvider);
        }

        var entry = new ActualCostEntry(
            tenantId, projectId, null, null, CostCategory.Subcontract, ActualCostEntryType.Actual,
            ActualCostSource.ManualEntry, 640_000.00m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            Guid.NewGuid(), null, "INV-1", null, null, null, null, null, null, null);

        using (var writeContext = CreateInterceptedContext())
        {
            await new ActualCostRepository(writeContext).AddAsync(entry);
        }

        using var verifyContext = CreateInterceptedContext();
        var auditRows = await verifyContext.AuditLogs
            .Where(a => a.EntityName == nameof(ActualCostEntry) && a.EntityId == entry.Id)
            .ToListAsync();
        Assert.Single(auditRows);
        Assert.Equal(AuditAction.Created, auditRows[0].Action);
        Assert.Equal(actorUserId, auditRows[0].UserId);
    }

    private sealed class FixedCurrentUserContextForAudit(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.PM;
    }

    private sealed class FixedClockForAudit(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
