namespace CMPlus.Application.Services.Wbs;

/// <summary>
/// One <c>WBSNode</c>'s shape as needed by <see cref="WbsProgressRollupCalculator"/> - id, parent
/// (for tree reconstruction) and its own <c>WeightPercentage</c> (evm-formulas.md's rollup formula
/// weights a node against its siblings, not against its own subtree). Deliberately narrower than
/// <c>CMPlus.Application.Wbs.WbsNodeFlatRow</c> (no <c>Code</c>/<c>Title</c>/<c>ActivityCount</c>) -
/// callers project down to this shape from whichever reader they already have (S8-BE-02 reuses
/// <c>IWbsTreeReader.GetNodesWithActivityCountsAsync</c> rather than adding a duplicate node-shape
/// query), keeping this calculator decoupled from any one reader's own DTO.
/// </summary>
public sealed record WbsRollupNodeInput(Guid Id, Guid? ParentWbsNodeId, decimal WeightPercentage);

/// <summary>
/// One <c>Activity</c>'s contribution to its owning WBS node's own (leaf-level) progress - current
/// <c>Activity.ProgressPercentage</c> (the ADR-0009 cache, i.e. "right now", not a historical
/// step-function reconstruction at some data date). This is a deliberate difference from
/// <see cref="CMPlus.Application.Services.Evm.EvmActivityProgressInput"/>: the WBS screen's own
/// rollup (what S8-BE-02's DoD requires this to match "to two decimal places") has no data-date
/// parameter at all - <c>GetNodeActivitiesQuery</c>/<c>ActivityForProgressDto.CurrentProgressPercentage</c>
/// always shows current state - so this calculator must read the same current cache, never the
/// dated reconstruction, or the two screens would only agree when <c>dataDate</c> happens to equal
/// "today" for every activity.
/// </summary>
public sealed record WbsRollupActivityInput(Guid WbsNodeId, decimal BudgetCost, decimal ProgressPercentage);

/// <summary>
/// A level whose children's <c>WeightPercentage</c> values did not sum to exactly 100.00 - raised as
/// a warning, never a block (evm-formulas.md's rollup rule; S8-BE-02 DoD: "น้ำหนักไม่ครบ 100 →
/// เตือน ไม่บล็อก"). <see cref="WbsNodeId"/> is <see langword="null"/> for the project's own
/// top-level (root) siblings, otherwise the parent whose direct children are misconfigured.
/// <see cref="WeightSum"/> is the raw total actually found (0 when every child happens to carry a
/// 0.00 weight - the degenerate case <see cref="WbsProgressRollupCalculator"/> falls back to an
/// equal-weight average for, so the rollup itself never divides by zero).
/// </summary>
public sealed record WbsRollupWeightWarning(Guid? WbsNodeId, int ChildCount, decimal WeightSum);

/// <summary>
/// <see cref="WbsProgressRollupCalculator.Compute"/>'s full result: the project-level rollup
/// (full <see cref="decimal"/> precision - callers round exactly once at their own response
/// boundary via <c>RoundingRules.ToPercentage</c>, same discipline as
/// <see cref="CMPlus.Application.Services.Evm.EvmEngine"/>/<c>EacCalculator</c>) plus every
/// data-quality signal the walk discovered, never silently absorbed.
/// </summary>
public sealed record WbsRollupResult(
    decimal ProgressPercentage,
    IReadOnlyList<WbsRollupWeightWarning> WeightWarnings,
    IReadOnlyList<Guid> MixedScopeNodeIds);
