using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S12-BE-02: <see cref="IManpowerEquipmentLogRepository"/> against
/// <see cref="CmPlusDbContext"/> - the persistence boundary for
/// <c>RecordManpowerLogCommand</c>/<c>RecordManpowerLogCorrectionCommand</c>.</summary>
public sealed class ManpowerEquipmentLogRepository(CmPlusDbContext dbContext) : IManpowerEquipmentLogRepository
{
    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> FindExistingWorkCategoryIdsAsync(
        Guid projectId, IReadOnlyCollection<Guid> workCategoryIds, CancellationToken cancellationToken = default)
    {
        if (workCategoryIds.Count == 0)
        {
            return [];
        }

        var idList = workCategoryIds.ToList();
        return await dbContext.WorkCategories.AsNoTracking()
            .Where(w => idList.Contains(w.Id) && (w.ProjectId == null || w.ProjectId == projectId))
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> FindExistingWbsNodeIdsAsync(
        Guid projectId, IReadOnlyCollection<Guid> wbsNodeIds, CancellationToken cancellationToken = default)
    {
        if (wbsNodeIds.Count == 0)
        {
            return [];
        }

        var idList = wbsNodeIds.ToList();
        return await dbContext.WBSNodes.AsNoTracking()
            .Where(n => idList.Contains(n.Id) && n.ProjectId == projectId)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> FindWbsNodeIdsInTenantAsync(
        IReadOnlyCollection<Guid> wbsNodeIds, CancellationToken cancellationToken = default)
    {
        if (wbsNodeIds.Count == 0)
        {
            return [];
        }

        var idList = wbsNodeIds.ToList();
        // No ProjectId predicate - tenant scoping alone comes from the global EF query filter
        // (ADR-0002). Used only to distinguish "different project, same tenant" from "not in this
        // tenant at all" - never to bypass tenant scoping.
        return await dbContext.WBSNodes.AsNoTracking()
            .Where(n => idList.Contains(n.Id))
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> FindExistingActivitiesWithWbsNodeAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default)
    {
        if (activityIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var idList = activityIds.ToList();
        var rows = await dbContext.Activities.AsNoTracking()
            .Where(a => idList.Contains(a.Id) && dbContext.WBSNodes.Any(n => n.Id == a.WbsNodeId && n.ProjectId == projectId))
            .Select(a => new { a.Id, a.WbsNodeId })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => r.WbsNodeId);
    }

    public async Task<IReadOnlyList<Guid>> FindActivityIdsInTenantAsync(
        IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default)
    {
        if (activityIds.Count == 0)
        {
            return [];
        }

        var idList = activityIds.ToList();
        return await dbContext.Activities.AsNoTracking()
            .Where(a => idList.Contains(a.Id))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<ManpowerEquipmentLog?> GetByIdAsync(Guid projectId, Guid logId, CancellationToken cancellationToken = default) =>
        dbContext.ManpowerEquipmentLogs.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == logId && m.ProjectId == projectId, cancellationToken);

    public Task<bool> HasAnyCorrectionTargetingAsync(Guid projectId, Guid targetLogId, CancellationToken cancellationToken = default) =>
        dbContext.ManpowerEquipmentLogs.AsNoTracking()
            .AnyAsync(m => m.ProjectId == projectId && m.CorrectsLogId == targetLogId, cancellationToken);

    public async Task<bool> HasInForceOriginalForNaturalKeyAsync(
        Guid projectId,
        DateTimeOffset logDate,
        Shift shift,
        Guid workCategoryId,
        Guid? wbsNodeId,
        LabourType labourType,
        string? subcontractorRef,
        CancellationToken cancellationToken = default)
    {
        var candidates = await dbContext.ManpowerEquipmentLogs.AsNoTracking()
            .Where(m =>
                m.ProjectId == projectId
                && m.LogDate == logDate
                && m.Shift == shift
                && m.WorkCategoryId == workCategoryId
                && m.WbsNodeId == wbsNodeId
                && m.LabourType == labourType
                && m.SubcontractorRef == subcontractorRef)
            .Select(m => new { m.Id, m.EntryKind })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return false;
        }

        var candidateIds = candidates.Select(c => c.Id).ToList();
        var pointedAt = await dbContext.ManpowerEquipmentLogs.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.CorrectsLogId != null && candidateIds.Contains(m.CorrectsLogId!.Value))
            .Select(m => m.CorrectsLogId!.Value)
            .ToListAsync(cancellationToken);
        var pointedAtSet = pointedAt.ToHashSet();

        // §4.7's effective-set rule: in force iff nothing points at it and it is not itself a
        // Retraction (a Retraction removes both itself and its target from the effective set).
        return candidates.Any(c => c.EntryKind != ManpowerLogEntryKind.Retraction && !pointedAtSet.Contains(c.Id));
    }

    public async Task AddAsync(ManpowerEquipmentLog log, CancellationToken cancellationToken = default)
    {
        dbContext.ManpowerEquipmentLogs.Add(log);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
