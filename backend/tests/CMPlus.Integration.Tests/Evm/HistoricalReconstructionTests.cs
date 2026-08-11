using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Features.Evm.Commands.CloseEvmPeriod;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Evm;

/// <summary>
/// S7-QA-03 (docs/10 §7, ADR-0009): independent end-to-end proof of the one contrast the whole
/// <see cref="EvmPeriodSnapshot"/> mechanism exists for - a *live* EVM recomputation at a past data
/// date legitimately changes when a backdated <see cref="ActivityProgressLog"/> entry lands, while an
/// already-closed <see cref="EvmPeriodSnapshot"/> for that same data date is frozen and never changes.
/// Neither half is proven together anywhere else in this sprint's suite:
/// <see cref="CMPlus.Integration.Tests.Persistence.EvmDataReaderTests"/> proves the reader's own
/// step-function correctness in isolation (including a historical read differing from the activity's
/// cache), and the Domain.Tests <c>EvmPeriodSnapshotTests</c> prove the snapshot type is structurally
/// immutable (no setter/mutator exists at all) - but nothing else exercises the real sequence "close a
/// period, land a correction, then prove the contrast" against a real <see cref="CmPlusDbContext"/>.
///
/// <para><b>EF Core InMemory, not real SQL Server - what this does and does not prove.</b> Docker
/// Desktop cannot start on this workstation (docs/perf/gantt-frontend-s6.md §3), so there is no way to
/// run this against a real MSSQL container this sprint; it does not qualify for the
/// <c>CMPLUS_TEST_MSSQL_CONNECTION</c> soft-skip pattern (<c>MigrationAndSeedSmokeTests</c>) either,
/// because that pattern exists for tests that specifically validate migrations/SQL Server-only
/// behaviour, and this one doesn't need to. ADR-0009's reconstruction rule ("latest PeriodEndDate
/// &lt;= asOf, ties broken by RecordedAt") is expressed entirely in LINQ
/// (<see cref="EvmDataReader.GetActivityInputsAsync"/>) and is exercised here through the exact same
/// LINQ/EF pipeline that runs against real SQL Server, so InMemory faithfully proves the
/// *application-level* ADR-0009 semantics this test targets - this is a correctness proof, not a
/// performance one. It does **not** prove the query plan is an index seek at the
/// 10,000-activity/26-period scale against a real query optimizer; that remains S7-DB-02's separate,
/// explicitly database-engine-owned concern and stays unverified until the Docker outage is resolved.
/// </para>
/// </summary>
public class HistoricalReconstructionTests
{
    private static Project CreateProject(Guid tenantId, decimal bac) =>
        Project.Create(
            tenantId, "Historical Reconstruction Test Project", "HRT-1", "Owner",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
            bac: bac, dataDate: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    private sealed class FixedActualCostReader(decimal amount) : IActualCostReader
    {
        public Task<ActualCostResult> GetActualCostAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActualCostResult(amount, amount == 0m ? 0 : 1));
    }

    private sealed class FakeCurrentUserContext(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.PM;
    }

    private sealed class FakeClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static CloseEvmPeriodCommandHandler CreateCloseHandler(
        CmPlusDbContext context, TestDbContextFactory factory, IActualCostReader actualCostReader, DateTimeOffset now) =>
        new(
            new EvmComputationService(new EvmDataReader(context), actualCostReader),
            new EvmSnapshotRepository(context),
            factory.TenantProvider,
            new FakeCurrentUserContext(Guid.NewGuid()),
            new FakeClock(now));

    [Fact]
    public async Task Backdated_Correction_To_An_Already_Closed_Period_Changes_The_Live_Ev_But_Not_The_Frozen_Snapshot()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = CreateProject(tenantId, bac: 1_000_000.00m);
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);
        var activity = new Activity(
            tenantId, node.Id, "A-1", "Foundation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-06-30T00:00:00Z"), 180, 1_000_000.00m);

        var periodOneEnd = DateTimeOffset.Parse("2026-01-31T00:00:00Z");
        var periodTwoEnd = DateTimeOffset.Parse("2026-02-28T00:00:00Z");

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.Add(activity);
            await seedContext.SaveChangesAsync();
        }

        // Period 1 originally reported 30%, then period 2 reported 60% - period 2 becomes the
        // Activity.ProgressPercentage cache (the greatest PeriodEndDate recorded so far).
        using (var writeContext = factory.CreateContext())
        {
            var tracked = await writeContext.Activities.SingleAsync(a => a.Id == activity.Id);
            var period1Original = tracked.RecordProgress(
                periodOneEnd, 30m, null, Guid.NewGuid(), ProgressSource.Manual,
                recordedAt: DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
            var period2 = tracked.RecordProgress(
                periodTwoEnd, 60m, null, Guid.NewGuid(), ProgressSource.Manual,
                recordedAt: DateTimeOffset.Parse("2026-03-01T00:00:00Z"));
            writeContext.ActivityProgressLogs.AddRange(period1Original, period2);
            await writeContext.SaveChangesAsync();
        }

        var actualCostReader = new FixedActualCostReader(400_000.00m);

        // --- Live EVM at period 1's own data date, BEFORE any correction lands. ---
        EvmComputation? beforeCorrection;
        using (var readContext = factory.CreateContext())
        {
            var computationService = new EvmComputationService(new EvmDataReader(readContext), actualCostReader);
            beforeCorrection = await computationService.ComputeAsync(project.Id, periodOneEnd);
        }

        Assert.NotNull(beforeCorrection);
        Assert.Equal(300_000.00m, beforeCorrection!.Core.Ev); // 1,000,000 x 30% - period 1's original report.

        // --- Close period 1: freezes this exact EV into an EvmPeriodSnapshot. ---
        Guid snapshotId;
        using (var closeContext = factory.CreateContext())
        {
            var handler = CreateCloseHandler(closeContext, factory, actualCostReader, DateTimeOffset.Parse("2026-02-05T00:00:00Z"));

            var closeResult = await handler.Handle(new CloseEvmPeriodCommand(project.Id, periodOneEnd), CancellationToken.None);
            Assert.True(closeResult.IsSuccess);
            Assert.Equal(300_000.00m, closeResult.Value.Ev);
            snapshotId = closeResult.Value.SnapshotId;
        }

        // --- A backdated correction lands: period 1 was actually 45%, not 30%, discovered two
        // months later. Same PeriodEndDate as the original period-1 entry, so ADR-0009's tie-break
        // ("greatest RecordedAt wins") applies for a live read as-of period 1 - but its PeriodEndDate
        // is still behind period 2's, so it must NOT move Activity.ProgressPercentage's cache
        // (S1-BE-02's own rule; asserted here too, since it is the reason the *next* assertion below
        // is a meaningful contrast and not an accident of the cache also having moved).
        using (var backdateContext = factory.CreateContext())
        {
            var tracked = await backdateContext.Activities.SingleAsync(a => a.Id == activity.Id);
            var correction = tracked.RecordProgress(
                periodOneEnd, 45m, null, Guid.NewGuid(), ProgressSource.Manual,
                recordedAt: DateTimeOffset.Parse("2026-04-01T00:00:00Z"));
            backdateContext.ActivityProgressLogs.Add(correction);
            await backdateContext.SaveChangesAsync();

            Assert.Equal(60m, tracked.ProgressPercentage); // cache still period 2's value, unmoved.
        }

        // --- Half 1 of the DoD: the LIVE recomputation at period 1's data date has changed. ---
        EvmComputation? afterCorrection;
        using (var readContext = factory.CreateContext())
        {
            var computationService = new EvmComputationService(new EvmDataReader(readContext), actualCostReader);
            afterCorrection = await computationService.ComputeAsync(project.Id, periodOneEnd);
        }

        Assert.NotNull(afterCorrection);
        Assert.Equal(450_000.00m, afterCorrection!.Core.Ev); // 1,000,000 x 45% - the correction now wins the tie-break.
        Assert.NotEqual(beforeCorrection.Core.Ev, afterCorrection.Core.Ev);

        // PV depends only on planned dates (unchanged throughout this test) - it must be identical
        // before/after, proving the correction affected EV and only EV.
        Assert.Equal(beforeCorrection.Core.Pv, afterCorrection.Core.Pv);

        // --- Half 2 of the DoD: the CLOSED snapshot is untouched - re-read from a brand new context
        // (not the tracked instance from the close step) to prove real durability, not merely an
        // unchanged in-memory object reference. ---
        using (var verifyContext = factory.CreateContext())
        {
            var snapshot = await verifyContext.EvmPeriodSnapshots.SingleAsync(s => s.Id == snapshotId);

            Assert.Equal(300_000.00m, snapshot.Ev); // still the pre-correction value - frozen.
            Assert.Equal(project.Id, snapshot.ProjectId);
            Assert.Equal(periodOneEnd, snapshot.DataDate);
        }
    }

    [Fact]
    public async Task A_Correction_To_An_Earlier_Period_Does_Not_Leak_Forward_Into_A_Later_Periods_Live_Ev()
    {
        // Companion to the test above: the step function is scoped by PeriodEndDate ordering, not
        // just "most recently written" - a correction to period 1 must never change period 2's own
        // already-later-dated EV, before or after it lands.
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = CreateProject(tenantId, bac: 1_000_000.00m);
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);
        var activity = new Activity(
            tenantId, node.Id, "A-1", "Foundation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-06-30T00:00:00Z"), 180, 1_000_000.00m);

        var periodOneEnd = DateTimeOffset.Parse("2026-01-31T00:00:00Z");
        var periodTwoEnd = DateTimeOffset.Parse("2026-02-28T00:00:00Z");

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.Add(activity);
            await seedContext.SaveChangesAsync();
        }

        using (var writeContext = factory.CreateContext())
        {
            var tracked = await writeContext.Activities.SingleAsync(a => a.Id == activity.Id);
            var period1 = tracked.RecordProgress(
                periodOneEnd, 30m, null, Guid.NewGuid(), ProgressSource.Manual, DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
            var period2 = tracked.RecordProgress(
                periodTwoEnd, 60m, null, Guid.NewGuid(), ProgressSource.Manual, DateTimeOffset.Parse("2026-03-01T00:00:00Z"));
            writeContext.ActivityProgressLogs.AddRange(period1, period2);
            await writeContext.SaveChangesAsync();
        }

        var actualCostReader = new FixedActualCostReader(400_000.00m);

        decimal evAtPeriodTwoBefore;
        using (var readContext = factory.CreateContext())
        {
            var computationService = new EvmComputationService(new EvmDataReader(readContext), actualCostReader);
            var computation = await computationService.ComputeAsync(project.Id, periodTwoEnd);
            Assert.NotNull(computation);
            evAtPeriodTwoBefore = computation!.Core.Ev;
        }

        Assert.Equal(600_000.00m, evAtPeriodTwoBefore); // 1,000,000 x 60%.

        using (var backdateContext = factory.CreateContext())
        {
            var tracked = await backdateContext.Activities.SingleAsync(a => a.Id == activity.Id);
            var correction = tracked.RecordProgress(
                periodOneEnd, 45m, null, Guid.NewGuid(), ProgressSource.Manual, DateTimeOffset.Parse("2026-04-01T00:00:00Z"));
            backdateContext.ActivityProgressLogs.Add(correction);
            await backdateContext.SaveChangesAsync();
        }

        decimal evAtPeriodTwoAfter;
        using (var readContext = factory.CreateContext())
        {
            var computationService = new EvmComputationService(new EvmDataReader(readContext), actualCostReader);
            var computation = await computationService.ComputeAsync(project.Id, periodTwoEnd);
            Assert.NotNull(computation);
            evAtPeriodTwoAfter = computation!.Core.Ev;
        }

        Assert.Equal(600_000.00m, evAtPeriodTwoAfter); // unchanged - period 2's own later entry still wins.
        Assert.Equal(evAtPeriodTwoBefore, evAtPeriodTwoAfter);
    }

    [Fact]
    public async Task Backfilling_A_Previously_Missing_Period_Changes_The_Live_Ev_At_That_Date_But_Not_An_Already_Closed_Snapshot()
    {
        // The other half of ADR-0009's step function: "no entry <= t contributes 0", later backfilled
        // - distinct from the tie-break/correction scenario above (there is no earlier entry to
        // out-rank here at all; the query's `?? 0m` fallback is what is actually being exercised).
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = CreateProject(tenantId, bac: 500_000.00m);
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);
        var activity = new Activity(
            tenantId, node.Id, "A-1", "Rebar",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-06-30T00:00:00Z"), 180, 500_000.00m);

        var dataDate = DateTimeOffset.Parse("2026-01-31T00:00:00Z");

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.Add(activity);
            await seedContext.SaveChangesAsync();
        }

        var actualCostReader = new FixedActualCostReader(0m);

        // No progress logged at all yet - EV(dataDate) must be exactly 0.00, not an error.
        using (var readContext = factory.CreateContext())
        {
            var computationService = new EvmComputationService(new EvmDataReader(readContext), actualCostReader);
            var computation = await computationService.ComputeAsync(project.Id, dataDate);
            Assert.NotNull(computation);
            Assert.Equal(0m, computation!.Core.Ev);
        }

        Guid snapshotId;
        using (var closeContext = factory.CreateContext())
        {
            var handler = CreateCloseHandler(closeContext, factory, actualCostReader, DateTimeOffset.Parse("2026-02-05T00:00:00Z"));

            var closeResult = await handler.Handle(new CloseEvmPeriodCommand(project.Id, dataDate), CancellationToken.None);
            Assert.True(closeResult.IsSuccess);
            Assert.Equal(0m, closeResult.Value.Ev);
            snapshotId = closeResult.Value.SnapshotId;
        }

        // Backfill: a progress record for that exact past date is entered for the first time, weeks
        // after the period was already closed.
        using (var backfillContext = factory.CreateContext())
        {
            var tracked = await backfillContext.Activities.SingleAsync(a => a.Id == activity.Id);
            var backfilled = tracked.RecordProgress(
                dataDate, 20m, null, Guid.NewGuid(), ProgressSource.Manual,
                recordedAt: DateTimeOffset.Parse("2026-03-15T00:00:00Z"));
            backfillContext.ActivityProgressLogs.Add(backfilled);
            await backfillContext.SaveChangesAsync();
        }

        using (var readContext = factory.CreateContext())
        {
            var computationService = new EvmComputationService(new EvmDataReader(readContext), actualCostReader);
            var computation = await computationService.ComputeAsync(project.Id, dataDate);
            Assert.NotNull(computation);
            Assert.Equal(100_000.00m, computation!.Core.Ev); // 500,000 x 20% - no longer 0.
        }

        using (var verifyContext = factory.CreateContext())
        {
            var snapshot = await verifyContext.EvmPeriodSnapshots.SingleAsync(s => s.Id == snapshotId);
            Assert.Equal(0m, snapshot.Ev); // still frozen at the pre-backfill value.
        }
    }
}
