using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;

namespace CMPlus.Domain.Tests.Entities;

public class WBSNodeTests
{
    private static WBSNode CreateNode(Guid tenantId, Guid projectId, string code, decimal weight = 10m, Guid? parentId = null) =>
        new(tenantId, projectId, code, $"Title-{code}", weight, parentId);

    [Fact]
    public void SetParent_Rejects_Node_As_Its_Own_Parent()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var a = CreateNode(tenantId, projectId, "A");
        var nodes = new Dictionary<Guid, WBSNode> { [a.Id] = a };

        Assert.Throws<DomainException>(() => a.SetParent(a.Id, nodes));
    }

    [Fact]
    public void SetParent_Rejects_Cycle_Through_Descendant_Chain()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var a = CreateNode(tenantId, projectId, "A");
        var b = CreateNode(tenantId, projectId, "B", parentId: a.Id);
        var c = CreateNode(tenantId, projectId, "C", parentId: b.Id);

        var nodes = new Dictionary<Guid, WBSNode> { [a.Id] = a, [b.Id] = b, [c.Id] = c };

        // A -> C would make A its own ancestor (A is currently an ancestor of C).
        var ex = Assert.Throws<DomainException>(() => a.SetParent(c.Id, nodes));
        Assert.Contains("ancestor", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The tree is unchanged after the rejected operation.
        Assert.Null(a.ParentWbsNodeId);
    }

    [Fact]
    public void SetParent_Does_Not_StackOverflow_On_A_Deep_Chain()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var nodes = new Dictionary<Guid, WBSNode>();
        WBSNode? previous = null;
        for (var i = 0; i < 5000; i++)
        {
            var node = CreateNode(tenantId, projectId, $"N{i}", parentId: previous?.Id);
            nodes[node.Id] = node;
            previous = node;
        }

        var root = nodes.Values.First(n => n.ParentWbsNodeId is null);
        var deepestLeaf = previous!;

        // Re-parenting the root under its own deepest descendant must be rejected, and must
        // terminate (the whole point of the iterative guard) rather than overflow the stack.
        Assert.Throws<DomainException>(() => root.SetParent(deepestLeaf.Id, nodes));
    }

    [Fact]
    public void SetParent_Allows_A_Valid_ReParent()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var a = CreateNode(tenantId, projectId, "A");
        var b = CreateNode(tenantId, projectId, "B");
        var nodes = new Dictionary<Guid, WBSNode> { [a.Id] = a, [b.Id] = b };

        b.SetParent(a.Id, nodes);

        Assert.Equal(a.Id, b.ParentWbsNodeId);
    }

    [Fact]
    public void SetParent_Null_Detaches_From_Parent()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var a = CreateNode(tenantId, projectId, "A");
        var b = CreateNode(tenantId, projectId, "B", parentId: a.Id);
        var nodes = new Dictionary<Guid, WBSNode> { [a.Id] = a, [b.Id] = b };

        b.SetParent(null, nodes);

        Assert.Null(b.ParentWbsNodeId);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-5, 0)]
    [InlineData(33.33, 33.33)]
    public void WeightPercentage_Clamps_To_0_100(decimal input, decimal expected)
    {
        var node = CreateNode(Guid.NewGuid(), Guid.NewGuid(), "A", weight: input);

        Assert.Equal(expected, node.WeightPercentage);
    }

    [Fact]
    public void SetWeightPercentage_Clamps_At_Setter_Not_Just_Constructor()
    {
        var node = CreateNode(Guid.NewGuid(), Guid.NewGuid(), "A");

        node.SetWeightPercentage(200m);

        Assert.Equal(100m, node.WeightPercentage);
    }

    [Fact]
    public void Constructor_Rejects_Blank_Code_Or_Title()
    {
        Assert.Throws<DomainException>(() => new WBSNode(Guid.NewGuid(), Guid.NewGuid(), "", "Title", 10m));
    }
}
