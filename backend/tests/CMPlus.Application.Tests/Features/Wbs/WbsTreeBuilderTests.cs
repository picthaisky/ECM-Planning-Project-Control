using CMPlus.Application.Features.Wbs.Queries.GetWbsTree;
using CMPlus.Application.Wbs;

namespace CMPlus.Application.Tests.Features.Wbs;

public class WbsTreeBuilderTests
{
    [Fact]
    public void Build_Returns_Empty_Tree_For_No_Nodes()
    {
        var tree = WbsTreeBuilder.Build(Guid.NewGuid(), []);

        Assert.Empty(tree.RootNodes);
    }

    [Fact]
    public void Build_Nests_Children_Under_Their_Parent()
    {
        var root = new WbsNodeFlatRow(Guid.NewGuid(), null, "1", "Structure", 60m, 3);
        var child = new WbsNodeFlatRow(Guid.NewGuid(), root.Id, "1.1", "Foundation", 30m, 2);
        var grandchild = new WbsNodeFlatRow(Guid.NewGuid(), child.Id, "1.1.1", "Piling", 10m, 1);

        var tree = WbsTreeBuilder.Build(Guid.NewGuid(), [grandchild, child, root]);

        var rootDto = Assert.Single(tree.RootNodes);
        Assert.Equal(root.Id, rootDto.Id);
        Assert.Equal(3, rootDto.ActivityCount);

        var childDto = Assert.Single(rootDto.Children);
        Assert.Equal(child.Id, childDto.Id);

        var grandchildDto = Assert.Single(childDto.Children);
        Assert.Equal(grandchild.Id, grandchildDto.Id);
        Assert.Empty(grandchildDto.Children);
    }

    [Fact]
    public void Build_Orders_Siblings_By_Code()
    {
        var root = new WbsNodeFlatRow(Guid.NewGuid(), null, "1", "Root", 100m, 0);
        var b = new WbsNodeFlatRow(Guid.NewGuid(), root.Id, "1.2", "B", 20m, 0);
        var a = new WbsNodeFlatRow(Guid.NewGuid(), root.Id, "1.1", "A", 20m, 0);
        var c = new WbsNodeFlatRow(Guid.NewGuid(), root.Id, "1.3", "C", 20m, 0);

        var tree = WbsTreeBuilder.Build(Guid.NewGuid(), [root, b, a, c]);

        var rootDto = Assert.Single(tree.RootNodes);
        Assert.Equal(["1.1", "1.2", "1.3"], rootDto.Children.Select(child => child.Code));
    }

    [Fact]
    public void Build_Supports_Multiple_Root_Nodes()
    {
        var root1 = new WbsNodeFlatRow(Guid.NewGuid(), null, "1", "Root 1", 50m, 0);
        var root2 = new WbsNodeFlatRow(Guid.NewGuid(), null, "2", "Root 2", 50m, 0);

        var tree = WbsTreeBuilder.Build(Guid.NewGuid(), [root2, root1]);

        Assert.Equal(2, tree.RootNodes.Count);
        Assert.Equal(["1", "2"], tree.RootNodes.Select(n => n.Code));
    }

    [Fact]
    public void Build_Omits_A_Node_Whose_Parent_Does_Not_Exist_In_The_Set_Rather_Than_Throwing()
    {
        // Defensive read-side handling of data that should never exist given WBSNode.SetParent's
        // write-time cycle guard, but a read path must not trust that structurally (e.g. a stray
        // row left over from a bad backfill/migration). This must never throw or infinite-loop.
        var orphan = new WbsNodeFlatRow(Guid.NewGuid(), Guid.NewGuid(), "9.9", "Orphan", 0m, 0);
        var root = new WbsNodeFlatRow(Guid.NewGuid(), null, "1", "Root", 100m, 0);

        var tree = WbsTreeBuilder.Build(Guid.NewGuid(), [orphan, root]);

        var rootDto = Assert.Single(tree.RootNodes);
        Assert.Equal(root.Id, rootDto.Id);
        Assert.Empty(rootDto.Children);
    }

    [Fact]
    public void Build_Does_Not_Overflow_The_Stack_On_A_Deep_Chain()
    {
        // Iterative (queue-based) construction, not recursion - a 5,000-deep chain (the DoD's own
        // node-count ceiling) must build without a StackOverflowException.
        const int depth = 5_000;
        var rows = new List<WbsNodeFlatRow>(depth);
        Guid? parentId = null;
        for (var i = 0; i < depth; i++)
        {
            var id = Guid.NewGuid();
            rows.Add(new WbsNodeFlatRow(id, parentId, i.ToString("D5"), $"Node {i}", 0m, 0));
            parentId = id;
        }

        var tree = WbsTreeBuilder.Build(Guid.NewGuid(), rows);

        var depthCount = 0;
        var current = Assert.Single(tree.RootNodes);
        depthCount++;
        while (current.Children.Count > 0)
        {
            current = Assert.Single(current.Children);
            depthCount++;
        }

        Assert.Equal(depth, depthCount);
    }
}
