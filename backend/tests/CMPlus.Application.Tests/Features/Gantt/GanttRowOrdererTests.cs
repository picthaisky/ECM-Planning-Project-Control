using CMPlus.Application.Features.Gantt.Queries.GetGantt;
using CMPlus.Application.Gantt;
using CMPlus.Application.Wbs;

namespace CMPlus.Application.Tests.Features.Gantt;

public class GanttRowOrdererTests
{
    private static GanttActivityFlatRow CreateActivity(
        Guid wbsNodeId, string code, bool isCritical = false, int? totalFloat = null, int? freeFloat = null) => new(
        Guid.NewGuid(),
        wbsNodeId,
        code,
        $"Activity {code}",
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        DateTimeOffset.Parse("2026-01-15T00:00:00Z"),
        null,
        null,
        isCritical,
        totalFloat,
        freeFloat);

    [Fact]
    public void Build_Returns_Empty_Activities_For_No_Nodes_Or_Activities()
    {
        var dto = GanttRowOrderer.Build(Guid.NewGuid(), [], []);

        Assert.Empty(dto.Activities);
    }

    [Fact]
    public void Build_Emits_A_Nodes_Own_Activities_Before_Its_Child_Nodes_Activities()
    {
        var root = new WbsNodeFlatRow(Guid.NewGuid(), null, "1", "Structure", 100m, 2);
        var child = new WbsNodeFlatRow(Guid.NewGuid(), root.Id, "1.1", "Foundation", 100m, 1);

        var rootActivity = CreateActivity(root.Id, "ACT-100");
        var childActivity = CreateActivity(child.Id, "ACT-110");

        var dto = GanttRowOrderer.Build(Guid.NewGuid(), [child, root], [childActivity, rootActivity]);

        Assert.Equal(["ACT-100", "ACT-110"], dto.Activities.Select(a => a.ActivityCode));
    }

    [Fact]
    public void Build_Orders_Sibling_Nodes_By_Code()
    {
        var root = new WbsNodeFlatRow(Guid.NewGuid(), null, "1", "Root", 100m, 0);
        var nodeB = new WbsNodeFlatRow(Guid.NewGuid(), root.Id, "1.2", "B", 0m, 0);
        var nodeA = new WbsNodeFlatRow(Guid.NewGuid(), root.Id, "1.1", "A", 0m, 0);

        var activityUnderB = CreateActivity(nodeB.Id, "ACT-B");
        var activityUnderA = CreateActivity(nodeA.Id, "ACT-A");

        var dto = GanttRowOrderer.Build(Guid.NewGuid(), [root, nodeB, nodeA], [activityUnderB, activityUnderA]);

        Assert.Equal(["ACT-A", "ACT-B"], dto.Activities.Select(a => a.ActivityCode));
    }

    [Fact]
    public void Build_Orders_Activities_Within_The_Same_Node_By_ActivityCode()
    {
        var node = new WbsNodeFlatRow(Guid.NewGuid(), null, "1", "Root", 100m, 2);
        var second = CreateActivity(node.Id, "ACT-200");
        var first = CreateActivity(node.Id, "ACT-100");

        var dto = GanttRowOrderer.Build(Guid.NewGuid(), [node], [second, first]);

        Assert.Equal(["ACT-100", "ACT-200"], dto.Activities.Select(a => a.ActivityCode));
    }

    [Fact]
    public void Build_Preserves_The_CPM_Fields_Verbatim_On_Each_Dto()
    {
        var node = new WbsNodeFlatRow(Guid.NewGuid(), null, "1", "Root", 100m, 1);
        var activity = CreateActivity(node.Id, "ACT-100", isCritical: true, totalFloat: 0, freeFloat: 0);

        var dto = GanttRowOrderer.Build(Guid.NewGuid(), [node], [activity]);

        var result = Assert.Single(dto.Activities);
        Assert.Equal(activity.Id, result.Id);
        Assert.Equal(node.Id, result.WbsNodeId);
        Assert.True(result.IsCritical);
        Assert.Equal(0, result.TotalFloat);
        Assert.Equal(0, result.FreeFloat);
    }

    [Fact]
    public void Build_Appends_An_Orphaned_Activity_Whose_WbsNodeId_Is_Unreachable_From_Any_Root_Rather_Than_Dropping_It()
    {
        // Defensive read-side handling, same philosophy as WbsTreeBuilder's own orphan-node test -
        // unlike an orphaned WBS grouping node, an orphaned Activity is real schedule data the
        // Gantt must never silently hide.
        var root = new WbsNodeFlatRow(Guid.NewGuid(), null, "1", "Root", 100m, 1);
        var rootActivity = CreateActivity(root.Id, "ACT-100");
        var orphanActivity = CreateActivity(Guid.NewGuid(), "ACT-999");

        var dto = GanttRowOrderer.Build(Guid.NewGuid(), [root], [orphanActivity, rootActivity]);

        Assert.Equal(2, dto.Activities.Count);
        Assert.Contains(dto.Activities, a => a.ActivityCode == "ACT-999");
    }

    [Fact]
    public void Build_Does_Not_Overflow_The_Stack_On_A_Deep_Chain()
    {
        const int depth = 5_000;
        var nodes = new List<WbsNodeFlatRow>(depth);
        Guid? parentId = null;
        Guid deepestId = Guid.Empty;
        for (var i = 0; i < depth; i++)
        {
            var id = Guid.NewGuid();
            nodes.Add(new WbsNodeFlatRow(id, parentId, i.ToString("D5"), $"Node {i}", 0m, 0));
            parentId = id;
            deepestId = id;
        }

        var deepestActivity = CreateActivity(deepestId, "ACT-DEEP");

        var dto = GanttRowOrderer.Build(Guid.NewGuid(), nodes, [deepestActivity]);

        var result = Assert.Single(dto.Activities);
        Assert.Equal("ACT-DEEP", result.ActivityCode);
    }
}
