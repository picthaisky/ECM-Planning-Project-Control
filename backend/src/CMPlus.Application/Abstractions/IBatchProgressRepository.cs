using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Persistence boundary for the S4-BE-03 batch progress-update feature. Shape deliberately mirrors
/// <c>IImportRepository</c>'s "load tracked activities, then commit everything (new log rows + the
/// already-mutated activity caches + one summarizing audit row) in a single <c>SaveChanges</c>"
/// pattern (Sprint 3 precedent) - reused here rather than reinvented, per docs/10 §6 S4-BE-03.
/// </summary>
public interface IBatchProgressRepository
{
    /// <summary>Tenant-scoped by the global EF query filter (ADR-0002).</summary>
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Loads the tracked <see cref="Activity"/> entities (by id, restricted to those whose
    /// <c>WBSNode</c> belongs to <paramref name="projectId"/>) that the batch will call
    /// <c>RecordProgress</c> on. Ids not found/not in this project are simply absent from the
    /// result - the caller rejects the whole batch if any are missing (S4-BE-03 DoD: all-or-nothing).</summary>
    Task<IReadOnlyDictionary<Guid, Activity>> LoadActivitiesForProjectAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists every new <see cref="ActivityProgressLog"/> row produced by
    /// <c>Activity.RecordProgress</c> (the owning <see cref="Activity"/> cache updates ride along
    /// automatically - those instances are already tracked, having come from
    /// <see cref="LoadActivitiesForProjectAsync"/> on this same repository) plus exactly one
    /// summarizing <c>AuditLog</c> row for the whole batch (S4-BE-03 DoD - reuses the S3-BE-04
    /// <c>CmPlusDbContext.SuppressPerEntityAudit</c> escape hatch rather than one row per activity).
    /// The single <c>SaveChangesAsync</c> call this makes is one atomic transaction - all rows
    /// commit or none do (S4-BE-03 DoD: "the whole batch is one transaction").
    /// </summary>
    Task SaveBatchAsync(
        Guid projectId,
        DateTimeOffset periodEndDate,
        IReadOnlyList<ActivityProgressLog> newEntries,
        CancellationToken cancellationToken = default);
}
