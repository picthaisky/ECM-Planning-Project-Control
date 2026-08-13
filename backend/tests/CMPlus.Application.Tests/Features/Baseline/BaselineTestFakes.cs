using CMPlus.Application.Abstractions;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Baseline;

/// <summary>Shared hand-written fakes for the Baseline handler test suites - mirrors this
/// codebase's established shared-fakes-per-feature convention (see
/// <c>CMPlus.Application.Tests.Features.Cpm.CpmTestFakes</c>). Bare <c>Baseline</c> is
/// intentionally never used as a type name in this file/namespace - it collides with the enclosing
/// <c>CMPlus.Application.Tests.Features.Baseline</c> namespace segment, so every reference to the
/// entity is written as <c>Domain.Entities.Baseline</c>.</summary>
internal sealed class FakeTenantProvider(Guid tenantId) : ITenantProvider
{
    public Guid TenantId { get; } = tenantId;
}

internal sealed class FakeCurrentUserContext(Guid? userId) : ICurrentUserContext
{
    public Guid? UserId { get; } = userId;

    public UserRole Role => UserRole.PM;
}

internal sealed class FakeClock(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = now;
}

internal sealed class FakeBaselineRepository : IBaselineRepository
{
    public decimal? BacToReturn { get; set; } = 1_000_000.00m;

    public List<BaselineActivitySourceRow> ActivitiesToReturn { get; set; } = [];

    public Dictionary<Guid, Domain.Entities.Baseline> BaselinesById { get; } = [];

    public Domain.Entities.Baseline? AddedBaseline { get; private set; }

    public int AddCallCount { get; private set; }

    /// <summary>Counts underlying <c>SaveChangesAsync</c> calls, not <c>TryActivateAsync</c> calls -
    /// deliberately, so a test asserting on this number exercises the S14-BE-01 fix's actual shape
    /// (one save when there is no previous active baseline to deactivate, two when there is), not
    /// merely "the handler tried to persist something once". Mirrors
    /// <see cref="TryActivateAsync"/>'s production counterpart's real save count.</summary>
    public int SaveChangesCallCount { get; private set; }

    /// <summary>When set, the FIRST underlying save in <see cref="TryActivateAsync"/> (the deactivate,
    /// if one runs - otherwise the activate) reports a concurrency conflict. Named generically
    /// (matching the pre-fix flag this replaces) because most tests using it seed no previous active
    /// baseline, so "first save" and "only save" are the same save.</summary>
    public bool NextSaveHitsConcurrencyConflict { get; set; }

    public Task<decimal?> GetProjectBacAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(BacToReturn);

    public Task<IReadOnlyList<BaselineActivitySourceRow>> GetCurrentActivitiesAsync(
        Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BaselineActivitySourceRow>>(ActivitiesToReturn);

    public Task AddAsync(Domain.Entities.Baseline baseline, CancellationToken cancellationToken = default)
    {
        AddedBaseline = baseline;
        AddCallCount++;
        BaselinesById[baseline.Id] = baseline;
        return Task.CompletedTask;
    }

    public Task<Domain.Entities.Baseline?> FindAsync(Guid baselineId, CancellationToken cancellationToken = default) =>
        Task.FromResult(BaselinesById.GetValueOrDefault(baselineId));

    /// <summary>Scans the live tracked set every call (rather than a separately-maintained
    /// "current active" pointer) so a test can mutate a seeded baseline's <c>IsActive</c> via
    /// <c>Activate</c>/<c>Deactivate</c> and immediately observe the effect here - the same
    /// same-instance semantics an EF Core tracked query would exhibit within one unit of work.
    /// <see cref="Enumerable.FirstOrDefault{TSource}(IEnumerable{TSource})"/>, not
    /// <c>SingleOrDefault</c>, so an accidental two-active seed in a test does not itself throw -
    /// tests assert the invariant explicitly instead.</summary>
    public Task<Domain.Entities.Baseline?> FindActiveAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(BaselinesById.Values.FirstOrDefault(b => b.ProjectId == projectId && b.IsActive));

    /// <summary>Mirrors <c>BaselineRepository.ListByProjectAsync</c>'s contract: this project's
    /// baselines as <see cref="BaselineListRow"/>s, newest capture first (then by time-ordered id).
    /// Other projects' rows are excluded here just as the real query's <c>ProjectId</c> filter (and
    /// the tenant filter) exclude them, so a handler test can assert both the projection and the
    /// scoping. <c>ActivityCount</c> reads the seeded baseline's own count (populated at
    /// <c>Capture</c>), matching the real query's SQL <c>Snapshots.Count</c>.</summary>
    public Task<IReadOnlyList<BaselineListRow>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BaselineListRow>>(
            BaselinesById.Values
                .Where(b => b.ProjectId == projectId)
                .OrderByDescending(b => b.CapturedAt)
                .ThenByDescending(b => b.Id)
                .Select(b => new BaselineListRow(
                    b.Id, b.ProjectId, b.Name, b.IsActive, b.CapturedAt, b.CapturedByUserId, b.Bac, b.ActivityCount))
                .ToList());

    /// <summary>S14-BE-01 fix fake: mirrors <c>BaselineRepository.TryActivateAsync</c>'s real
    /// two-separate-saves shape (mutate <paramref name="previousActive"/>, "save", THEN mutate
    /// <paramref name="target"/>, "save") rather than mutating both up front and saving once - a test
    /// asserting <see cref="SaveChangesCallCount"/> is therefore asserting on the actual fix shape,
    /// not just "some save happened". If the first save fails, the second mutation/save never runs -
    /// mirrors the real method's transaction rollback on the first
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>.</summary>
    public Task<bool> TryActivateAsync(
        Domain.Entities.Baseline target, Domain.Entities.Baseline? previousActive, CancellationToken cancellationToken = default)
    {
        if (previousActive is not null)
        {
            previousActive.Deactivate();
            SaveChangesCallCount++;
            if (NextSaveHitsConcurrencyConflict)
            {
                return Task.FromResult(false);
            }
        }

        target.Activate();
        SaveChangesCallCount++;
        return Task.FromResult(!NextSaveHitsConcurrencyConflict);
    }
}

internal sealed class FakeBaselineComparisonReader : IBaselineComparisonReader
{
    public decimal? CurrentBac { get; set; }

    public BaselineSummary? ActiveBaseline { get; set; }

    public Dictionary<Guid, BaselineSummary> BaselinesById { get; } = [];

    public List<BaselineActivityComparisonRow> Rows { get; set; } = [];

    public Task<decimal?> GetCurrentBacAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(CurrentBac);

    public Task<BaselineSummary?> FindActiveBaselineAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ActiveBaseline);

    public Task<BaselineSummary?> FindBaselineAsync(Guid projectId, Guid baselineId, CancellationToken cancellationToken = default) =>
        Task.FromResult(BaselinesById.GetValueOrDefault(baselineId));

    public Task<IReadOnlyList<BaselineActivityComparisonRow>> GetActivityComparisonAsync(
        Guid projectId, Guid baselineId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BaselineActivityComparisonRow>>(Rows);
}
