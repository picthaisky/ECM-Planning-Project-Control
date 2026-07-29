using CMPlus.Application.Import;
using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Persistence boundary for the whole import feature (S3-BE-04). Deliberately one interface (not
/// one per format) because every import shares the same shape: validate -&gt; parse in memory
/// -&gt; a single atomic <c>SaveChanges</c> that both records the <see cref="FileImportJob"/>'s
/// terminal outcome and (on success) bulk-persists the parsed rows - never two round trips, so a
/// job row is never visible in a transient "Pending" state a reader could observe mid-import.
/// Implemented in Infrastructure against <c>CmPlusDbContext</c> (ADR-0001).
/// </summary>
public interface IImportRepository
{
    /// <summary>Tenant-scoped by the caller's <c>ITenantProvider</c> via the global query filter
    /// (ADR-0002) - a project id belonging to another tenant is indistinguishable from "does not exist".</summary>
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Persists a rejected/failed job as the only change in this operation (one row, the
    /// default per-entity <c>AuditSaveChangesInterceptor</c> behaviour is fine here since exactly
    /// one entity changes).</summary>
    Task SaveFailedJobAsync(FileImportJob job, CancellationToken cancellationToken = default);

    /// <summary>Persists a successful XER/MSPDI schedule import: the job (already transitioned to
    /// <c>Succeeded</c> by the caller) plus every parsed WBS node/activity/relation, in one
    /// transaction. Writes exactly one summarizing <c>AuditLog</c> row for the whole operation
    /// (db-conventions.md §8) - the normal one-row-per-changed-entity interceptor behaviour is
    /// suppressed for this call so a 10,000-activity import does not also produce 10,000+ audit rows.</summary>
    Task SaveScheduleImportAsync(FileImportJob job, ParsedSchedule schedule, CancellationToken cancellationToken = default);

    /// <summary>Loads the tracked <see cref="Activity"/> entities (by id, restricted to those whose
    /// <c>WBSNode</c> belongs to <paramref name="projectId"/>) that an Excel progress import will
    /// call <c>RecordProgress</c> on. Ids not found/not in this project are simply absent from the
    /// result - the caller decides how to treat that (S3-BE-03 handler policy).</summary>
    Task<IReadOnlyDictionary<Guid, Activity>> LoadActivitiesForProjectAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default);

    /// <summary>Persists a successful Excel progress import: the job (<c>Succeeded</c>) plus every
    /// new <see cref="ActivityProgressLog"/> row produced by <c>Activity.RecordProgress</c> (the
    /// owning <see cref="Activity"/> cache updates ride along automatically - those instances are
    /// already tracked, having come from <see cref="LoadActivitiesForProjectAsync"/> on this same
    /// repository). One summarizing <c>AuditLog</c> row, same as <see cref="SaveScheduleImportAsync"/>.</summary>
    Task SaveProgressImportAsync(
        FileImportJob job, IReadOnlyList<ActivityProgressLog> newEntries, CancellationToken cancellationToken = default);

    Task<FileImportJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Newest-first, paged (docs/db-conventions.md §6: "page everything that can exceed
    /// ~200 rows").</summary>
    Task<IReadOnlyList<FileImportJob>> GetJobHistoryAsync(
        Guid projectId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Every activity in the project, for the Excel export template (S3-BE-03) - current
    /// cached <c>ProgressPercentage</c> included so the field team can see what they are updating.</summary>
    Task<IReadOnlyList<ActivityProgressTemplateRow>> GetActivityTemplateRowsAsync(
        Guid projectId, CancellationToken cancellationToken = default);
}
