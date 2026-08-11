using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Persistence boundary for the <see cref="VariationOrder"/> aggregate and its S10-BE-01/02/03
/// approval-chain/approval-effects commands. Mirrors <see cref="IPaymentCertificateRepository"/>'s
/// "tracked load, mutate via domain methods, explicit <see cref="TrySaveChangesAsync"/>" shape, plus
/// two VO-specific extensions: <see cref="FindActivitiesForApprovalAsync"/> (tracked, on the SAME
/// scoped <c>DbContext</c>, so applying the scope payload's budget deltas commits atomically with the
/// document's own state change - domain-rules.md §5.1 "Atomicity") and
/// <see cref="GetApprovedNetSignedTotalAsync"/> (the escalation-bypass re-check's $\Sigma^{VO}$,
/// domain-rules.md §4.7).
/// </summary>
public interface IVariationOrderRepository
{
    /// <summary>Tracked (not <c>AsNoTracking</c>) - the caller mutates the returned instance via its
    /// domain methods and this same instance is what <see cref="TrySaveChangesAsync"/> persists.
    /// Includes <c>ApprovalSteps</c> and <c>ScopeItems</c> (both owned collections, needed by every
    /// transition handler). Tenant-scoped by the global EF query filter (ADR-0002).</summary>
    Task<VariationOrder?> FindAsync(Guid variationOrderId, CancellationToken cancellationToken = default);

    /// <summary>Read-only sibling of <see cref="FindAsync"/> for query handlers that will never
    /// follow this load with <see cref="TrySaveChangesAsync"/>.</summary>
    Task<VariationOrder?> GetByIdAsync(Guid variationOrderId, CancellationToken cancellationToken = default);

    /// <summary>Every variation order for one project, newest-first (mirrors
    /// <see cref="IPaymentCertificateRepository.ListByProjectAsync"/>'s own UUIDv7 ordinal-string
    /// ordering rationale).</summary>
    Task<IReadOnlyList<VariationOrder>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>domain-rules.md §7.1: <c>VoNumber</c> is unique per <c>(TenantId, ProjectId)</c>.
    /// Pre-check used at creation - the unique index is the authoritative, race-safe backstop.</summary>
    Task<bool> ExistsWithVoNumberAsync(Guid projectId, string voNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// $\Sigma^{VO}$ - the net-signed (additions less deductions, ADR-0015 N-1) sum of every
    /// <c>Approved</c> variation order's <c>Amount</c> for this project. Used both by the Submit-time
    /// escalation test (via <c>ApprovalRoutingRequest.CumulativeApprovedVoAmount</c>) and by the
    /// domain-rules.md §4.7 final-approval escalation-bypass re-check. <c>database-engineer</c> owns
    /// the S10-DB-01 index that makes this an index seek rather than a scan
    /// (<c>UNIQUE/(TenantId, ProjectId, Status)</c>-shaped) - this method is the query that index
    /// serves, not a duplicate of that work.
    /// </summary>
    Task<decimal> GetApprovedNetSignedTotalAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Tracked <see cref="Activity"/> rows for the given ids, scoped to
    /// <paramref name="projectId"/>, on the SAME scoped <c>DbContext</c> as the
    /// <see cref="VariationOrder"/> this call is servicing - so <see cref="Activity.AdjustBudgetCost"/>
    /// mutations commit in the same <see cref="TrySaveChangesAsync"/> transaction as the document's own
    /// state change (domain-rules.md §5.1 "Atomicity"). Any id not found (wrong project, or does not
    /// exist) is simply absent from the result; the caller is responsible for detecting a short list.
    /// </summary>
    Task<IReadOnlyList<Activity>> FindActivitiesForApprovalAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default);

    void Add(VariationOrder variationOrder);

    /// <summary>
    /// Stages one manually-constructed <see cref="AuditLog"/> row - domain-rules.md §6.2's mandatory
    /// disclosure ("<c>VoApprovedWithCertificateInFlight</c>") when a VO reaches <c>Approved</c> while
    /// an IPC for the same project sits <c>PendingApproval</c>. Distinct from the generic per-entity
    /// audit <c>AuditSaveChangesInterceptor</c> already writes automatically for every changed row:
    /// that mechanism audits ONE entity's own before/after property snapshot, but this disclosure is
    /// about a relationship between TWO documents (the VO and the affected certificate) that no single
    /// entity's property diff could express - the same "summarizing row an Infrastructure repository
    /// adds directly" shape <c>CpmScheduleRepository</c>/<c>ImportRepository</c> already use for their
    /// own bulk-operation summaries. Persisted in the same <see cref="TrySaveChangesAsync"/> call as
    /// everything else in this unit of work.
    /// </summary>
    void AddAuditLogEntry(AuditLog entry);

    /// <summary>Persists every staged change for this unit of work (the document's own mutation, any
    /// <see cref="Activity"/>/<see cref="Project"/> changes tracked on the same <c>DbContext</c>, and -
    /// via <see cref="IApprovalActionRepository.Add"/> sharing the same scope - the new
    /// <see cref="ApprovalAction"/> row) as one atomic transaction. Returns <see langword="false"/>
    /// instead of throwing on a concurrent <see cref="VariationOrder.RowVersion"/> mismatch (409, never
    /// a double-advance).</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default);
}
