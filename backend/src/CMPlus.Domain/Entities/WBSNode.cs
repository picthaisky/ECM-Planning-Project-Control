using CMPlus.Domain.Common;

namespace CMPlus.Domain.Entities;

/// <summary>
/// A node in a project's Work Breakdown Structure hierarchy (docs/9 §4). Unlimited depth via a
/// self-referencing <see cref="ParentWbsNodeId"/> (naming per docs/db-conventions.md §1).
/// </summary>
public sealed class WBSNode : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid? ParentWbsNodeId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public decimal WeightPercentage { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private WBSNode()
    {
    }

    public WBSNode(
        Guid tenantId,
        Guid projectId,
        string code,
        string title,
        decimal weightPercentage,
        Guid? parentWbsNodeId = null)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Code = ValidateCode(code);
        Title = ValidateTitle(title);
        WeightPercentage = PercentageGuard.Clamp(weightPercentage);

        if (parentWbsNodeId == Id)
        {
            throw new DomainException($"WBSNode '{Id}' cannot be its own parent.");
        }

        ParentWbsNodeId = parentWbsNodeId;
    }

    public void Rename(string title) => Title = ValidateTitle(title);

    public void SetWeightPercentage(decimal weightPercentage) =>
        WeightPercentage = PercentageGuard.Clamp(weightPercentage);

    /// <summary>
    /// Re-parents this node. Rejects a re-parent that would make this node its own ancestor with
    /// a <see cref="DomainException"/> - the ancestor chain of the *proposed* parent is walked
    /// with an explicit, bounded <c>while</c> loop (never recursion), so neither a legitimately
    /// deep tree nor a pre-existing corrupt/cyclic chain elsewhere can cause a stack overflow
    /// (S1-BE-01 DoD). <paramref name="nodesInProjectById"/> must contain every WBSNode in the
    /// project keyed by <see cref="Id"/> (the caller resolves this once, e.g. via the WBS tree
    /// projection) so a single node does not need repository access to validate the invariant.
    /// </summary>
    public void SetParent(Guid? newParentId, IReadOnlyDictionary<Guid, WBSNode> nodesInProjectById)
    {
        if (newParentId is null)
        {
            ParentWbsNodeId = null;
            return;
        }

        if (newParentId.Value == Id)
        {
            throw new DomainException($"WBSNode '{Id}' cannot be its own parent.");
        }

        var visited = new HashSet<Guid>();
        Guid? currentId = newParentId;
        while (currentId is not null)
        {
            if (currentId.Value == Id)
            {
                throw new DomainException(
                    $"Re-parenting WBSNode '{Id}' under '{newParentId}' would make '{Id}' its own ancestor.");
            }

            if (!visited.Add(currentId.Value))
            {
                // Defensive: an already-corrupt chain elsewhere in the tree - stop rather than
                // loop forever walking the same nodes.
                throw new DomainException(
                    "Cannot validate re-parent: an existing cycle was detected in the WBS tree.");
            }

            currentId = nodesInProjectById.TryGetValue(currentId.Value, out var node)
                ? node.ParentWbsNodeId
                : null;
        }

        ParentWbsNodeId = newParentId;
    }

    private static string ValidateCode(string code) =>
        string.IsNullOrWhiteSpace(code)
            ? throw new DomainException("WBSNode code is required.")
            : code.Trim();

    private static string ValidateTitle(string title) =>
        string.IsNullOrWhiteSpace(title)
            ? throw new DomainException("WBSNode title is required.")
            : title.Trim();
}
