using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>One <see cref="Activity"/>'s current planned dates/duration/budget, read as the source
/// for a new <see cref="Baseline"/> capture (S14-BE-01) - deliberately the same four scalar fields
/// <see cref="BaselineActivitySnapshotInput"/> needs, nothing else.</summary>
public sealed record BaselineActivitySourceRow(
    Guid ActivityId, DateTimeOffset PlannedStart, DateTimeOffset PlannedFinish, int DurationDays, decimal BudgetCost);

/// <summary>
/// One row of the baseline LIST read (<see cref="IBaselineRepository.ListByProjectAsync"/>) - the
/// summary columns the Baseline screen's list needs. <see cref="ActivityCount"/> is computed in SQL as
/// <c>Snapshots.Count</c> (a correlated COUNT, no row materialization): <see cref="Baseline.ActivityCount"/>
/// is <c>Ignore()</c>'d - a passthrough over the in-memory <c>Snapshots</c> collection - so a
/// parent-only entity load would read 0, and the list must not pay to materialize every baseline's
/// (potentially 10,000-row) snapshots just to count them.
/// </summary>
public sealed record BaselineListRow(
    Guid Id, Guid ProjectId, string Name, bool IsActive, DateTimeOffset CapturedAt,
    Guid CapturedByUserId, decimal Bac, int ActivityCount);

/// <summary>
/// Persistence boundary for S14-BE-01's <c>CaptureBaselineCommand</c>/<c>ActivateBaselineCommand</c>.
/// </summary>
public interface IBaselineRepository
{
    /// <summary><see langword="null"/> if <paramref name="projectId"/> does not exist (or belongs to
    /// another tenant, indistinguishable under the global query filter - ADR-0002); otherwise the
    /// project's current <c>BAC</c>, which also doubles as the existence check so callers need no
    /// separate round trip (mirrors <c>IEvmDataReader.GetProjectSettingsAsync</c>'s identical
    /// shape).</summary>
    Task<decimal?> GetProjectBacAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every <see cref="Activity"/> under <paramref name="projectId"/>'s WBS, as it stands right
    /// now - exactly the source data <see cref="Baseline.Capture"/> snapshots. One bounded query,
    /// never one round trip per activity (S14-BE-02's "10,000 activities must stay within perf
    /// targets" applies just as much to capture, even though docs/10 states no explicit latency
    /// budget for the write path itself - mirrors <c>ICpmScheduleRepository.LoadScheduleGraphAsync</c>'s
    /// identical reasoning).
    /// </summary>
    Task<IReadOnlyList<BaselineActivitySourceRow>> GetCurrentActivitiesAsync(
        Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <paramref name="baseline"/> (with its <see cref="Baseline.Snapshots"/> children,
    /// cascaded via the owned-collection navigation - one ordinary tracked <c>Add</c>, never a raw
    /// SQL/OPENJSON bulk technique) plus exactly one summarizing <c>AuditLog</c> row, mirroring
    /// <c>ICpmScheduleRepository.SaveResultsAsync</c>'s <c>SuppressPerEntityAudit</c> pattern for the
    /// identical reason: without it, a 10,000-activity capture would produce 10,000+ per-row audit
    /// rows instead of the one CLAUDE.md's "every mutating domain operation writes an audit log
    /// entry" actually calls for. This is a pure-insert operation (a brand-new <see cref="Baseline"/>
    /// and brand-new children, never a seek-then-modify) with time-ordered ids
    /// (<c>Guid.CreateVersion7()</c>), the same shape <c>CpmRun</c>'s own capture already
    /// establishes as acceptable to persist via ordinary EF Core tracking rather than raw SQL - see
    /// that interface's remarks for the measured reasoning this inherits.
    /// </summary>
    Task AddAsync(Baseline baseline, CancellationToken cancellationToken = default);

    /// <summary>Tracked (not <c>AsNoTracking</c>) and deliberately does <b>not</b> load
    /// <see cref="Baseline.Snapshots"/> - <c>ActivateBaselineCommand</c> only ever mutates
    /// <see cref="Baseline.IsActive"/> on the parent row, so loading a baseline's (potentially
    /// 10,000-row) child collection here would be pure waste. Tenant-scoped by the global EF query
    /// filter (ADR-0002).</summary>
    Task<Baseline?> FindAsync(Guid baselineId, CancellationToken cancellationToken = default);

    /// <summary>The project's currently-active baseline, if any - tracked, parent-only (same
    /// reasoning as <see cref="FindAsync"/>). At most one row can legitimately match; see
    /// <see cref="Baseline"/>'s own remarks on how that invariant is (and is not yet) enforced.
    /// </summary>
    Task<Baseline?> FindActiveAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every baseline captured for <paramref name="projectId"/> as a <see cref="BaselineListRow"/>,
    /// newest capture first - a projected read (<c>AsNoTracking</c>), never loading the potentially-
    /// 10,000-row <see cref="Baseline.Snapshots"/>; the row's <c>ActivityCount</c> is a SQL
    /// <c>Snapshots.Count</c>, see <see cref="BaselineListRow"/>. Tenant-scoped by the global query
    /// filter (ADR-0002), so an unknown or cross-tenant <paramref name="projectId"/> yields an empty
    /// list, never a leak - the same list-read shape <c>IVariationOrderRepository.ListByProjectAsync</c>
    /// establishes.
    /// </summary>
    Task<IReadOnlyList<BaselineListRow>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>S14-BE-01 defect fix (docs/perf/s14-baseline-storage.md §2).</b> Activates
    /// <paramref name="target"/> and, if <paramref name="previousActive"/> is not <see langword="null"/>,
    /// deactivates it first - as <b>two separate, sequential <c>SaveChangesAsync</c> calls wrapped in
    /// one transaction</b>, deliberately never as two <c>Modified</c> siblings flushed together in a
    /// single <c>SaveChanges</c> batch. The single-batch shape this replaced relied on EF Core emitting
    /// the deactivate UPDATE before the activate UPDATE; <c>database-engineer</c>'s 30-trial SQLite
    /// probe against the real <see cref="Baseline"/>/<c>BaselineConfiguration</c> shape proved that
    /// ordering is <b>not</b> something the application can rely on (a ~50/50 split across identical
    /// runs, because <paramref name="target"/> is tracked before <paramref name="previousActive"/> -
    /// see <c>ActivateBaselineCommandHandler</c>'s remarks for why that inverts mutation order versus
    /// tracking order). Splitting into two fully-flushed round trips removes the non-determinism
    /// entirely: the deactivate is durable before the activate statement is even generated, so the
    /// `(TenantId, ProjectId) WHERE IsActive = 1` filtered unique index never sees two active rows,
    /// not even momentarily. Re-run of the identical 30-trial harness against this shape: 30/30
    /// succeeded.
    ///
    /// <para>If <paramref name="previousActive"/> is <see langword="null"/> (the first-ever activation
    /// for a project - nothing to race against), only the activate save runs; the transaction still
    /// wraps it for symmetry/simplicity, not because a single save needs one.</para>
    ///
    /// <para><b>Catches two things, deliberately, not a bare <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>:</b>
    /// (1) <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> - <see cref="Baseline"/>
    /// carries no <c>RowVersion</c> (db-conventions.md §4: not a multi-step/multi-user workflow), so
    /// this branch is not currently reachable in practice, kept only for symmetry with
    /// <c>IProjectRepository.TrySaveChangesAsync</c>'s identical discipline; and (2) a
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> that
    /// <c>Infrastructure.Persistence.UniqueIndexViolationClassifier</c> identifies specifically as a
    /// `(TenantId, ProjectId) WHERE IsActive = 1` filtered-unique-index violation. That second branch
    /// exists because the two-phase-save fix above only removes single-*batch* statement-ordering
    /// non-determinism for one caller's own two writes - it does nothing to stop a second,
    /// independent, genuinely concurrent caller from loading the same <paramref name="previousActive"/>
    /// before this call's transaction commits, then activating a *different* <paramref name="target"/>:
    /// that second call's own activate save then collides with this one's already-committed result
    /// against the real unique index. This is a true two-*request* race, distinct from (and not fixed
    /// by) the single-batch ordering defect above - reproduced against a real SQLite filtered unique
    /// index in <c>BaselineActivationOrderingSqliteTests.TryActivateAsync_Reports_A_Clean_Failure_Not_An_Escaped_Exception_When_Two_Concurrent_Requests_Race</c>.
    /// Anything else - any <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> the
    /// classifier does not recognise as this one specific shape - still surfaces as an unhandled
    /// exception -&gt; <c>GlobalExceptionHandler</c>'s generic 500, exactly as before this fix: see
    /// <c>UniqueIndexViolationClassifier</c>'s own remarks for why a bare
    /// <c>catch (DbUpdateException)</c> here would repeat the exact mistake
    /// <c>ProjectRepository.TrySaveChangesAsync</c>'s doc comment warns against.</para>
    /// </summary>
    Task<bool> TryActivateAsync(Baseline target, Baseline? previousActive, CancellationToken cancellationToken = default);
}
