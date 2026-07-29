namespace CMPlus.Application.Wbs;

/// <summary>
/// S4-BE-01: a single flat, lean projection of a <c>WBSNode</c> row plus its rolled-up
/// <see cref="ActivityCount"/> - never the full <c>WBSNode</c>/<c>Activity</c> entities. Returned by
/// <see cref="CMPlus.Application.Abstractions.IWbsTreeReader"/> in one shot for every node in a
/// project (no per-node round trip - see that interface's remarks on why this avoids the classic
/// "load node, then load its children" N+1 trap). <c>WbsTreeBuilder</c>
/// (<c>Features/Wbs/Queries/GetWbsTree</c>) nests these into the response tree in memory.
/// </summary>
public sealed record WbsNodeFlatRow(
    Guid Id,
    Guid? ParentWbsNodeId,
    string Code,
    string Title,
    decimal WeightPercentage,
    int ActivityCount);
