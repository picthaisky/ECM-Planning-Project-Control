using CMPlus.Application.Gantt;
using CMPlus.Application.Wbs;

namespace CMPlus.Application.Features.Gantt.Queries.GetGantt;

/// <summary>
/// S6-BE-01: nests a flat <see cref="WbsNodeFlatRow"/> list (the WBS hierarchy, reused verbatim from
/// <c>IWbsTreeReader</c> - see <see cref="CMPlus.Application.Abstractions.IGanttActivityReader"/>'s
/// remarks on why a second hierarchy read was not invented) and a flat
/// <see cref="GanttActivityFlatRow"/> list (the bars, possibly date-windowed) into one flat,
/// already-ordered <see cref="GanttDto"/> - pure, in-memory, no I/O, independently unit testable,
/// same "flat rows in, pure builder, flat/nested rows out" shape as <c>WbsTreeBuilder</c> rather
/// than a third payload convention.
///
/// <para>Row order mirrors how the frontend renders the WBS tree with activity leaves inserted
/// under their owning node: depth-first, siblings ordered by <c>Code</c>/<c>ActivityCode</c>
/// (matching <c>WbsTreeBuilder</c>'s own sibling order), a node's own activities emitted
/// immediately after the node itself and before recursing into its child nodes. This is a modelling
/// decision this feature had to resolve itself - the WBS tree (Sprint 4) never nested activities
/// under a node in the first place (only a rolled-up <c>ActivityCount</c>), so there is no existing
/// "correct" interleave order to copy; "own activities before child nodes" was chosen as the more
/// common convention in WBS/Gantt tooling (a node's directly-assigned work listed before its
/// sub-elements) and is easy for the frontend to reason about deterministically.</para>
/// </summary>
public static class GanttRowOrderer
{
    /// <summary>Sentinel key standing in for "no parent" (a root node) - mirrors
    /// <c>WbsTreeBuilder.RootSentinel</c>'s reasoning verbatim (a real WBSNode id is always a
    /// <see cref="Guid.CreateVersion7"/> value, never <see cref="Guid.Empty"/>).</summary>
    private static readonly Guid RootSentinel = Guid.Empty;

    public static GanttDto Build(
        Guid projectId,
        IReadOnlyList<WbsNodeFlatRow> nodes,
        IReadOnlyList<GanttActivityFlatRow> activities)
    {
        var childNodesByParent = new Dictionary<Guid, List<WbsNodeFlatRow>>();
        foreach (var node in nodes.OrderBy(n => n.Code, StringComparer.Ordinal))
        {
            var parentKey = node.ParentWbsNodeId ?? RootSentinel;
            if (!childNodesByParent.TryGetValue(parentKey, out var siblings))
            {
                siblings = [];
                childNodesByParent[parentKey] = siblings;
            }

            siblings.Add(node);
        }

        var activitiesByNode = new Dictionary<Guid, List<GanttActivityFlatRow>>();
        foreach (var activity in activities.OrderBy(a => a.ActivityCode, StringComparer.Ordinal))
        {
            if (!activitiesByNode.TryGetValue(activity.WbsNodeId, out var siblings))
            {
                siblings = [];
                activitiesByNode[activity.WbsNodeId] = siblings;
            }

            siblings.Add(activity);
        }

        var orderedActivities = new List<GanttActivityDto>(activities.Count);
        var emittedActivityIds = new HashSet<Guid>();
        var visitedNodeIds = new HashSet<Guid>();

        // Explicit stack-based DFS - never recursion, same stack-safety discipline
        // WBSNode.SetParent/WbsTreeBuilder already establish in this codebase, so a
        // pathologically deep WBS tree cannot overflow the call stack here either.
        var pendingNodeIds = new Stack<Guid>();
        if (childNodesByParent.TryGetValue(RootSentinel, out var rootNodes))
        {
            // Pushed in reverse so the stack pops roots back into their original Code order.
            for (var i = rootNodes.Count - 1; i >= 0; i--)
            {
                pendingNodeIds.Push(rootNodes[i].Id);
            }
        }

        while (pendingNodeIds.Count > 0)
        {
            var nodeId = pendingNodeIds.Pop();
            if (!visitedNodeIds.Add(nodeId))
            {
                // Defensive: never re-enter an already-visited node (a stray cyclic/corrupt chain
                // elsewhere - a read path must not trust write-time invariants structurally, same
                // philosophy as WbsTreeBuilder).
                continue;
            }

            if (activitiesByNode.TryGetValue(nodeId, out var ownActivities))
            {
                foreach (var activity in ownActivities)
                {
                    orderedActivities.Add(ToDto(activity));
                    emittedActivityIds.Add(activity.Id);
                }
            }

            if (childNodesByParent.TryGetValue(nodeId, out var childNodes))
            {
                for (var i = childNodes.Count - 1; i >= 0; i--)
                {
                    pendingNodeIds.Push(childNodes[i].Id);
                }
            }
        }

        // Defensive: an activity whose WbsNodeId is not reachable from any root (should never
        // happen given the FK plus WBSNode.SetParent's own cycle guard, but a read path must not
        // *trust* that structurally) is appended rather than silently dropped - unlike an
        // unreachable WBS grouping node (which carries no data of its own, and WbsTreeBuilder
        // correctly omits it), an orphaned Activity is real schedule data the Gantt must never hide.
        foreach (var activity in activities.OrderBy(a => a.ActivityCode, StringComparer.Ordinal))
        {
            if (!emittedActivityIds.Contains(activity.Id))
            {
                orderedActivities.Add(ToDto(activity));
            }
        }

        // DataDate is not this builder's concern (it has no reason to read Project at all - see
        // this class's own "pure, in-memory, no I/O" contract) - the caller (GetGanttQueryHandler)
        // always overwrites this placeholder via `with { DataDate = ... }` immediately after Build
        // returns, so DateTimeOffset.MinValue here is never actually observed by anything real.
        return new GanttDto(projectId, DateTimeOffset.MinValue, orderedActivities);
    }

    private static GanttActivityDto ToDto(GanttActivityFlatRow activity) => new(
        activity.Id,
        activity.WbsNodeId,
        activity.ActivityCode,
        activity.Name,
        activity.PlannedStart,
        activity.PlannedFinish,
        activity.ActualStart,
        activity.ActualFinish,
        activity.IsCritical,
        activity.TotalFloat,
        activity.FreeFloat);
}
