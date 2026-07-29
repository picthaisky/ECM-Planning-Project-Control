using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// A CPM precedence relation between two activities (docs/9 §4). Naming per
/// docs/db-conventions.md §1: <see cref="PredecessorActivityId"/>, <see cref="SuccessorActivityId"/>,
/// <see cref="RelationType"/>, <see cref="LagDays"/> (signed - negative = lead). Only the trivial
/// self-loop is rejected here; validating that the whole relation *graph* is acyclic is the
/// Sprint 5 CPM engine's job (a single entity cannot see the whole graph), not a Sprint 1 entity
/// invariant.
/// </summary>
public sealed class ActivityRelation : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public Guid PredecessorActivityId { get; private set; }

    public Guid SuccessorActivityId { get; private set; }

    public RelationType RelationType { get; private set; }

    public int LagDays { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private ActivityRelation()
    {
    }

    public ActivityRelation(
        Guid tenantId,
        Guid predecessorActivityId,
        Guid successorActivityId,
        RelationType relationType,
        int lagDays = 0)
    {
        if (predecessorActivityId == successorActivityId)
        {
            throw new DomainException("An activity cannot have a relation to itself.");
        }

        TenantId = tenantId;
        PredecessorActivityId = predecessorActivityId;
        SuccessorActivityId = successorActivityId;
        RelationType = relationType;
        LagDays = lagDays;
    }

    public void SetLagDays(int lagDays) => LagDays = lagDays;
}
