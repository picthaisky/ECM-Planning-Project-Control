using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Issues;

/// <summary>Shared hand-written fakes for the S11-BE-03 issue-log handler test suites - mirrors this
/// codebase's established shared-fakes-per-feature convention (see
/// <c>CMPlus.Application.Tests.Features.Payment.PaymentApprovalTestFakes</c>).</summary>
internal sealed class FakeIssueLogRepository : IIssueLogRepository
{
    public bool ProjectExists { get; set; } = true;
    public List<IssueLog> Added { get; } = [];
    public bool SaveShouldSucceed { get; set; } = true;
    public int SaveCallCount { get; private set; }

    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ProjectExists);

    public Task<IssueLog?> FindAsync(Guid issueId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Added.SingleOrDefault(i => i.Id == issueId));

    public Task<IReadOnlyList<IssueLog>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IssueLog>>(Added.Where(i => i.ProjectId == projectId).ToList());

    public void Add(IssueLog issue) => Added.Add(issue);

    public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCallCount++;
        return Task.FromResult(SaveShouldSucceed);
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
