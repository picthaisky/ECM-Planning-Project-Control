using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S11-BE-01: <see cref="IDailyWeatherLogRepository"/> against <see cref="CmPlusDbContext"/> -
/// the persistence boundary for <c>RecordWeatherLogCommand</c>/<c>RecordWeatherLogCorrectionCommand</c>/
/// <c>ListWeatherLogsQuery</c>.</summary>
public sealed class DailyWeatherLogRepository(CmPlusDbContext dbContext) : IDailyWeatherLogRepository
{
    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, cancellationToken);

    public Task<DailyWeatherLog?> GetByIdAsync(Guid projectId, Guid logId, CancellationToken cancellationToken = default) =>
        dbContext.DailyWeatherLogs.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == logId && w.ProjectId == projectId, cancellationToken);

    public Task<bool> HasAnyCorrectionTargetingAsync(Guid projectId, Guid targetLogId, CancellationToken cancellationToken = default) =>
        dbContext.DailyWeatherLogs.AsNoTracking()
            .AnyAsync(w => w.ProjectId == projectId && w.CorrectsWeatherLogId == targetLogId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> FindExistingActivityIdsAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default)
    {
        if (activityIds.Count == 0)
        {
            return [];
        }

        var idList = activityIds.ToList();

        return await dbContext.Activities.AsNoTracking()
            .Where(a => idList.Contains(a.Id) && dbContext.WBSNodes.Any(n => n.Id == a.WbsNodeId && n.ProjectId == projectId))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(DailyWeatherLog log, CancellationToken cancellationToken = default)
    {
        dbContext.DailyWeatherLogs.Add(log);

        // A single new aggregate (the log plus its owned AffectedActivities rows) - the default
        // one-row-per-changed-entity AuditSaveChangesInterceptor behaviour is exactly right here
        // (not a bulk operation).
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DailyWeatherLog>> ListByProjectAsync(
        Guid projectId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
    {
        var query = dbContext.DailyWeatherLogs
            .AsNoTracking()
            .Include(w => w.AffectedActivities)
            .Where(w => w.ProjectId == projectId);

        if (from is { } fromDate)
        {
            query = query.Where(w => w.LogDate >= fromDate);
        }

        if (to is { } toDate)
        {
            query = query.Where(w => w.LogDate <= toDate);
        }

        return await query
            .OrderByDescending(w => w.LogDate)
            .ThenByDescending(w => w.RecordedAt)
            .ToListAsync(cancellationToken);
    }
}
