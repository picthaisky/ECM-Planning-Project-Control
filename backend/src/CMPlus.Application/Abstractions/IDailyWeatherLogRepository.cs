using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Persistence boundary for <c>RecordWeatherLogCommand</c>/<c>RecordWeatherLogCorrectionCommand</c>/
/// <c>ListWeatherLogsQuery</c> (S11-BE-01, domain-rules.md weather-eot §8). Every existence check is
/// <b>project</b>-scoped, not merely tenant-scoped - mirrors <see cref="IActualCostRepository"/>'s
/// own discipline (a <c>CorrectsWeatherLogId</c>/affected-activity-id belonging to a different
/// project in the same tenant must be rejected, never silently accepted). Tenant scoping itself
/// still comes from <c>CmPlusDbContext</c>'s global query filter (ADR-0002).
/// </summary>
public interface IDailyWeatherLogRepository
{
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Loads the log referenced by <paramref name="logId"/> within <paramref name="projectId"/>
    /// - used by <c>RecordWeatherLogCorrectionCommandHandler</c> to validate domain-rules.md §8.2's
    /// chain-integrity rule 4 (the target must be older: <c>target.RecordedAt &lt; this.RecordedAt</c>).
    /// <see langword="null"/> if it does not exist in this project (mirrors
    /// <see cref="IActualCostRepository.EntryExistsAsync"/>'s identical role, returning the row
    /// instead of a bool since the caller needs <c>RecordedAt</c>, not just existence).</summary>
    Task<DailyWeatherLog?> GetByIdAsync(Guid projectId, Guid logId, CancellationToken cancellationToken = default);

    /// <summary>domain-rules.md §8.2 chain-integrity rule 2 - the load-bearing one: "at most one
    /// entry may point at any given entry". Pre-check backed by the authoritative filtered unique
    /// index <c>(TenantId, CorrectsWeatherLogId) WHERE CorrectsWeatherLogId IS NOT NULL</c> - and,
    /// specifically in this codebase's EF Core InMemory test/dev environment (Docker unavailable),
    /// the ONLY enforcement, since InMemory does not evaluate relational unique/check constraints at
    /// all.</summary>
    Task<bool> HasAnyCorrectionTargetingAsync(Guid projectId, Guid targetLogId, CancellationToken cancellationToken = default);

    /// <summary>Of the given ids, the subset that are real <see cref="Activity"/> ids belonging to
    /// this project - the caller compares counts to detect any id that does not resolve (same shape
    /// as <see cref="IVariationOrderRepository.FindActivitiesForApprovalAsync"/>, but read-only: this
    /// write path never mutates an <see cref="Activity"/>).</summary>
    Task<IReadOnlyList<Guid>> FindExistingActivityIdsAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts <paramref name="log"/> (and its owned <see cref="DailyWeatherLog.AffectedActivities"/>
    /// rows) as the only change in this operation - the default one-row-per-changed-entity
    /// <c>AuditSaveChangesInterceptor</c> behaviour is exactly right here (CLAUDE.md: every mutating
    /// domain operation writes an audit log entry).
    /// </summary>
    Task AddAsync(DailyWeatherLog log, CancellationToken cancellationToken = default);

    /// <summary>Every weather log for one project, optionally bounded by <paramref name="from"/>/
    /// <paramref name="to"/> (inclusive, against <see cref="DailyWeatherLog.LogDate"/>) - S11-DB-01's
    /// date-range-as-seek DoD. Newest first. Never throws/404s on an unknown project - see
    /// <c>ListWeatherLogsQueryHandler</c>'s own remarks.</summary>
    Task<IReadOnlyList<DailyWeatherLog>> ListByProjectAsync(
        Guid projectId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default);
}
