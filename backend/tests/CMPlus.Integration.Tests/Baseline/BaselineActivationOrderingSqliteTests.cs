using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Configurations;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Deliberately NOT "CMPlus.Integration.Tests.Baseline" - see
// CMPlus.Application.Tests.Features.Baseline.BaselineTestFakes' own remarks on why a namespace
// segment literally named "Baseline" makes every bare `Baseline` reference elsewhere in an
// enclosing namespace resolve to the namespace instead of Domain.Entities.Baseline (this file's
// own folder collided with CMPlus.Integration.Tests.Persistence.TenantIsolationTests' existing
// bare `Baseline` usage under the shared CMPlus.Integration.Tests ancestor namespace - confirmed
// by a real CS0118 build break, not merely inferred from the sibling project's comment).
namespace CMPlus.Integration.Tests.BaselineOrdering;

/// <summary>
/// S14-QA-01: independent verification of the single-active-baseline DB-level invariant
/// ("ตั้ง active สองชุดพร้อมกันไม่ได้") against a real, constraint-enforcing relational engine -
/// closing the exact gap <c>ActivateBaselineCommandHandler</c>/<c>BaselineRepository.TryActivateAsync</c>'s
/// own remarks and <c>docs/perf/s14-baseline-storage.md</c> §2 flag explicitly: "no test that runs
/// in this environment can observe statement ordering... the ordering proof is the SQLite probe, not
/// any test in this repository." That SQLite probe was a scratch, session-local harness that
/// re-implemented the handler's mutation order rather than calling the shipped code - this test
/// calls the actual shipped <see cref="BaselineRepository.TryActivateAsync"/> method (via the real
/// <see cref="IBaselineRepository"/> interface, exactly as <c>ActivateBaselineCommandHandler</c>
/// does) against a real on-disk-format SQLite engine with the production
/// <see cref="Configurations.BaselineConfiguration"/> filtered unique index actually materialized,
/// so it is committed, permanent, constraint-enforcing evidence rather than a one-off report.
///
/// <para>Sqlite, not SQL Server, for the same reason <c>docs/perf/s14-baseline-storage.md</c> used
/// it: Docker cannot start in this environment (no Administrator), so no real SQL Server is
/// reachable. Sqlite is a genuine, non-deferred-check relational engine (unlike EF Core InMemory,
/// which does not enforce unique indexes at all - see <c>CmPlusDbContext</c>'s remarks and the
/// second bullet of the 2026-08-10 lessons-learned entry on trusting InMemory's silence). What this
/// still does not prove: that SQL Server's own statement-batching heuristic behaves identically to
/// SQLite's (docs/perf/s14-baseline-storage.md §2.1 explicitly leaves that as [INFERRED] pending a
/// real SQL Server run) - this test closes the "no test observes the ordering at all" gap, not the
/// separate "does this generalize to SQL Server" question.</para>
/// </summary>
public class BaselineActivationOrderingSqliteTests
{
    private sealed class FixedDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedCurrentUserContext(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.PM;
    }

    /// <summary>
    /// Schema-only DbContext used exclusively for <c>EnsureCreatedAsync</c>, mapping only
    /// <see cref="Domain.Entities.Baseline"/>/<see cref="BaselineActivitySnapshot"/> via the real,
    /// unmodified production <see cref="BaselineConfiguration"/>/<see cref="BaselineActivitySnapshotConfiguration"/>
    /// classes - never used for any actual read/write in the tests below (those go through the real
    /// <see cref="CmPlusDbContext"/>/<see cref="BaselineRepository"/>). This split exists solely
    /// because <see cref="CmPlusDbContext.OnModelCreating"/> maps the whole application's schema, and
    /// one unrelated table (<c>AuditLogs</c>, <c>HasColumnType("nvarchar(max)")</c>) does not parse
    /// under Sqlite's column-type grammar (a genuine, narrow provider-portability gap in
    /// <c>AuditLogConfiguration</c>, out of scope for this QA-authored test to fix - see the
    /// backend-developer report) - <c>Database.EnsureCreatedAsync()</c> has no per-entity filter, so
    /// creating the real <see cref="CmPlusDbContext"/>'s full model against Sqlite fails before this
    /// test's actual subject (the Baseline tables) is even reached. Once the two tables exist with
    /// the exact column/index shape the production configuration classes describe, the real
    /// <see cref="CmPlusDbContext"/> can query/write them normally over the same connection - EF does
    /// not re-validate the rest of the schema on every query, only the tables a given query actually
    /// touches, and <see cref="BaselineRepository.TryActivateAsync"/>/<see cref="BaselineRepository.FindAsync"/>/
    /// <see cref="BaselineRepository.FindActiveAsync"/> touch only <c>Baselines</c>.
    /// </summary>
    private sealed class BaselineSchemaOnlyDbContext(DbContextOptions<BaselineSchemaOnlyDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfiguration(new BaselineConfiguration());
            modelBuilder.ApplyConfiguration(new BaselineActivitySnapshotConfiguration());
        }
    }

    private static async Task CreateBaselineSchemaAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<BaselineSchemaOnlyDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var schemaContext = new BaselineSchemaOnlyDbContext(options);
        await schemaContext.Database.EnsureCreatedAsync();
    }

    private static CmPlusDbContext CreateContext(SqliteConnection connection, FakeTenantProvider tenantProvider)
    {
        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseSqlite(connection)
            .Options;

        return new CmPlusDbContext(options, tenantProvider);
    }

    /// <summary>
    /// Mirrors <c>ActivateBaselineCommandHandler.Handle</c>'s exact call order verbatim (load
    /// <paramref name="targetId"/> first, load the previous active baseline second, then delegate to
    /// <see cref="IBaselineRepository.TryActivateAsync"/>) so this is a faithful exercise of the
    /// shipped production code path, not a hand-rolled reproduction of it.
    /// </summary>
    private static async Task<bool> ActivateAsync(
        CmPlusDbContext context, FakeTenantProvider tenantProvider, Guid projectId, Guid targetId)
    {
        var repository = new BaselineRepository(
            context, tenantProvider, new FixedCurrentUserContext(Guid.NewGuid()), new FixedDateTimeProvider(DateTimeOffset.UtcNow));

        var target = await repository.FindAsync(targetId);
        Assert.NotNull(target);

        if (target!.IsActive)
        {
            return true;
        }

        var previousActive = await repository.FindActiveAsync(projectId);
        return await repository.TryActivateAsync(target, previousActive);
    }

    /// <summary>
    /// The canary this task's brief explicitly asks for: with the shipped two-<c>SaveChangesAsync</c>
    /// fix in place, every one of 30 independent activate-a-second-baseline trials against a real
    /// filtered unique index must succeed, and at every point in time exactly one baseline is active
    /// per project - never zero, never two.
    /// </summary>
    [Fact]
    public async Task TryActivateAsync_Never_Violates_The_Single_Active_Baseline_Unique_Index_Across_30_Trials()
    {
        const int trialCount = 30;
        var successCount = 0;

        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await CreateBaselineSchemaAsync(connection);

        for (var trial = 0; trial < trialCount; trial++)
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new FakeTenantProvider(tenantId);
            var projectId = Guid.NewGuid();

            Guid originalId;
            Guid revisedId;
            await using (var seedContext = CreateContext(connection, tenantProvider))
            {
                var original = Domain.Entities.Baseline.Capture(
                    tenantId, projectId, $"BL-0-{trial}", DateTimeOffset.UtcNow, Guid.NewGuid(), 1_000_000.00m, []);
                original.Activate();
                var revised = Domain.Entities.Baseline.Capture(
                    tenantId, projectId, $"BL-1-{trial}", DateTimeOffset.UtcNow, Guid.NewGuid(), 1_050_000.00m, []);

                seedContext.Baselines.AddRange(original, revised);
                await seedContext.SaveChangesAsync();
                originalId = original.Id;
                revisedId = revised.Id;
            }

            await using var activateContext = CreateContext(connection, tenantProvider);
            var succeeded = await ActivateAsync(activateContext, tenantProvider, projectId, revisedId);

            await using var verifyContext = CreateContext(connection, tenantProvider);
            var activeCount = await verifyContext.Baselines.CountAsync(b => b.ProjectId == projectId && b.IsActive);

            // Never zero, never two, regardless of whether this trial's activation itself succeeded
            // or reported a (handled) concurrency conflict - the invariant is about the persisted
            // state, not about every individual call succeeding.
            Assert.Equal(1, activeCount);

            if (succeeded)
            {
                successCount++;
                var revisedIsActive = await verifyContext.Baselines
                    .Where(b => b.Id == revisedId).Select(b => b.IsActive).SingleAsync();
                Assert.True(revisedIsActive);
            }

            _ = originalId; // retained for readability of the seeded pair; not asserted directly.
        }

        // The shipped fix (two sequential SaveChangesAsync calls inside one transaction) removes the
        // non-determinism the single-batch shape had (docs/perf/s14-baseline-storage.md §2: a ~50/50
        // split under the single-SaveChanges shape) - every trial here has a previous active baseline
        // to race against, so 30/30 is the expected, asserted outcome for the current code.
        Assert.Equal(trialCount, successCount);
    }

    /// <summary>
    /// <b>The genuine two-request race the 30-trial single-process harness above cannot reach:</b> that
    /// test's own loop only ever has one <see cref="CmPlusDbContext"/> "in flight" activating a
    /// baseline at a time (seed, then activate, then verify - strictly sequential), so it never
    /// exercises the case two independent requests each load their own state <b>before either
    /// commits</b>. This test reproduces exactly that: two independent <see cref="BaselineRepository"/>
    /// instances (mirroring two concurrent HTTP requests against <c>ActivateBaselineCommandHandler</c>)
    /// both call <see cref="BaselineRepository.FindAsync"/>/<see cref="BaselineRepository.FindActiveAsync"/>
    /// and observe the <em>same</em> pre-race active baseline, before either one calls
    /// <see cref="BaselineRepository.TryActivateAsync"/> at all - then activate two <b>different</b>
    /// target baselines in sequence (request A runs to completion and commits first, exactly as the
    /// task brief specifies: "two contexts, both reading the pre-race active baseline, then activating
    /// in sequence"). Request B's own <c>previousActive</c> is now a stale row (already deactivated by
    /// A) and its own target is a different baseline than the one A just activated - by the time B's
    /// own activate <c>SaveChangesAsync</c> runs, the real `(TenantId, ProjectId) WHERE IsActive = 1`
    /// index already has A's target as the active row, so B's attempt collides with it. This is a
    /// genuine <c>DbUpdateException</c> (not <c>DbUpdateConcurrencyException</c> - no optimistic
    /// concurrency token is involved anywhere in this sequence; every individual
    /// <c>SaveChangesAsync</c> call above updates exactly the one row it targeted), which is exactly
    /// the shape <c>TryActivateAsync</c>'s previous <c>catch (DbUpdateConcurrencyException)</c>-only
    /// handling did not catch - qa-engineer reproduced this against the already-ordering-fixed code
    /// with two <c>DbContext</c>s racing and found it escapes as an unhandled 500 instead of the
    /// graceful conflict result the two-phase design otherwise returns for every other failure shape.
    ///
    /// <para><b>Mutation check (this task's brief, "a fix without the red-first proof is a guess"):</b>
    /// with <see cref="BaselineRepository.TryActivateAsync"/>'s widened
    /// <c>catch (DbUpdateException ex) when (UniqueIndexViolationClassifier.IsUniqueConstraintViolation(ex))</c>
    /// branch reverted back to only <c>catch (DbUpdateConcurrencyException)</c>, this test fails: the
    /// raw <c>DbUpdateException</c> from request B's activate <c>SaveChangesAsync</c> escapes
    /// unhandled straight out of the <c>await repoB.TryActivateAsync(...)</c> call below (deliberately
    /// not wrapped in a local try/catch - an escaping exception must fail *this test*, not be quietly
    /// absorbed by it), so xUnit reports the test itself as failed/erroring rather than the intended
    /// clean <see langword="false"/>. With the fix in place, the exact same sequence returns
    /// <see langword="false"/> and the persisted state is exactly one active baseline (A's), never
    /// zero, never two.</para>
    /// </summary>
    [Fact]
    public async Task TryActivateAsync_Reports_A_Clean_Failure_Not_An_Escaped_Exception_When_Two_Concurrent_Requests_Race()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await CreateBaselineSchemaAsync(connection);

        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var projectId = Guid.NewGuid();

        Guid originalId;
        Guid revisedAId;
        Guid revisedBId;
        await using (var seedContext = CreateContext(connection, tenantProvider))
        {
            var original = Domain.Entities.Baseline.Capture(
                tenantId, projectId, "BL-0", DateTimeOffset.UtcNow, Guid.NewGuid(), 1_000_000.00m, []);
            original.Activate();
            var revisedA = Domain.Entities.Baseline.Capture(
                tenantId, projectId, "BL-1", DateTimeOffset.UtcNow, Guid.NewGuid(), 1_050_000.00m, []);
            var revisedB = Domain.Entities.Baseline.Capture(
                tenantId, projectId, "BL-2", DateTimeOffset.UtcNow, Guid.NewGuid(), 1_100_000.00m, []);

            seedContext.Baselines.AddRange(original, revisedA, revisedB);
            await seedContext.SaveChangesAsync();
            originalId = original.Id;
            revisedAId = revisedA.Id;
            revisedBId = revisedB.Id;
        }

        // Two independent DbContexts/repositories - request A and request B - each loading its own
        // target plus the SAME pre-race active baseline (`original`), before either one calls
        // TryActivateAsync. This is the "both reading the pre-race active baseline" half of the race.
        await using var contextA = CreateContext(connection, tenantProvider);
        var repoA = new BaselineRepository(
            contextA, tenantProvider, new FixedCurrentUserContext(Guid.NewGuid()), new FixedDateTimeProvider(DateTimeOffset.UtcNow));
        var targetA = await repoA.FindAsync(revisedAId);
        var previousActiveA = await repoA.FindActiveAsync(projectId);

        await using var contextB = CreateContext(connection, tenantProvider);
        var repoB = new BaselineRepository(
            contextB, tenantProvider, new FixedCurrentUserContext(Guid.NewGuid()), new FixedDateTimeProvider(DateTimeOffset.UtcNow));
        var targetB = await repoB.FindAsync(revisedBId);
        var previousActiveB = await repoB.FindActiveAsync(projectId);

        Assert.NotNull(targetA);
        Assert.NotNull(targetB);
        Assert.NotNull(previousActiveA);
        Assert.NotNull(previousActiveB);
        Assert.Equal(originalId, previousActiveA!.Id);
        Assert.Equal(originalId, previousActiveB!.Id);

        // "...then activating in sequence" - request A wins the race, running to completion (both
        // SaveChanges calls, transaction committed) first.
        var winnerSucceeded = await repoA.TryActivateAsync(targetA!, previousActiveA);
        Assert.True(winnerSucceeded);

        // Request B is the loser. Deliberately NOT wrapped in try/catch: the whole point of this test
        // is that no exception may escape TryActivateAsync for this failure shape - if one does,
        // xUnit must report this test as failed, not a caught-and-ignored side detail.
        var loserSucceeded = await repoB.TryActivateAsync(targetB!, previousActiveB);
        Assert.False(loserSucceeded);

        await using var verifyContext = CreateContext(connection, tenantProvider);
        var activeCount = await verifyContext.Baselines.CountAsync(b => b.ProjectId == projectId && b.IsActive);
        Assert.Equal(1, activeCount);

        var activeId = await verifyContext.Baselines
            .Where(b => b.ProjectId == projectId && b.IsActive)
            .Select(b => b.Id)
            .SingleAsync();
        Assert.Equal(revisedAId, activeId);
    }

    /// <summary>
    /// Confirms the filtered unique index actually materializes on Sqlite for the real
    /// <see cref="Configurations.BaselineConfiguration"/> (not just asserted by inspection) - if this
    /// ever silently stopped being unique/filtered (e.g. an unrelated EF Core upgrade changing how
    /// <c>HasFilter</c> translates), <see cref="TryActivateAsync_Never_Violates_The_Single_Active_Baseline_Unique_Index_Across_30_Trials"/>
    /// above would stop being meaningful evidence without this test necessarily failing on its own -
    /// this test is the direct check that the constraint the other test relies on is really there.
    /// </summary>
    [Fact]
    public async Task Inserting_A_Second_Active_Baseline_Directly_Violates_The_Unique_Index()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await CreateBaselineSchemaAsync(connection);
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var projectId = Guid.NewGuid();

        await using (var context = CreateContext(connection, tenantProvider))
        {
            var first = Domain.Entities.Baseline.Capture(
                tenantId, projectId, "BL-0", DateTimeOffset.UtcNow, Guid.NewGuid(), 1_000_000.00m, []);
            first.Activate();
            context.Baselines.Add(first);
            await context.SaveChangesAsync();
        }

        await using var secondContext = CreateContext(connection, tenantProvider);
        var second = Domain.Entities.Baseline.Capture(
            tenantId, projectId, "BL-1", DateTimeOffset.UtcNow, Guid.NewGuid(), 1_000_000.00m, []);
        second.Activate();
        secondContext.Baselines.Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
    }
}
