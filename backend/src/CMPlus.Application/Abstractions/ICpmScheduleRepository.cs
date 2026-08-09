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
/// </summary>
public interface ICpmScheduleRepository
{
    /// <summary>Tenant-scoped by the global EF query filter (ADR-0002).</summary>
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default);

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
    /// 10,000-activity project would be 10,000+ audit rows). Both the bulk update and the audit row
    /// commit as one atomic transaction. This interface itself has no EF Core dependency - only its
    /// Infrastructure implementation does (ADR-0001).
    /// </summary>
    Task SaveResultsAsync(
        Guid projectId, IReadOnlyList<CpmActivityWriteBack> results, CancellationToken cancellationToken = default);
}
