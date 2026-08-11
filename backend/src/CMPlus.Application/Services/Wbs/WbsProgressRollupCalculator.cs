using CMPlus.Domain.Common;

namespace CMPlus.Application.Services.Wbs;

/// <summary>
/// S8-BE-02/US-8.3: pure Application-layer implementation of
/// `.claude/knowledge/domain/evm-formulas.md`'s "Progress rollup (WBS tree)" rule -
/// `Pct_parent = sum(Pct_c . W_c) / sum(W_c)` using each child's own <c>WeightPercentage</c>,
/// applied bottom-up from the WBS tree's leaves to the project-level total. No dependency on EF
/// Core/MediatR anywhere in this class (ADR-0001, same discipline as
/// <see cref="CMPlus.Application.Services.Evm.EvmEngine"/>) - unit-testable with nothing but plain
/// CLR records/decimals.
///
/// <para><b>This is a schedule/progress-only calculation, not an EVM-engine metric.</b> It never
/// touches <c>AC</c>/cost data and does not go through
/// <see cref="CMPlus.Application.Features.Evm.EvmComputationService"/> - S8-BE-01's "no calculation
/// outside the EVM engine" rule governs the money-shaped metrics (PV/EV/AC/SV/CV/SPI/CPI/EAC/ETC/
/// VAC); the WBS weight-based rollup is evm-formulas.md's own, separate, sibling formula and has no
/// engine of its own to reuse. Reusing readers is still expected where they already exist - S8-BE-02
/// reuses <c>IWbsTreeReader</c> for the node/weight shape rather than adding a duplicate query.</para>
///
/// <para><b>Leaf-level progress (resolved ambiguity):</b> evm-formulas.md defines the parent-to-
/// child rollup but not how a leaf node's own progress is derived from its <c>Activity</c> rows -
/// no such formula exists anywhere in the domain knowledge base. This implementation uses a
/// <c>BudgetCost</c>-weighted average of the leaf's own activities'
/// <see cref="WbsRollupActivityInput.ProgressPercentage"/> (falling back to an equal-weight average
/// only when every activity under that node is genuinely zero-budget, so a fully-complete
/// zero-budget scope never reads as "not started"). This mirrors the same budget-weighting
/// principle `EV = sum(BudgetCost_i . Pct_i / 100)` already uses project-wide, and is the natural
/// choice given `WBSNode.WeightPercentage` is documented as "budget-proportional" - but it is a
/// genuine resolved-ambiguity call, the same shape as <c>EvmEngine.ComputePlannedPercentage</c>'s
/// own documented resolution, and should be confirmed by domain-expert/system-architect before a
/// golden-file WBS-rollup comparison is signed off.</para>
///
/// <para><b>A node with both children and its own direct activities</b> (non-standard, but not
/// forbidden by the data model) has no assigned weight for "its own work outside the subtree" to
/// blend against the children's <c>W_c</c>, so the subtree rollup wins and the direct activities are
/// excluded from that node's own figure - surfaced via <see cref="WbsRollupResult.MixedScopeNodeIds"/>
/// rather than silently dropped, consistent with this codebase's warn-don't-hide idiom
/// (<c>EacCalculator.Warnings</c>, actual-cost.md's coverage warning).</para>
/// </summary>
public static class WbsProgressRollupCalculator
{
    /// <summary>Sentinel key standing in for "no parent" (a root/top-level node) - same trick and
    /// same safety argument as <c>WbsTreeBuilder.RootSentinel</c> (a real <c>WBSNode.Id</c> is
    /// always a <see cref="Guid.CreateVersion7"/> value, never <see cref="Guid.Empty"/>).</summary>
    private static readonly Guid RootSentinel = Guid.Empty;

    public static WbsRollupResult Compute(
        IReadOnlyList<WbsRollupNodeInput> nodes,
        IReadOnlyList<WbsRollupActivityInput> activities)
    {
        var childrenByParent = new Dictionary<Guid, List<WbsRollupNodeInput>>();
        foreach (var node in nodes)
        {
            var parentKey = node.ParentWbsNodeId ?? RootSentinel;
            if (!childrenByParent.TryGetValue(parentKey, out var siblings))
            {
                siblings = [];
                childrenByParent[parentKey] = siblings;
            }

            siblings.Add(node);
        }

        var activitiesByNode = activities
            .GroupBy(a => a.WbsNodeId)
            .ToDictionary(g => g.Key, IReadOnlyList<WbsRollupActivityInput> (g) => g.ToList());

        // Breadth-first from the virtual root so every node is visited at most once and a node
        // unreachable from any root (an orphaned/cyclic chain that WBSNode.SetParent's own
        // write-time cycle guard should prevent, but a read path must never *trust* that
        // structurally - WbsTreeBuilder's own precedent) is simply omitted rather than causing an
        // infinite loop or corrupting the bottom-up pass below.
        var visitOrder = new List<WbsRollupNodeInput>();
        var enqueuedIds = new HashSet<Guid> { RootSentinel };
        var pendingParentIds = new Queue<Guid>();
        pendingParentIds.Enqueue(RootSentinel);

        while (pendingParentIds.Count > 0)
        {
            var parentId = pendingParentIds.Dequeue();
            if (!childrenByParent.TryGetValue(parentId, out var childRows))
            {
                continue;
            }

            foreach (var childRow in childRows)
            {
                visitOrder.Add(childRow);
                if (enqueuedIds.Add(childRow.Id))
                {
                    pendingParentIds.Enqueue(childRow.Id);
                }
            }
        }

        var weightWarnings = new List<WbsRollupWeightWarning>();
        var mixedScopeNodeIds = new List<Guid>();
        var pctById = new Dictionary<Guid, decimal>();

        // Bottom-up: visitOrder is parent-before-child (breadth-first), so walking it in reverse
        // guarantees every node's children already have a completed Pct entry before the node
        // itself is processed - the same back-to-front trick WbsTreeBuilder uses to build its DTO
        // tree in one pass with no recursion (so a pathologically deep tree cannot overflow the
        // call stack either).
        for (var i = visitOrder.Count - 1; i >= 0; i--)
        {
            var node = visitOrder[i];
            var hasOwnActivities = activitiesByNode.TryGetValue(node.Id, out var ownActivities);
            var hasChildren = childrenByParent.TryGetValue(node.Id, out var children);

            if (hasChildren && children is not null)
            {
                if (hasOwnActivities)
                {
                    mixedScopeNodeIds.Add(node.Id);
                }

                var (pct, weightSum) = WeightedAverage(
                    children.Select(c => (pctById[c.Id], c.WeightPercentage)).ToList());
                RecordWeightWarningIfNeeded(weightWarnings, node.Id, children.Count, weightSum);
                pctById[node.Id] = pct;
            }
            else
            {
                pctById[node.Id] = LeafPercentage(ownActivities);
            }
        }

        var rootNodes = childrenByParent.TryGetValue(RootSentinel, out var roots) ? roots : [];
        var (projectPct, rootWeightSum) = WeightedAverage(
            rootNodes.Select(n => (pctById[n.Id], n.WeightPercentage)).ToList());
        RecordWeightWarningIfNeeded(weightWarnings, wbsNodeId: null, rootNodes.Count, rootWeightSum);

        return new WbsRollupResult(PercentageGuard.Clamp(projectPct), weightWarnings, mixedScopeNodeIds);
    }

    private static void RecordWeightWarningIfNeeded(
        List<WbsRollupWeightWarning> warnings, Guid? wbsNodeId, int childCount, decimal weightSum)
    {
        // No children at all is "no scope defined yet", not a misconfiguration - only ever warn
        // when there was actually something to weigh (evm-formulas.md: "weights at a level should
        // sum to 100"; an empty level has no weights to sum).
        if (childCount > 0 && weightSum != 100.00m)
        {
            warnings.Add(new WbsRollupWeightWarning(wbsNodeId, childCount, weightSum));
        }
    }

    private static decimal LeafPercentage(IReadOnlyList<WbsRollupActivityInput>? activities)
    {
        if (activities is null || activities.Count == 0)
        {
            // No scope defined yet under this node (empty branch) -> 0.00, never null. Same
            // precedent as actual-cost.md's "no entries -> AC = 0.00, not null": this rollup is a
            // sum-shaped quantity, not an undefined ratio like SPI/CPI.
            return 0.00m;
        }

        var budgetSum = activities.Sum(a => a.BudgetCost);
        return budgetSum == 0m
            // Every activity under this node is zero-budget: fall back to an equal-weight average
            // (mirrors EvmEngine's own "no division by BudgetCost" zero-budget guard) rather than a
            // bare 0.00, which would misreport fully-complete zero-budget scope as not started.
            ? activities.Average(a => a.ProgressPercentage)
            : activities.Sum(a => a.BudgetCost * a.ProgressPercentage) / budgetSum;
    }

    /// <summary>evm-formulas.md: `Pct_parent = sum(Pct_c * W_c) / sum(W_c)`. Returns the achieved
    /// weight sum alongside the percentage so the caller can both fall back safely (sum = 0, the
    /// formula's own denominator) and raise the "weights should sum to 100" warning from one shared
    /// summation, never two independent passes over the same list.</summary>
    private static (decimal Pct, decimal WeightSum) WeightedAverage(IReadOnlyList<(decimal Pct, decimal Weight)> items)
    {
        if (items.Count == 0)
        {
            return (0.00m, 0m);
        }

        var weightSum = items.Sum(i => i.Weight);
        if (weightSum == 0m)
        {
            // Every sibling at this level carries a 0.00 weight (unconfigured/degenerate) - fall
            // back to an equal-weight average rather than dividing by zero, per the DoD's "warn, not
            // block". The caller still raises a warning because weightSum (0) != 100.00.
            return (items.Average(i => i.Pct), weightSum);
        }

        return (items.Sum(i => i.Pct * i.Weight) / weightSum, weightSum);
    }
}
