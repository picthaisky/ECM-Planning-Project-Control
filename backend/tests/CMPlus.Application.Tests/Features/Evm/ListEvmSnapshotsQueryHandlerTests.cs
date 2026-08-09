using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Features.Evm.Queries.ListEvmSnapshots;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Evm;

/// <summary>
/// Covers the closed-period listing read that S7-FE-04's S-Curve depends on (its DoD requires
/// pre-data-date points to come from frozen snapshots, not a live recomputation - ADR-0009).
/// </summary>
public class ListEvmSnapshotsQueryHandlerTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();

    /// <summary>Records the bounds it was handed so the tests can assert the handler passes the
    /// caller's window straight through to the repository rather than filtering in memory (which
    /// would silently load every snapshot the project has ever closed).</summary>
    private sealed class FakeEvmSnapshotRepository(params EvmPeriodSnapshot[] rows) : IEvmSnapshotRepository
    {
        public int ListCallCount { get; private set; }
        public DateTimeOffset? LastFrom { get; private set; }
        public DateTimeOffset? LastTo { get; private set; }

        public Task<bool> ExistsForDataDateAsync(Guid projectId, DateTimeOffset dataDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task AddAsync(EvmPeriodSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<EvmPeriodSnapshot>> ListAsync(
            Guid projectId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
        {
            ListCallCount++;
            LastFrom = from;
            LastTo = to;
            return Task.FromResult<IReadOnlyList<EvmPeriodSnapshot>>(rows);
        }
    }

    private static EvmPeriodSnapshot Snapshot(DateTimeOffset dataDate) => new(
        tenantId: Guid.NewGuid(),
        projectId: ProjectId,
        dataDate: dataDate,
        bac: 1_000_000m,
        pv: 400_000m,
        ev: 300_000m,
        ac: 350_000m,
        eacVariant: EacVariant.CpiBased,
        performanceFactor: 1.166667m,
        eac: 1_166_666.67m,
        etc: 816_666.67m,
        vac: -166_666.67m,
        createdAt: dataDate,
        createdByUserId: Guid.NewGuid());

    [Fact]
    public async Task Returns_The_Repositorys_Snapshots_Mapped_To_Dtos()
    {
        var first = Snapshot(DateTimeOffset.Parse("2026-01-31T00:00:00Z"));
        var second = Snapshot(DateTimeOffset.Parse("2026-02-28T00:00:00Z"));
        var handler = new ListEvmSnapshotsQueryHandler(new FakeEvmSnapshotRepository(first, second));

        var result = await handler.Handle(new ListEvmSnapshotsQuery(ProjectId, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(first.DataDate, result.Value[0].DataDate);
        Assert.Equal(second.DataDate, result.Value[1].DataDate);
        Assert.Equal(first.Eac, result.Value[0].Eac);
    }

    [Fact]
    public async Task Passes_The_Requested_Window_Through_To_The_Repository()
    {
        // The date filtering must happen in the query, not in memory - otherwise a long-running
        // project silently loads every period it has ever closed just to plot a short window.
        var repository = new FakeEvmSnapshotRepository();
        var handler = new ListEvmSnapshotsQueryHandler(repository);
        var from = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-06-30T00:00:00Z");

        await handler.Handle(new ListEvmSnapshotsQuery(ProjectId, from, to), CancellationToken.None);

        Assert.Equal(1, repository.ListCallCount);
        Assert.Equal(from, repository.LastFrom);
        Assert.Equal(to, repository.LastTo);
    }

    [Fact]
    public async Task An_Inverted_Range_Is_Rejected_Rather_Than_Answered_With_An_Empty_Chart()
    {
        var repository = new FakeEvmSnapshotRepository();
        var handler = new ListEvmSnapshotsQueryHandler(repository);

        var result = await handler.Handle(
            new ListEvmSnapshotsQuery(
                ProjectId,
                DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
                DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(EvmErrorCodes.InvalidSnapshotRange, result.Error);
        Assert.Equal(0, repository.ListCallCount); // rejected before touching the database
    }

    [Fact]
    public async Task An_Unknown_Project_Yields_An_Empty_List_Not_An_Error()
    {
        // Established precedent for list reads in this codebase (import job history, S3-BE-04,
        // confirmed non-leaking in docs/security/reviews/sprint-03.md §5a): the tenant query filter
        // makes an empty result disclose nothing about whether the id exists.
        var handler = new ListEvmSnapshotsQueryHandler(new FakeEvmSnapshotRepository());

        var result = await handler.Handle(
            new ListEvmSnapshotsQuery(Guid.NewGuid(), null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
