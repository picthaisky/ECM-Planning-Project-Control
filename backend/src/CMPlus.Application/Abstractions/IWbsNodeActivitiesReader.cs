using CMPlus.Application.Wbs;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// S4-BE-05: reads the activities under one WBS node - the natural complement
/// <see cref="IWbsTreeReader"/>'s own remarks name as future work ("a project's full activity list
/// per node is fetched lazily/paginated by a separate query, out of that sprint's scope").
/// Implemented in Infrastructure against <c>CmPlusDbContext</c> (ADR-0001).
/// </summary>
public interface IWbsNodeActivitiesReader
{
    /// <summary>
    /// True only if <paramref name="wbsNodeId"/> both exists and belongs to
    /// <paramref name="projectId"/> specifically - tenant-scoped by the global EF query filter
    /// (ADR-0002) and additionally scoped to this project, so a node id that belongs to a
    /// different project in the same tenant cannot leak its activities through the wrong
    /// project's route segment.
    /// </summary>
    Task<bool> NodeExistsInProjectAsync(Guid projectId, Guid wbsNodeId, CancellationToken cancellationToken = default);

    /// <summary>Every <c>Activity</c> under this node, ordered by <c>ActivityCode</c> - not
    /// paginated at this sprint's scope (a single WBS node's activity count is small relative to a
    /// whole project's ~10,000, unlike the tree read itself which explicitly avoids embedding
    /// activities for that reason).</summary>
    Task<IReadOnlyList<ActivityForProgressDto>> GetActivitiesForNodeAsync(
        Guid wbsNodeId, CancellationToken cancellationToken = default);
}
