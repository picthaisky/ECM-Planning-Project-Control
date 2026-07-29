using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

public class ActivityRelationTests
{
    [Fact]
    public void Constructor_Rejects_SelfReferencing_Relation()
    {
        var activityId = Guid.NewGuid();

        Assert.Throws<DomainException>(() =>
            new ActivityRelation(Guid.NewGuid(), activityId, activityId, RelationType.FS));
    }

    [Fact]
    public void Constructor_Allows_Negative_LagDays_As_Lead()
    {
        var relation = new ActivityRelation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RelationType.FS, lagDays: -3);

        Assert.Equal(-3, relation.LagDays);
    }

    [Theory]
    [InlineData(RelationType.FS)]
    [InlineData(RelationType.SS)]
    [InlineData(RelationType.FF)]
    [InlineData(RelationType.SF)]
    public void Constructor_Accepts_All_Relation_Types(RelationType type)
    {
        var relation = new ActivityRelation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type);

        Assert.Equal(type, relation.RelationType);
    }
}
