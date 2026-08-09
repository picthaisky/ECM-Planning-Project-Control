using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S8: <see cref="IActualCostRepository"/> against <see cref="CmPlusDbContext"/> - the
/// write-path persistence boundary for <c>RecordActualCostCommand</c> (actual-cost.md §9/§12).</summary>
public sealed class ActualCostRepository(CmPlusDbContext dbContext) : IActualCostRepository
{
    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, cancellationToken);

    public Task<bool> WbsNodeExistsAsync(Guid projectId, Guid wbsNodeId, CancellationToken cancellationToken = default) =>
        dbContext.WBSNodes.AsNoTracking()
            .AnyAsync(n => n.Id == wbsNodeId && n.ProjectId == projectId, cancellationToken);

    public Task<bool> ActivityExistsAsync(Guid projectId, Guid activityId, CancellationToken cancellationToken = default) =>
        dbContext.Activities.AsNoTracking()
            .AnyAsync(a => a.Id == activityId && dbContext.WBSNodes.Any(n => n.Id == a.WbsNodeId && n.ProjectId == projectId), cancellationToken);

    public Task<bool> EntryExistsAsync(Guid projectId, Guid entryId, CancellationToken cancellationToken = default) =>
        dbContext.ActualCostEntries.AsNoTracking()
            .AnyAsync(e => e.Id == entryId && e.ProjectId == projectId, cancellationToken);

    public Task<bool> ClosedPeriodExistsOnOrAfterAsync(Guid projectId, DateTimeOffset incurredDate, CancellationToken cancellationToken = default) =>
        dbContext.EvmPeriodSnapshots.AsNoTracking()
            .AnyAsync(s => s.ProjectId == projectId && s.DataDate >= incurredDate, cancellationToken);

    public async Task AddAsync(ActualCostEntry entry, CancellationToken cancellationToken = default)
    {
        dbContext.ActualCostEntries.Add(entry);

        // A single new entity - the default one-row AuditSaveChangesInterceptor behaviour is exactly
        // right here (not a bulk operation).
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
