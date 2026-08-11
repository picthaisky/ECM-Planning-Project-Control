using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>The whole schedule graph <c>RecalculateCpmCommand</c> needs for one project: every
/// <see cref="Activity"/> under that project's WBS (tracked - see
/// <see cref="ICpmScheduleRepository.LoadScheduleGraphAsync"/>), keyed by id, plus every
/// <see cref="ActivityRelation"/> between them (read-only; relations are never mutated by this
/// feature).</summary>
public sealed record CpmScheduleGraph(
    IReadOnlyDictionary<Guid, Activity> Activities, IReadOnlyList<ActivityRelation> Relations);

/// <summary>One activity's CPM result as the persistence boundary sees it - the same three fields
/// <c>Activity.SetCpmResults</c> exposes, decoupled from
/// <c>CMPlus.Application.Services.Cpm.CpmActivityResult</c> so this abstraction does not need to
/// know about the engine's own ES/EF/LS/LF working model.</summary>
public sealed record CpmActivityWriteBack(Guid ActivityId, bool IsCritical, int TotalFloat, int FreeFloat);

/// <summary>Just enough project context for <c>RecalculateCpmCommandHandler</c> to both confirm the
/// project exists (ADR-0002: tenant-scoped, so cross-tenant is indistinguishable from not-found) and
/// populate <see cref="CMPlus.Domain.Entities.CpmRun.DataDate"/> - see that property's own remarks
/// on why it is descriptive metadata only, never a <c>CpmEngine</c> input. Replaces the earlier
/// bare-bool existence check (ADR-0019): folding <c>DataDate</c> into this same single-row lookup
/// costs nothing extra over the bool check it replaces.</summary>
public sealed record CpmProjectContext(DateTimeOffset DataDate);

/// <summary>
/// Persistence boundary for S5-BE-04's <c>RecalculateCpmCommand</c>.
///
/// <para><see cref="SaveResultsAsync"/>'s shape changed from an earlier draft that mirrored
/// <c>IBatchProgressRepository</c>'s "mutate already-tracked entities, let one
/// <c>SaveChangesAsync</c> persist them all" pattern verbatim: that pattern is exactly right at
/// Sprint 3/4's ~20-row batch scale, but a real measurement against the S4-DB-02 10,000-activity
/// dataset showed EF Core's change tracker still emits one individual <c>UPDATE</c> statement per
/// modified <see cref="Activity"/> under the hood - "one <c>SaveChangesAsync</c> call" is not the
/// same thing as "one bulk write" at that row count, and measured ~90 seconds end to end (SQL
/// Server pegged at ~1 CPU core for the whole duration, the API container essentially idle - see
/// the Sprint 5 backend-developer report for the full before/after numbers). <see cref="SaveResultsAsync"/>
/// now takes the actual result values so the Infrastructure implementation can issue one
/// genuinely set-based <c>UPDATE ... FROM ... JOIN</c> statement instead.</para>
///
/// <para><b>ADR-0019 addendum:</b> <see cref="SaveResultsAsync"/> also persists one append-only
/// <see cref="CpmRun"/> (with its <see cref="CpmRun.Activities"/>/<see cref="CpmRun.Relations"/>
/// children) in the SAME transaction as the Activity write-back, via an ordinary EF Core tracked
/// <c>Add</c> - deliberately NOT the raw-SQL OPENJSON technique the Activity <c>UPDATE</c> above
/// uses, for two reasons the Infrastructure implementation's remarks expand on: (a)
/// <c>ExecuteSqlInterpolatedAsync</c> cannot run against the EF Core InMemory provider at all, so a
/// raw-SQL run-capture path would be untestable in this environment (no reachable SQL Server -
/// docs/perf/gantt-frontend-s6.md §3) and unverified by anything but inspection; (b) this keeps run
/// capture exercisable by a real, executed InMemory-backed integration test. This is a genuinely
/// heavier write than before - up to <c>1 + activities.Count + relations.Count</c> new rows per
/// recalculation (e.g. 25,001 at the 10,000-activity/15,000-relation reference scale this
/// codebase's own docs already use), on top of the unchanged bulk <c>UPDATE</c>. That volume was
/// not measured against a real SQL Server in this environment (Docker unavailable); if a future
/// measurement shows it is too slow, the same OPENJSON bulk-insert technique already proven for the
/// <c>UPDATE</c> above is the natural follow-up, not a new design.</para>
///
/// <para><b>database-engineer's read (S11-DB-01, ADR-0019 re-scope):</b> acceptable to ship as the
/// default, not a shortcut to revisit reactively. Unlike the Activity <c>UPDATE</c> this mirrors,
/// these are pure inserts of never-before-seen rows (no seek-then-modify), the ids are time-ordered
/// (<c>Guid.CreateVersion7()</c>, docs/db-conventions.md §3.2) so the clustered index does not
/// thrash, and CPM recalculation carries no stated latency budget anywhere in docs/10. or
/// domain-rules.md the way the WBS tree's &lt;100ms does - so some elevated tolerance here is by
/// design, not an oversight. A correct, InMemory-provable default beats a faster but untested one.
/// <b>Suggested unblock trigger, not a spec'd SLA:</b> the day a real SQL Server is reachable, time
/// <c>CpmRuns.Add(run)</c> + the resulting <c>SaveChangesAsync</c> in isolation (excluding the
/// already-fast OPENJSON <c>UPDATE</c>) against the S4-DB-02 10,000-activity/15,000-relation
/// dataset; a low-single-digit-second result or worse means this step alone dominates the request
/// and justifies the rewrite. <b>What the rewrite costs, concretely:</b> three set-based statements
/// mirroring the proven <c>UPDATE</c> exactly (one plain <c>INSERT</c> for the single
/// <c>CpmRuns</c> row, one <c>INSERT ... SELECT FROM OPENJSON(@activities)</c>, one for
/// <c>@relations</c>, each with the same explicit <c>WHERE</c>/join-scoped
/// <c>TenantId = {tenantProvider.TenantId}</c> defense-in-depth the Activity <c>UPDATE</c> already
/// asserts) - but it moves <c>RecalculateCpmCommandHandlerCpmRunCaptureTests</c>'s currently-passing
/// InMemory assertions onto the real-SQL-Server-only lane
/// (<c>RecalculateCpmCommandHandlerIntegrationTests</c>, itself not executed in this environment
/// either - docs/perf/gantt-frontend-s6.md §3), so a genuine multi-activity run-capture would then be
/// unverified by execution, not just unmeasured, until CI/dev regains real SQL Server access. Weigh
/// that coverage trade against the measured number; do not pre-emptively pay it.</para>
/// </summary>
public interface ICpmScheduleRepository
{
    /// <summary>Tenant-scoped by the global EF query filter (ADR-0002). <see langword="null"/> when
    /// the project does not exist (or is not in the caller's tenant, which the global query filter
    /// makes indistinguishable from "does not exist").</summary>
    Task<CpmProjectContext?> GetProjectContextAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads every <see cref="Activity"/> under <paramref name="projectId"/>'s WBS (tracked - the
    /// caller mutates each one via <c>Activity.SetCpmResults</c> for domain-level correctness/
    /// testability, even though <see cref="SaveResultsAsync"/> persists via a separate bulk
    /// statement rather than relying on the change tracker - see this interface's remarks) together
    /// with every <see cref="ActivityRelation"/> between them (no-tracking - never mutated by this
    /// feature). Exactly two queries, regardless of how many activities/relations the project has
    /// (S5-DB-01's "query count that does not grow with row count" DoD) - never one query per
    /// activity.
    /// </summary>
    Task<CpmScheduleGraph> LoadScheduleGraphAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists every <paramref name="results"/> row as one set-based bulk statement - not a
    /// per-activity round trip, and not "one <c>SaveChangesAsync</c> over 10,000 tracked entities"
    /// either (see this interface's remarks on why that was measured as insufficient) - plus exactly
    /// one summarizing <c>AuditLog</c> row for the whole recalculation (reuses the S3-BE-04
    /// <c>CmPlusDbContext.SuppressPerEntityAudit</c> escape hatch, same as
    /// <c>BatchProgressRepository.SaveBatchAsync</c>, rather than one row per activity - which for a
    /// 10,000-activity project would be 10,000+ audit rows) plus, per ADR-0019, the append-only
    /// <paramref name="run"/> and its children (see this interface's remarks). All three - the bulk
    /// update, the run capture, and the audit row - commit as one atomic transaction. This interface
    /// itself has no EF Core dependency - only its Infrastructure implementation does (ADR-0001).
    /// </summary>
    Task SaveResultsAsync(
        Guid projectId, IReadOnlyList<CpmActivityWriteBack> results, CpmRun run, CancellationToken cancellationToken = default);
}
