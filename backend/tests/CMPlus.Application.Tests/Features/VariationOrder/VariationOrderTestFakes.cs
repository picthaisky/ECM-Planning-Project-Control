using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;

namespace CMPlus.Application.Tests.Features.VariationOrder;

/// <summary>
/// S10-BE-01/02/03 unit-test doubles. Deliberately does NOT re-declare
/// <c>FakeApprovalActionRepository</c>/<c>FakeApprovalPolicyReaderForPayment</c>/
/// <c>FakeTenantProviderForPayment</c>/<c>FakeCurrentUserContextForPayment</c>/<c>FakeClockForPayment</c>/
/// <c>FakePaymentCertificateRepository</c> from <c>CMPlus.Application.Tests.Features.Payment</c> - all
/// six are already document-type-agnostic (none of their logic is actually Payment-specific despite
/// the "...ForPayment" naming), and they are <see langword="internal"/>, hence visible across
/// namespaces within this same test assembly. Reusing them here is the same "no rework" ADR-0008
/// staged Sprint 10 for the production code applied to its test doubles too. Only the two genuinely
/// new dependencies this feature introduces - <see cref="IVariationOrderRepository"/> and
/// <see cref="IProjectRepository"/> - get their own fake here.
/// </summary>
internal sealed class FakeVariationOrderRepository : IVariationOrderRepository
{
    private readonly Dictionary<Guid, CMPlus.Domain.Entities.VariationOrder> _variationOrders = [];
    private readonly Dictionary<Guid, Activity> _activities = [];

    public List<AuditLog> AddedAuditLogEntries { get; } = [];

    public bool SaveShouldSucceed { get; set; } = true;

    public int SaveCallCount { get; private set; }

    public void Seed(CMPlus.Domain.Entities.VariationOrder variationOrder) => _variationOrders[variationOrder.Id] = variationOrder;

    /// <summary>Registers an <see cref="Activity"/> as belonging to <paramref name="projectId"/> for
    /// <see cref="FindActivitiesForApprovalAsync"/> - the fake's stand-in for the real repository's
    /// <c>WBSNodes</c> join (this test double has no WBS graph to join against).</summary>
    public void SeedActivity(Guid projectId, Activity activity) => _activities[activity.Id] = activity;

    public Task<CMPlus.Domain.Entities.VariationOrder?> FindAsync(Guid variationOrderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_variationOrders.GetValueOrDefault(variationOrderId));

    public Task<CMPlus.Domain.Entities.VariationOrder?> GetByIdAsync(Guid variationOrderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_variationOrders.GetValueOrDefault(variationOrderId));

    public Task<IReadOnlyList<CMPlus.Domain.Entities.VariationOrder>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CMPlus.Domain.Entities.VariationOrder> rows = _variationOrders.Values
            .Where(v => v.ProjectId == projectId)
            .OrderByDescending(v => v.Id.ToString(), StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(rows);
    }

    public Task<bool> ExistsWithVoNumberAsync(Guid projectId, string voNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(_variationOrders.Values.Any(v => v.ProjectId == projectId && v.VoNumber == voNumber));

    public Task<decimal> GetApprovedNetSignedTotalAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var total = _variationOrders.Values
            .Where(v => v.ProjectId == projectId && v.Status == Domain.Enums.VariationOrderStatus.Approved)
            .Sum(v => v.Amount);

        return Task.FromResult(total);
    }

    public Task<IReadOnlyList<Activity>> FindActivitiesForApprovalAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Activity> found = activityIds
            .Where(id => _activities.ContainsKey(id))
            .Select(id => _activities[id])
            .ToList();

        return Task.FromResult(found);
    }

    public void Add(CMPlus.Domain.Entities.VariationOrder variationOrder) => _variationOrders[variationOrder.Id] = variationOrder;

    public void AddAuditLogEntry(AuditLog entry) => AddedAuditLogEntries.Add(entry);

    public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCallCount++;
        return Task.FromResult(SaveShouldSucceed);
    }
}

internal sealed class FakeProjectRepository : IProjectRepository
{
    private readonly Dictionary<Guid, Project> _projects = [];

    public int SaveChangesCallCount { get; private set; }

    public void Seed(Project project) => _projects[project.Id] = project;

    public Task<Project?> FindAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.GetValueOrDefault(projectId));

    public bool NextSaveHitsConcurrencyConflict { get; set; }

    public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(!NextSaveHitsConcurrencyConflict);
    }
}
