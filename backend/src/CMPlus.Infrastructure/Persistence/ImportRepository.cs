using System.Text.Json;
using CMPlus.Application.Abstractions;
using CMPlus.Application.Import;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// S3-BE-04: <see cref="IImportRepository"/> against <see cref="CmPlusDbContext"/>. The two "commit"
/// methods are the only places in the codebase that set
/// <see cref="CmPlusDbContext.SuppressPerEntityAudit"/> - see that property's doc comment for why.
/// </summary>
public sealed class ImportRepository(
    CmPlusDbContext dbContext, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IImportRepository
{
    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, cancellationToken);

    public async Task SaveFailedJobAsync(FileImportJob job, CancellationToken cancellationToken = default)
    {
        dbContext.FileImportJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveScheduleImportAsync(FileImportJob job, ParsedSchedule schedule, CancellationToken cancellationToken = default)
    {
        var previousAutoDetect = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        dbContext.SuppressPerEntityAudit = true;
        // db-conventions.md §8 bulk-insert guidance: AddRange + a single SaveChangesAsync with
        // AutoDetectChangesEnabled = false during the bulk add - not per-row SaveChanges, not a
        // second NuGet dependency (EFCore.BulkExtensions/SqlBulkCopy) this sprint (see the
        // backend-developer Sprint 3 report for why: SqlBulkCopy is untestable against the
        // EF Core InMemory provider this solution's integration tests use, and AddRange+SaveChanges
        // is one of db-conventions.md's two explicitly sanctioned approaches).
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            dbContext.FileImportJobs.Add(job);
            dbContext.WBSNodes.AddRange(schedule.WbsNodes);
            dbContext.Activities.AddRange(schedule.Activities);
            dbContext.ActivityRelations.AddRange(schedule.Relations);

            dbContext.AuditLogs.Add(new AuditLog(
                tenantProvider.TenantId, nameof(FileImportJob), job.Id, AuditAction.Created, currentUser.UserId,
                beforeJson: null,
                afterJson: JsonSerializer.Serialize(new
                {
                    job.FileName,
                    job.Format,
                    WbsNodes = schedule.WbsNodes.Count,
                    Activities = schedule.Activities.Count,
                    Relations = schedule.Relations.Count,
                }),
                clock.UtcNow));

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.SuppressPerEntityAudit = false;
            dbContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetect;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, Activity>> LoadActivitiesForProjectAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default)
    {
        if (activityIds.Count == 0)
        {
            return new Dictionary<Guid, Activity>();
        }

        var idList = activityIds.ToList();

        // Deliberately tracked (no AsNoTracking) - the caller mutates these via
        // Activity.RecordProgress and SaveProgressImportAsync relies on EF Core's change tracker
        // to detect and persist the resulting ProgressPercentage cache update.
        var activities = await dbContext.Activities
            .Where(a => idList.Contains(a.Id) && dbContext.WBSNodes.Any(w => w.Id == a.WbsNodeId && w.ProjectId == projectId))
            .ToListAsync(cancellationToken);

        return activities.ToDictionary(a => a.Id);
    }

    public async Task SaveProgressImportAsync(
        FileImportJob job, IReadOnlyList<ActivityProgressLog> newEntries, CancellationToken cancellationToken = default)
    {
        dbContext.SuppressPerEntityAudit = true;

        try
        {
            dbContext.FileImportJobs.Add(job);
            dbContext.ActivityProgressLogs.AddRange(newEntries);
            // The owning Activity entities are already tracked (loaded via
            // LoadActivitiesForProjectAsync on this same DbContext) and already mutated in-memory
            // by Activity.RecordProgress - EF Core picks up their Modified state automatically.

            dbContext.AuditLogs.Add(new AuditLog(
                tenantProvider.TenantId, nameof(FileImportJob), job.Id, AuditAction.Created, currentUser.UserId,
                beforeJson: null,
                afterJson: JsonSerializer.Serialize(new { job.FileName, ProgressLogRowsCreated = newEntries.Count }),
                clock.UtcNow));

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.SuppressPerEntityAudit = false;
        }
    }

    public Task<FileImportJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        dbContext.FileImportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

    public async Task<IReadOnlyList<FileImportJob>> GetJobHistoryAsync(
        Guid projectId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        await dbContext.FileImportJobs
            .AsNoTracking()
            .Where(j => j.ProjectId == projectId)
            .OrderByDescending(j => j.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ActivityProgressTemplateRow>> GetActivityTemplateRowsAsync(
        Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.Activities
            .AsNoTracking()
            .Where(a => dbContext.WBSNodes.Any(w => w.Id == a.WbsNodeId && w.ProjectId == projectId))
            .OrderBy(a => a.ActivityCode)
            .Select(a => new ActivityProgressTemplateRow(a.Id, a.ActivityCode, a.Name, a.ProgressPercentage))
            .ToListAsync(cancellationToken);
}
