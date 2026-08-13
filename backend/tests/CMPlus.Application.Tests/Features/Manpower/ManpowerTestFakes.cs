using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Manpower;

/// <summary>Shared hand-written fake for the S12-BE-02 manpower-log handler test suites - mirrors
/// this codebase's established shared-fakes-per-feature convention
/// (<c>CMPlus.Application.Tests.Features.Weather.FakeDailyWeatherLogRepository</c>).</summary>
internal sealed class FakeManpowerEquipmentLogRepository : IManpowerEquipmentLogRepository
{
    public bool ProjectExists { get; set; } = true;
    public HashSet<Guid> ExistingWorkCategoryIds { get; set; } = [];
    public HashSet<Guid> ExistingWbsNodeIds { get; set; } = [];
    public HashSet<Guid> WbsNodeIdsInTenant { get; set; } = [];
    public Dictionary<Guid, Guid> ExistingActivitiesWithWbsNode { get; set; } = [];
    public HashSet<Guid> ActivityIdsInTenant { get; set; } = [];
    public Dictionary<Guid, ManpowerEquipmentLog> LogsById { get; } = [];
    public bool HasInForceOriginalForNaturalKey { get; set; }
    public ManpowerEquipmentLog? AddedLog { get; private set; }
    public int AddCallCount { get; private set; }

    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ProjectExists);

    public List<WorkCategory> WorkCategoriesToReturn { get; set; } = [];

    public Task<IReadOnlyList<Guid>> FindExistingWorkCategoryIdsAsync(
        Guid projectId, IReadOnlyCollection<Guid> workCategoryIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(workCategoryIds.Where(ExistingWorkCategoryIds.Contains).ToList());

    public Task<IReadOnlyList<WorkCategory>> ListWorkCategoriesForProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkCategory>>(WorkCategoriesToReturn);

    public Task<IReadOnlyList<Guid>> FindExistingWbsNodeIdsAsync(
        Guid projectId, IReadOnlyCollection<Guid> wbsNodeIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(wbsNodeIds.Where(ExistingWbsNodeIds.Contains).ToList());

    public Task<IReadOnlyList<Guid>> FindWbsNodeIdsInTenantAsync(
        IReadOnlyCollection<Guid> wbsNodeIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(wbsNodeIds.Where(WbsNodeIdsInTenant.Contains).ToList());

    public Task<IReadOnlyDictionary<Guid, Guid>> FindExistingActivitiesWithWbsNodeAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(
            activityIds.Where(ExistingActivitiesWithWbsNode.ContainsKey)
                .ToDictionary(id => id, id => ExistingActivitiesWithWbsNode[id]));

    public Task<IReadOnlyList<Guid>> FindActivityIdsInTenantAsync(
        IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(activityIds.Where(ActivityIdsInTenant.Contains).ToList());

    public Task<ManpowerEquipmentLog?> GetByIdAsync(Guid projectId, Guid logId, CancellationToken cancellationToken = default) =>
        Task.FromResult(LogsById.TryGetValue(logId, out var log) && log.ProjectId == projectId ? log : null);

    public Task<bool> HasAnyCorrectionTargetingAsync(Guid projectId, Guid targetLogId, CancellationToken cancellationToken = default) =>
        Task.FromResult(LogsById.Values.Any(l => l.ProjectId == projectId && l.CorrectsLogId == targetLogId));

    public Task<bool> HasInForceOriginalForNaturalKeyAsync(
        Guid projectId, DateTimeOffset logDate, Shift shift, Guid workCategoryId, Guid? wbsNodeId,
        LabourType labourType, string? subcontractorRef, CancellationToken cancellationToken = default) =>
        Task.FromResult(HasInForceOriginalForNaturalKey);

    public Task AddAsync(ManpowerEquipmentLog log, CancellationToken cancellationToken = default)
    {
        AddedLog = log;
        AddCallCount++;
        LogsById[log.Id] = log;
        return Task.CompletedTask;
    }
}

internal sealed class FakeTenantProvider(Guid tenantId) : ITenantProvider
{
    public Guid TenantId { get; } = tenantId;
}

internal sealed class FakeCurrentUserContext(Guid? userId) : ICurrentUserContext
{
    public Guid? UserId { get; } = userId;

    public UserRole Role => UserRole.Site;
}

internal sealed class FakeClock(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = now;
}
