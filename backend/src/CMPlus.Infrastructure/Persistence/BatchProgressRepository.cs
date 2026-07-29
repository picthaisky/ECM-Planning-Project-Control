using System.Text.Json;
using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S4-BE-03: <see cref="IBatchProgressRepository"/> against <see cref="CmPlusDbContext"/>.
/// Mirrors <see cref="ImportRepository"/>'s bulk-commit shape (Sprint 3 precedent).</summary>
public sealed class BatchProgressRepository(
    CmPlusDbContext dbContext, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IBatchProgressRepository
{
    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Activity>> LoadActivitiesForProjectAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default)
    {
        if (activityIds.Count == 0)
        {
            return new Dictionary<Guid, Activity>();
        }

        var idList = activityIds.ToList();

        // Deliberately tracked (no AsNoTracking) - the caller mutates these via
        // Activity.RecordProgress and SaveBatchAsync relies on EF Core's change tracker to detect
        // and persist the resulting ProgressPercentage cache update (same pattern as
        // ImportRepository.LoadActivitiesForProjectAsync).
        var activities = await dbContext.Activities
            .Where(a => idList.Contains(a.Id) && dbContext.WBSNodes.Any(w => w.Id == a.WbsNodeId && w.ProjectId == projectId))
            .ToListAsync(cancellationToken);

        return activities.ToDictionary(a => a.Id);
    }

    public async Task SaveBatchAsync(
        Guid projectId,
        DateTimeOffset periodEndDate,
        IReadOnlyList<ActivityProgressLog> newEntries,
        CancellationToken cancellationToken = default)
    {
        dbContext.SuppressPerEntityAudit = true;

        try
        {
            dbContext.ActivityProgressLogs.AddRange(newEntries);
            // The owning Activity entities are already tracked (loaded via
            // LoadActivitiesForProjectAsync on this same DbContext) and already mutated in-memory by
            // Activity.RecordProgress - EF Core picks up their Modified state automatically.

            // One summarizing AuditLog row for the whole batch (S4-BE-03 DoD), anchored on the
            // Project the batch belongs to - there is no single natural per-activity anchor for a
            // cross-activity batch operation, and "which project changed" is the discovery axis a
            // PM/QS actually queries by.
            dbContext.AuditLogs.Add(new AuditLog(
                tenantProvider.TenantId, nameof(Project), projectId, AuditAction.Updated, currentUser.UserId,
                beforeJson: null,
                afterJson: JsonSerializer.Serialize(new
                {
                    ProjectId = projectId,
                    PeriodEndDate = periodEndDate,
                    EntriesRecorded = newEntries.Count,
                }),
                clock.UtcNow));

            // A single SaveChangesAsync call is one atomic transaction (S4-BE-03 DoD: "the whole
            // batch is one transaction, all-or-nothing") - EF Core wraps every tracked change made
            // in this call in one implicit transaction.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.SuppressPerEntityAudit = false;
        }
    }
}
