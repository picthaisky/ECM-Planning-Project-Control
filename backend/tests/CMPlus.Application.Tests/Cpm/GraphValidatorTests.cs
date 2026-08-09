using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Cpm;

/// <summary>S5-BE-03: cycle detection (with the offending chain), duplicate-relation rejection,
/// and the unknown-activity defensive check - all BEFORE CpmEngine's forward pass would ever run
/// (verified by calling GraphValidator directly, not just through CpmEngine.Calculate).</summary>
public class GraphValidatorTests
{
    [Fact]
    public void CPM_EDGE_CYCLE_Two_Activity_Cycle_Is_Rejected_With_The_Offending_Chain()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var result = GraphValidator.Validate(
            [new CpmActivityInput(a, 5), new CpmActivityInput(b, 3)],
            [new CpmRelationInput(a, b, RelationType.FS, 0), new CpmRelationInput(b, a, RelationType.FS, 0)]);

        Assert.False(result.IsValid);
        Assert.Equal(CpmValidationErrorCodes.CycleDetected, result.ErrorCode);
        Assert.NotNull(result.CycleChain);
        // Ordered chain, first activity repeated at the end (e.g. [A, B, A]) - same shape as
        // Infrastructure's Sprint 3 RelationGraphValidator.FindCycle.
        Assert.Equal(3, result.CycleChain!.Count);
        Assert.Equal(result.CycleChain[0], result.CycleChain[^1]);
        Assert.Contains(a, result.CycleChain);
        Assert.Contains(b, result.CycleChain);
    }

    [Fact]
    public void A_Longer_Cycle_Through_An_Otherwise_Valid_Chain_Is_Still_Detected()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var result = GraphValidator.Validate(
            [new CpmActivityInput(a, 1), new CpmActivityInput(b, 1), new CpmActivityInput(c, 1)],
            [
                new CpmRelationInput(a, b, RelationType.FS, 0),
                new CpmRelationInput(b, c, RelationType.FS, 0),
                new CpmRelationInput(c, a, RelationType.FS, 0),
            ]);

        Assert.False(result.IsValid);
        Assert.Equal(CpmValidationErrorCodes.CycleDetected, result.ErrorCode);
        Assert.Equal(4, result.CycleChain!.Count);
        Assert.Equal(result.CycleChain[0], result.CycleChain[^1]);
    }

    [Fact]
    public void CPM_EDGE_DUPLICATE_RELATION_Same_Ordered_Pair_Twice_Is_Rejected()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var result = GraphValidator.Validate(
            [new CpmActivityInput(a, 5), new CpmActivityInput(b, 3)],
            [new CpmRelationInput(a, b, RelationType.FS, 0), new CpmRelationInput(a, b, RelationType.FS, 0)]);

        Assert.False(result.IsValid);
        Assert.Equal(CpmValidationErrorCodes.DuplicateRelation, result.ErrorCode);
        Assert.Null(result.CycleChain);
    }

    [Fact]
    public void A_Relation_Referencing_An_Activity_Outside_The_Supplied_Set_Is_Rejected()
    {
        var a = Guid.NewGuid();
        var unknown = Guid.NewGuid();

        var result = GraphValidator.Validate(
            [new CpmActivityInput(a, 5)],
            [new CpmRelationInput(a, unknown, RelationType.FS, 0)]);

        Assert.False(result.IsValid);
        Assert.Equal(CpmValidationErrorCodes.UnknownActivityInRelation, result.ErrorCode);
    }

    [Fact]
    public void An_Acyclic_Network_With_No_Duplicates_Is_Valid()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var result = GraphValidator.Validate(
            [new CpmActivityInput(a, 5), new CpmActivityInput(b, 3)],
            [new CpmRelationInput(a, b, RelationType.FS, 0)]);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.CycleChain);
    }

    [Fact]
    public void An_Activity_With_No_Relations_At_All_Does_Not_Crash_Validation()
    {
        var iso = Guid.NewGuid();

        var result = GraphValidator.Validate([new CpmActivityInput(iso, 4)], []);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void A_Long_Near_Linear_Chain_Does_Not_Overflow_The_Call_Stack()
    {
        // Mirrors the S4-DB-02 perf dataset's own ~9,999-edge sequential backbone shape - the
        // pathological case an accidentally-recursive DFS would fail on.
        const int chainLength = 20_000;
        var ids = Enumerable.Range(0, chainLength).Select(_ => Guid.NewGuid()).ToList();
        var activities = ids.Select(id => new CpmActivityInput(id, 1)).ToList();
        var relations = Enumerable.Range(0, chainLength - 1)
            .Select(i => new CpmRelationInput(ids[i], ids[i + 1], RelationType.FS, 0))
            .ToList();

        var result = GraphValidator.Validate(activities, relations);

        Assert.True(result.IsValid);
    }
}
