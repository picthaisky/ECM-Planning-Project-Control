using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Persistence boundary for the <see cref="IssueLog"/> aggregate (S11-BE-03). Carries a
/// <c>RowVersion</c> concurrency token per domain-rules.md §9.2 ("two users advancing simultaneously
/// means the second gets 409, never a double-advance"), so <see cref="TrySaveChangesAsync"/> mirrors
/// <see cref="IVariationOrderRepository.TrySaveChangesAsync"/>'s shape.
/// </summary>
public interface IIssueLogRepository
{
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Tracked (not <c>AsNoTracking</c>) - the caller mutates the returned instance via
    /// <see cref="IssueLog.AdvanceStatus"/> and this same instance is what <see cref="TrySaveChangesAsync"/>
    /// persists. Tenant-scoped by the global EF query filter (ADR-0002) - a cross-tenant or unknown
    /// id is indistinguishable, both simply resolve to <see langword="null"/>.</summary>
    Task<IssueLog?> FindAsync(Guid issueId, CancellationToken cancellationToken = default);

    /// <summary>Every issue for one project, in one round trip - the single source
    /// <c>ListIssuesQueryHandler</c> derives BOTH the table rows and the open/doing/closed tile
    /// counts from, which is what makes "the tile counter must match the real data" (S11-BE-03 DoD)
    /// a structural guarantee rather than a hope (this task's brief, and S8-QA's cross-screen
    /// consistency precedent).</summary>
    Task<IReadOnlyList<IssueLog>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    void Add(IssueLog issue);

    /// <summary>Returns <see langword="false"/> instead of throwing on a concurrent
    /// <see cref="IssueLog.RowVersion"/> mismatch (409, never a double-advance) - domain-rules.md
    /// §9.2, identical translation to <see cref="IVariationOrderRepository.TrySaveChangesAsync"/>.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default);
}
