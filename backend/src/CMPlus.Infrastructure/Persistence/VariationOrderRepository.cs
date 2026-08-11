using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S10-BE-01/02/03: <see cref="IVariationOrderRepository"/> against <see cref="CmPlusDbContext"/>.
/// Mirrors <see cref="PaymentCertificateRepository"/>'s shape (tracked load with both owned
/// collections included, ordinal-string "newest first" ordering, <c>DbUpdateConcurrencyException</c>
/// -&gt; <c>false</c> translation) plus the two VO-specific reads
/// <see cref="GetApprovedNetSignedTotalAsync"/> and <see cref="FindActivitiesForApprovalAsync"/>.</summary>
public sealed class VariationOrderRepository(CmPlusDbContext dbContext) : IVariationOrderRepository
{
    /// <summary>
    /// Tracked, and critically <c>Include</c>s both <see cref="VariationOrder.ApprovalSteps"/> (H-01
    /// fix - Approve/Reject/ReturnForRevision/Withdraw resolve step authority and void the snapshot
    /// entirely from this collection) and <see cref="VariationOrder.ScopeItems"/> (Approve applies
    /// each line's <c>BudgetCostDelta</c> to its <see cref="Activity"/>, and
    /// <c>UpdateVariationOrderContentCommandHandler</c>'s re-price replaces the whole collection) -
    /// both require the collection loaded up front, not lazily (this codebase has no lazy-loading
    /// proxies, ADR-0004-adjacent "no surprises" convention).
    /// </summary>
    public Task<VariationOrder?> FindAsync(Guid variationOrderId, CancellationToken cancellationToken = default) =>
        dbContext.VariationOrders
            .Include(v => v.ApprovalSteps)
            .Include(v => v.ScopeItems)
            .FirstOrDefaultAsync(v => v.Id == variationOrderId, cancellationToken);

    /// <summary>Read-only sibling of <see cref="FindAsync"/> for query handlers that will never
    /// follow this load with <see cref="TrySaveChangesAsync"/>.</summary>
    public Task<VariationOrder?> GetByIdAsync(Guid variationOrderId, CancellationToken cancellationToken = default) =>
        dbContext.VariationOrders
            .AsNoTracking()
            .Include(v => v.ApprovalSteps)
            .Include(v => v.ScopeItems)
            .FirstOrDefaultAsync(v => v.Id == variationOrderId, cancellationToken);

    /// <summary>"Newest first" orders by <see cref="CMPlus.Domain.Common.Entity.Id"/> (a UUIDv7),
    /// sorted client-side by the canonical hex string (ordinal comparison) - see
    /// <see cref="PaymentCertificateRepository.ListByProjectAsync"/>'s own remarks for the full,
    /// empirically-verified rationale (SQL Server <c>uniqueidentifier</c> collation and
    /// <see cref="Guid.CreateVersion7"/>'s millisecond-only resolution); identical reasoning applies
    /// here unchanged.</summary>
    public async Task<IReadOnlyList<VariationOrder>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.VariationOrders
            .AsNoTracking()
            .Include(v => v.ApprovalSteps)
            .Include(v => v.ScopeItems)
            .Where(v => v.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(v => v.Id.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    public Task<bool> ExistsWithVoNumberAsync(Guid projectId, string voNumber, CancellationToken cancellationToken = default) =>
        dbContext.VariationOrders
            .AsNoTracking()
            .AnyAsync(v => v.ProjectId == projectId && v.VoNumber == voNumber, cancellationToken);

    /// <summary>
    /// $\Sigma^{VO}$ - net-signed (ADR-0015 N-1) SUM of every <c>Approved</c> VO's signed
    /// <c>Amount</c> for this project. A pure server-side <c>SumAsync</c>: no per-row
    /// materialization, so this stays an index-seek-and-aggregate regardless of how many approved
    /// VOs a project accumulates over its life (S10-DB-01's own concern is the covering index behind
    /// this filter, not this query shape).
    /// </summary>
    public Task<decimal> GetApprovedNetSignedTotalAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.VariationOrders
            .AsNoTracking()
            .Where(v => v.ProjectId == projectId && v.Status == VariationOrderStatus.Approved)
            .SumAsync(v => v.Amount, cancellationToken);

    /// <summary>
    /// Tracked <see cref="Activity"/> rows scoped to <paramref name="projectId"/> via the
    /// established <c>WBSNodes</c> join (the same shape <c>CpmScheduleRepository</c> already uses -
    /// <see cref="Activity"/> has no direct <c>ProjectId</c> column, only <c>WbsNodeId</c>), on the
    /// SAME scoped <see cref="CmPlusDbContext"/> as the <see cref="VariationOrder"/> this call
    /// services - so a caller's <see cref="Activity.AdjustBudgetCost"/> mutations commit atomically
    /// with the document's own state change via this repository's own
    /// <see cref="TrySaveChangesAsync"/> (domain-rules.md §5.1 "Atomicity").
    /// </summary>
    public async Task<IReadOnlyList<Activity>> FindActivitiesForApprovalAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default)
    {
        if (activityIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Activities
            .Where(a => activityIds.Contains(a.Id) && dbContext.WBSNodes.Any(w => w.Id == a.WbsNodeId && w.ProjectId == projectId))
            .ToListAsync(cancellationToken);
    }

    public void Add(VariationOrder variationOrder) => dbContext.VariationOrders.Add(variationOrder);

    public void AddAuditLogEntry(AuditLog entry) => dbContext.AuditLogs.Add(entry);

    /// <summary>
    /// Persists every staged change for this unit of work - the document's own mutation, any
    /// <see cref="Activity"/>/<see cref="Project"/> changes tracked on the same
    /// <see cref="CmPlusDbContext"/>, any manually-added <see cref="AuditLog"/> disclosure row
    /// (domain-rules.md §6.2), and - via <see cref="IApprovalActionRepository.Add"/> sharing the same
    /// scope - the new <see cref="ApprovalAction"/> row - as one atomic transaction. Returns
    /// <see langword="false"/> instead of throwing on a concurrent <see cref="VariationOrder.RowVersion"/>
    /// mismatch (409, never a double-advance) - identical translation to
    /// <see cref="PaymentCertificateRepository.TrySaveChangesAsync"/>.
    /// </summary>
    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
