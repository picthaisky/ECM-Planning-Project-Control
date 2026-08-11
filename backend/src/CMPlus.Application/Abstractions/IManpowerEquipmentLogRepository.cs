using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Persistence boundary for <c>RecordManpowerLogCommand</c>/<c>RecordManpowerLogCorrectionCommand</c>
/// (S12-BE-02, domain-rules.md (manpower-equipment) §4). Every existence check is <b>project</b>-scoped,
/// not merely tenant-scoped - mirrors <see cref="IDailyWeatherLogRepository"/>'s own discipline (a
/// <c>WorkCategoryId</c>/<c>WbsNodeId</c>/<c>ActivityId</c>/<c>CorrectsLogId</c> belonging to a
/// different project in the same tenant must be rejected, never silently accepted). Tenant scoping
/// itself still comes from <c>CmPlusDbContext</c>'s global query filter (ADR-0002).
/// </summary>
public interface IManpowerEquipmentLogRepository
{
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Of the given ids, the subset that are real <see cref="WorkCategory"/> ids visible to
    /// this project (tenant-wide default catalogue entries - <c>ProjectId == null</c> - or this
    /// project's own override).</summary>
    Task<IReadOnlyList<Guid>> FindExistingWorkCategoryIdsAsync(
        Guid projectId, IReadOnlyCollection<Guid> workCategoryIds, CancellationToken cancellationToken = default);

    /// <summary>Of the given ids, the subset that are real <see cref="WBSNode"/> ids belonging to
    /// this project.</summary>
    Task<IReadOnlyList<Guid>> FindExistingWbsNodeIdsAsync(
        Guid projectId, IReadOnlyCollection<Guid> wbsNodeIds, CancellationToken cancellationToken = default);

    /// <summary>§4.1's last row of the validation table, cross-tenant half (ADR-0002): of the given
    /// ids, the subset that are real <see cref="WBSNode"/> ids <b>anywhere in this tenant</b> (no
    /// <c>ProjectId</c> predicate - tenant scoping alone comes from the global EF query filter). Used
    /// only to distinguish "belongs to a different project in this same tenant" (422
    /// <c>WbsNodeNotInProject</c>, fixture M-14a's sibling) from "does not resolve in this tenant at
    /// all" (404, fixture M-14b) - never to bypass tenant scoping itself.</summary>
    Task<IReadOnlyList<Guid>> FindWbsNodeIdsInTenantAsync(
        IReadOnlyCollection<Guid> wbsNodeIds, CancellationToken cancellationToken = default);

    /// <summary>The subset of <paramref name="activityIds"/> that are real <see cref="Activity"/> ids
    /// belonging to this project, each paired with its own <see cref="Activity.WbsNodeId"/> - the
    /// caller (the handler) uses this both for the existence check and for §4.1's last validation
    /// rule (an <c>ActivityId</c>'s own node must agree with the row's <c>WbsNodeId</c> when both are
    /// set).</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> FindExistingActivitiesWithWbsNodeAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default);

    /// <summary>Tenant-wide half of the same cross-tenant/wrong-project distinction, for
    /// <see cref="Activity"/> ids (fixture M-14b).</summary>
    Task<IReadOnlyList<Guid>> FindActivityIdsInTenantAsync(
        IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default);

    /// <summary>Loads the log referenced by <paramref name="logId"/> within <paramref name="projectId"/> -
    /// used by <c>RecordManpowerLogCorrectionCommandHandler</c> to validate §4.7's chain-integrity
    /// rule 4 (the target must be older). <see langword="null"/> if it does not exist in this
    /// project.</summary>
    Task<ManpowerEquipmentLog?> GetByIdAsync(Guid projectId, Guid logId, CancellationToken cancellationToken = default);

    /// <summary>§4.7 chain-integrity rule 2 - the load-bearing one: "at most one entry may point at
    /// any given entry". Pre-check backed by the authoritative filtered unique index
    /// <c>(TenantId, CorrectsLogId) WHERE CorrectsLogId IS NOT NULL</c>, and - specifically in this
    /// codebase's EF Core InMemory test/dev environment (Docker unavailable) - the ONLY enforcement,
    /// since InMemory does not evaluate relational unique/check constraints at all.</summary>
    Task<bool> HasAnyCorrectionTargetingAsync(Guid projectId, Guid targetLogId, CancellationToken cancellationToken = default);

    /// <summary>§4.4/Q8's warn-and-confirm duplicate check: does an in-force <c>Original</c> row
    /// already exist for this exact natural key (LogDate, Shift, WorkCategoryId, WbsNodeId,
    /// LabourType, SubcontractorRef)? "In-force" means no correction/retraction points at it yet.</summary>
    Task<bool> HasInForceOriginalForNaturalKeyAsync(
        Guid projectId,
        DateTimeOffset logDate,
        CMPlus.Domain.Enums.Shift shift,
        Guid workCategoryId,
        Guid? wbsNodeId,
        CMPlus.Domain.Enums.LabourType labourType,
        string? subcontractorRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts <paramref name="log"/> as the only change in this operation - the default
    /// one-row-per-changed-entity <c>AuditSaveChangesInterceptor</c> behaviour is exactly right here
    /// (CLAUDE.md: every mutating domain operation writes an audit log entry).
    /// </summary>
    Task AddAsync(ManpowerEquipmentLog log, CancellationToken cancellationToken = default);
}
