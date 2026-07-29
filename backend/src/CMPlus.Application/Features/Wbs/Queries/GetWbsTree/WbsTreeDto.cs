namespace CMPlus.Application.Features.Wbs.Queries.GetWbsTree;

/// <summary>S4-BE-01 response shape: the whole WBS node hierarchy for a project, nested.</summary>
public sealed record WbsTreeDto(Guid ProjectId, IReadOnlyList<WbsTreeNodeDto> RootNodes);

/// <summary>
/// One WBS node plus its nested <see cref="Children"/>. <see cref="ActivityCount"/> is a rolled-up
/// count only - the activities themselves are not embedded (US-4.1: lazy/paginated child-loading),
/// so this payload's size is proportional to the (bounded, ~5,000) node count, never to the total
/// activity count across the project.
/// </summary>
public sealed record WbsTreeNodeDto(
    Guid Id,
    Guid? ParentWbsNodeId,
    string Code,
    string Title,
    decimal WeightPercentage,
    int ActivityCount,
    IReadOnlyList<WbsTreeNodeDto> Children);
