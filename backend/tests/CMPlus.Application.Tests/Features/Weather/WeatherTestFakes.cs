using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Weather;

/// <summary>Shared hand-written fakes for the S11-BE-01 weather-log handler test suites - mirrors
/// this codebase's established shared-fakes-per-feature convention (see
/// <c>CMPlus.Application.Tests.Features.Payment.PaymentApprovalTestFakes</c>).</summary>
internal sealed class FakeDailyWeatherLogRepository : IDailyWeatherLogRepository
{
    public bool ProjectExists { get; set; } = true;
    public Dictionary<Guid, DailyWeatherLog> LogsById { get; } = [];
    public HashSet<Guid> ExistingActivityIds { get; set; } = [];
    public DailyWeatherLog? AddedLog { get; private set; }
    public int AddCallCount { get; private set; }
    public IReadOnlyList<DailyWeatherLog> RowsToList { get; set; } = [];
    public (Guid ProjectId, DateTimeOffset? From, DateTimeOffset? To)? LastListCall { get; private set; }

    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ProjectExists);

    public Task<DailyWeatherLog?> GetByIdAsync(Guid projectId, Guid logId, CancellationToken cancellationToken = default) =>
        Task.FromResult(LogsById.TryGetValue(logId, out var log) && log.ProjectId == projectId ? log : null);

    public Task<bool> HasAnyCorrectionTargetingAsync(Guid projectId, Guid targetLogId, CancellationToken cancellationToken = default) =>
        Task.FromResult(LogsById.Values.Any(l => l.ProjectId == projectId && l.CorrectsWeatherLogId == targetLogId));

    public Task<IReadOnlyList<Guid>> FindExistingActivityIdsAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(activityIds.Where(id => ExistingActivityIds.Contains(id)).ToList());

    public Task AddAsync(DailyWeatherLog log, CancellationToken cancellationToken = default)
    {
        AddedLog = log;
        AddCallCount++;
        LogsById[log.Id] = log;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DailyWeatherLog>> ListByProjectAsync(
        Guid projectId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
    {
        LastListCall = (projectId, from, to);
        return Task.FromResult(RowsToList);
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
