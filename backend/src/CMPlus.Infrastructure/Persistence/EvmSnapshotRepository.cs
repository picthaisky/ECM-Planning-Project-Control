using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S7-BE-05: <see cref="IEvmSnapshotRepository"/> against <see cref="CmPlusDbContext"/>.</summary>
public sealed class EvmSnapshotRepository(CmPlusDbContext dbContext) : IEvmSnapshotRepository
{
    public Task<bool> ExistsForDataDateAsync(Guid projectId, DateTimeOffset dataDate, CancellationToken cancellationToken = default) =>
        dbContext.EvmPeriodSnapshots
            .AsNoTracking()
            .AnyAsync(s => s.ProjectId == projectId && s.DataDate == dataDate, cancellationToken);

    public async Task AddAsync(EvmPeriodSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        dbContext.EvmPeriodSnapshots.Add(snapshot);

        // A single new entity - the default one-row AuditSaveChangesInterceptor behaviour is exactly
        // right here (S7-BE-05 is not a bulk operation).
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EvmPeriodSnapshot>> ListAsync(
        Guid projectId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.EvmPeriodSnapshots
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId);

        // Both bounds inclusive - a caller asking for a window ending on a closed period's own data
        // date means "include that period", not "stop just before it".
        if (from is not null)
        {
            query = query.Where(s => s.DataDate >= from.Value);
        }

        if (to is not null)
        {
            query = query.Where(s => s.DataDate <= to.Value);
        }

        return await query
            .OrderBy(s => s.DataDate)
            .ToListAsync(cancellationToken);
    }
}
