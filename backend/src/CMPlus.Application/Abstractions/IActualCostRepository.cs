using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Persistence boundary for <c>RecordActualCostCommand</c> - actual-cost.md §9/§12's minimum write
/// path (a single manual entry; Excel/CSV import and the QS grid UI are later work, ADR-0013).
///
/// <para>Every existence check here is <b>project</b>-scoped, not merely tenant-scoped: a
/// <c>WbsNodeId</c>/<c>ActivityId</c>/<c>ReversesEntryId</c> belonging to a different project in
/// the same tenant must be rejected, never silently accepted (actual-cost.md §7.6 - "cost posted
/// to a WBS node in another project/tenant" is release-blocking if it ever passes). Tenant scoping
/// itself still comes from <c>CmPlusDbContext</c>'s global query filter (ADR-0002) - not
/// re-implemented here.</para>
/// </summary>
public interface IActualCostRepository
{
    /// <summary>Tenant-scoped by the global EF query filter (ADR-0002).</summary>
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<bool> WbsNodeExistsAsync(Guid projectId, Guid wbsNodeId, CancellationToken cancellationToken = default);

    Task<bool> ActivityExistsAsync(Guid projectId, Guid activityId, CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="entryId"/> references an existing <see cref="ActualCostEntry"/>
    /// in <paramref name="projectId"/> - used to validate a supplied <c>ReversesEntryId</c>.</summary>
    Task<bool> EntryExistsAsync(Guid projectId, Guid entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when an <c>EvmPeriodSnapshot</c> has already been closed for this project at or after
    /// <paramref name="incurredDate"/> - actual-cost.md §6.6: posting into an already-closed period
    /// is allowed (soft, not hard) but requires a <c>Note</c> on the entry. <c>EvmPeriodSnapshot</c>
    /// itself is never touched by this check or by the resulting insert.
    /// </summary>
    Task<bool> ClosedPeriodExistsOnOrAfterAsync(Guid projectId, DateTimeOffset incurredDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts <paramref name="entry"/> as the only change in this operation - the default
    /// one-row-per-changed-entity <c>AuditSaveChangesInterceptor</c> behaviour is exactly right
    /// here (a single-row mutation, not a bulk operation), producing one <c>Created</c> audit row
    /// (docs/db-conventions.md §7/§8, CLAUDE.md: every mutating domain operation writes an audit
    /// log entry).
    /// </summary>
    Task AddAsync(ActualCostEntry entry, CancellationToken cancellationToken = default);
}
