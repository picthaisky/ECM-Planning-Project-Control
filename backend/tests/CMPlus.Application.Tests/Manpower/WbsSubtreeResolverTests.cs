using CMPlus.Application.Wbs;

namespace CMPlus.Application.Tests.Manpower;

/// <summary>domain-rules.md (manpower-equipment) §4.3's <c>subtree(WbsNodeId)</c> primitive - the
/// closure <see cref="ProductivityIndexReader"/>'s Tier 1 scope matching is built on.</summary>
public class WbsSubtreeResolverTests
{
    [Fact]
    public void ResolveSubtree_Returns_Only_The_Root_When_It_Has_No_Children()
    {
        var root = Guid.NewGuid();
        var nodes = new[] { new WbsNodeParentLink(root, null) };

        var subtree = WbsSubtreeResolver.ResolveSubtree(nodes, root);

        Assert.Single(subtree);
        Assert.Contains(root, subtree);
    }

    [Fact]
    public void ResolveSubtree_Includes_Every_Descendant_At_Every_Depth()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var grandchild = Guid.NewGuid();
        var unrelatedSibling = Guid.NewGuid();

        var nodes = new[]
        {
            new WbsNodeParentLink(root, null),
            new WbsNodeParentLink(child, root),
            new WbsNodeParentLink(grandchild, child),
            new WbsNodeParentLink(unrelatedSibling, null),
        };

        var subtree = WbsSubtreeResolver.ResolveSubtree(nodes, root);

        Assert.Equal(3, subtree.Count);
        Assert.Contains(root, subtree);
        Assert.Contains(child, subtree);
        Assert.Contains(grandchild, subtree);
        Assert.DoesNotContain(unrelatedSibling, subtree);
    }

    [Fact]
    public void ResolveSubtree_Rooted_At_A_Leaf_Returns_Only_That_Leaf()
    {
        var root = Guid.NewGuid();
        var leaf = Guid.NewGuid();
        var nodes = new[] { new WbsNodeParentLink(root, null), new WbsNodeParentLink(leaf, root) };

        var subtree = WbsSubtreeResolver.ResolveSubtree(nodes, leaf);

        Assert.Single(subtree);
        Assert.Contains(leaf, subtree);
    }
}
