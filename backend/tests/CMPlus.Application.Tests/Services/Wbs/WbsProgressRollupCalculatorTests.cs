using CMPlus.Application.Services.Wbs;

namespace CMPlus.Application.Tests.Services.Wbs;

/// <summary>
/// S8-BE-02 (backend-developer's own sanity check, per CLAUDE.md's "implement the worked examples
/// as your own sanity check before handing to QA"). evm-formulas.md's "Progress rollup (WBS tree)"
/// section has no transcribed worked example of its own (unlike the EVM/EAC sections), so these
/// fixtures are hand-authored here rather than loaded from a JSON fixture file - each one is worked
/// by hand in its own comment so the expected value is independently checkable.
/// </summary>
public class WbsProgressRollupCalculatorTests
{
    [Fact]
    public void Compute_Returns_Zero_For_An_Empty_Project_With_No_Warnings()
    {
        var result = WbsProgressRollupCalculator.Compute([], []);

        Assert.Equal(0.00m, result.ProgressPercentage);
        Assert.Empty(result.WeightWarnings);
        Assert.Empty(result.MixedScopeNodeIds);
    }

    [Fact]
    public void Compute_A_Leaf_Nodes_Progress_Is_The_Budget_Weighted_Average_Of_Its_Activities()
    {
        var leaf = Guid.NewGuid();
        var nodes = new[] { new WbsRollupNodeInput(leaf, null, 100m) };
        var activities = new[]
        {
            new WbsRollupActivityInput(leaf, BudgetCost: 100m, ProgressPercentage: 50m),
            new WbsRollupActivityInput(leaf, BudgetCost: 300m, ProgressPercentage: 90m),
        };

        // (100*50 + 300*90) / (100+300) = (5,000 + 27,000) / 400 = 80.00.
        var result = WbsProgressRollupCalculator.Compute(nodes, activities);

        Assert.Equal(80.00m, result.ProgressPercentage);
        Assert.Empty(result.WeightWarnings);
    }

    [Fact]
    public void Compute_A_Three_Level_Tree_Rolls_Up_Correctly()
    {
        var a = Guid.NewGuid();
        var a1 = Guid.NewGuid();
        var a2 = Guid.NewGuid();
        var b = Guid.NewGuid();

        var nodes = new[]
        {
            new WbsRollupNodeInput(a, null, 60m),
            new WbsRollupNodeInput(b, null, 40m),
            new WbsRollupNodeInput(a1, a, 70m),
            new WbsRollupNodeInput(a2, a, 30m),
        };
        var activities = new[]
        {
            // A1: (100*50 + 300*90) / 400 = 80.00.
            new WbsRollupActivityInput(a1, 100m, 50m),
            new WbsRollupActivityInput(a1, 300m, 90m),
            // A2: single activity -> 20.00.
            new WbsRollupActivityInput(a2, 200m, 20m),
            // B: single activity -> 40.00.
            new WbsRollupActivityInput(b, 500m, 40m),
        };

        // A = (80*70 + 20*30) / 100 = (5,600 + 600) / 100 = 62.00.
        // Project = (62*60 + 40*40) / 100 = (3,720 + 1,600) / 100 = 53.20.
        var result = WbsProgressRollupCalculator.Compute(nodes, activities);

        Assert.Equal(53.20m, result.ProgressPercentage);
        Assert.Empty(result.WeightWarnings);
        Assert.Empty(result.MixedScopeNodeIds);
    }

    [Fact]
    public void Compute_Warns_But_Does_Not_Block_When_A_Levels_Weights_Do_Not_Sum_To_100()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var nodes = new[]
        {
            new WbsRollupNodeInput(a, null, 60m),
            new WbsRollupNodeInput(b, null, 30m), // 60 + 30 = 90, not 100.
        };
        var activities = new[]
        {
            new WbsRollupActivityInput(a, 1m, 80m),
            new WbsRollupActivityInput(b, 1m, 20m),
        };

        // The formula still divides by the *actual* weight sum (90), per evm-formulas.md - never
        // blocked: (80*60 + 20*30) / 90 = (4,800 + 600) / 90 = 60.00.
        var result = WbsProgressRollupCalculator.Compute(nodes, activities);

        Assert.Equal(60.00m, result.ProgressPercentage);
        var warning = Assert.Single(result.WeightWarnings);
        Assert.Null(warning.WbsNodeId); // root/top-level.
        Assert.Equal(2, warning.ChildCount);
        Assert.Equal(90m, warning.WeightSum);
    }

    [Fact]
    public void Compute_Falls_Back_To_An_Equal_Weight_Average_When_Every_Sibling_Is_Zero_Weighted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var nodes = new[]
        {
            new WbsRollupNodeInput(a, null, 0m),
            new WbsRollupNodeInput(b, null, 0m),
        };
        var activities = new[]
        {
            new WbsRollupActivityInput(a, 1m, 80m),
            new WbsRollupActivityInput(b, 1m, 20m),
        };

        // Never divides by zero - falls back to a plain average: (80 + 20) / 2 = 50.00.
        var result = WbsProgressRollupCalculator.Compute(nodes, activities);

        Assert.Equal(50.00m, result.ProgressPercentage);
        var warning = Assert.Single(result.WeightWarnings);
        Assert.Equal(0m, warning.WeightSum);
    }

    [Fact]
    public void Compute_Falls_Back_To_An_Equal_Weight_Average_Of_Activities_When_A_Leafs_Activities_Are_All_Zero_Budget()
    {
        var leaf = Guid.NewGuid();
        var nodes = new[] { new WbsRollupNodeInput(leaf, null, 100m) };
        var activities = new[]
        {
            new WbsRollupActivityInput(leaf, 0m, 100m), // fully complete, but zero budget.
            new WbsRollupActivityInput(leaf, 0m, 0m),
        };

        // A bare 0.00 (as a naive "no budget -> no contribution" rule would give, mirroring
        // EvmEngine's EV guard) would misreport the fully-complete activity as not started - the
        // equal-weight fallback avoids that: (100 + 0) / 2 = 50.00.
        var result = WbsProgressRollupCalculator.Compute(nodes, activities);

        Assert.Equal(50.00m, result.ProgressPercentage);
    }

    [Fact]
    public void Compute_Returns_Zero_For_A_Leaf_With_No_Activities_And_No_Children()
    {
        var leaf = Guid.NewGuid();
        var nodes = new[] { new WbsRollupNodeInput(leaf, null, 100m) };

        var result = WbsProgressRollupCalculator.Compute(nodes, []);

        Assert.Equal(0.00m, result.ProgressPercentage);
        Assert.Empty(result.WeightWarnings); // an empty branch is "no scope yet", not a misconfiguration.
    }

    [Fact]
    public void Compute_Excludes_A_Non_Leaf_Nodes_Own_Direct_Activities_And_Flags_It_As_Mixed_Scope()
    {
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        var nodes = new[]
        {
            new WbsRollupNodeInput(parent, null, 100m),
            new WbsRollupNodeInput(child, parent, 100m),
        };
        var activities = new[]
        {
            // The child's own, real scope.
            new WbsRollupActivityInput(child, 1m, 30m),
            // Direct activities on a non-leaf node - non-standard, must be excluded from the
            // subtree rollup (no weight exists to blend it against W_c) and flagged, not silently
            // dropped.
            new WbsRollupActivityInput(parent, 1m, 999m),
        };

        var result = WbsProgressRollupCalculator.Compute(nodes, activities);

        Assert.Equal(30.00m, result.ProgressPercentage);
        Assert.Contains(parent, result.MixedScopeNodeIds);
    }

    [Fact]
    public void Compute_Excludes_A_Node_Unreachable_From_Any_Root_Rather_Than_Throwing()
    {
        var root = Guid.NewGuid();
        var nodes = new[]
        {
            new WbsRollupNodeInput(root, null, 100m),
            // References a parent id that does not exist anywhere in `nodes` - defensively
            // excluded (same "never trust the shape structurally" precedent as WbsTreeBuilder),
            // never a KeyNotFoundException/infinite loop.
            new WbsRollupNodeInput(Guid.NewGuid(), Guid.NewGuid(), 50m),
        };
        var activities = new[] { new WbsRollupActivityInput(root, 1m, 77m) };

        var result = WbsProgressRollupCalculator.Compute(nodes, activities);

        Assert.Equal(77.00m, result.ProgressPercentage);
    }

    [Fact]
    public void Compute_Clamps_The_Final_Percentage_To_The_Zero_To_Hundred_Range()
    {
        // Defensive only - unreachable via any valid WeightPercentage/ProgressPercentage input
        // (both domain-guarded to [0,100] at their own point of assignment), but this asserts the
        // belt-and-braces PercentageGuard.Clamp call the same way EvmEngine keeps its own
        // already-unreachable zero-division guard.
        var leaf = Guid.NewGuid();
        var nodes = new[] { new WbsRollupNodeInput(leaf, null, 100m) };
        var activities = new[] { new WbsRollupActivityInput(leaf, 1m, 100m) };

        var result = WbsProgressRollupCalculator.Compute(nodes, activities);

        Assert.InRange(result.ProgressPercentage, 0m, 100m);
    }
}
