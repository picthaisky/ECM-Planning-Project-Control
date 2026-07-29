using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Parsers.Mspdi;

namespace CMPlus.Integration.Tests.Parsers;

/// <summary>
/// S3-QA-01/S3-BE-02: field-for-field comparison against
/// <c>tests/fixtures/goldenfiles/mspdi/sample-schedule.xml</c> - see <see cref="FixtureFiles"/>'s
/// provenance note. Same hand-computed expectations as the XER golden file (they describe the same
/// project), proving both parsers agree on the shared 8-hour-workday convention.
/// </summary>
public class MspdiScheduleParserGoldenFileTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public void Parses_The_Golden_File_Matching_Every_Hand_Computed_Field()
    {
        using var stream = FixtureFiles.OpenRead("mspdi/sample-schedule.xml");
        var result = new MspdiScheduleParser().Parse(stream, TenantId, ProjectId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        var schedule = result.Value;

        Assert.Equal(2, schedule.WbsNodes.Count);
        var civil = Assert.Single(schedule.WbsNodes, n => n.Title == "Civil Works");
        Assert.Equal("1", civil.Code);
        Assert.Null(civil.ParentWbsNodeId); // the UID-0/OutlineLevel-0 project summary task is skipped.
        var mep = Assert.Single(schedule.WbsNodes, n => n.Title == "MEP Works");
        Assert.Equal("2", mep.Code);

        Assert.Equal(3, schedule.Activities.Count);

        var excavation = Assert.Single(schedule.Activities, a => a.Name == "Excavation");
        Assert.Equal("1.1", excavation.ActivityCode);
        Assert.Equal(new DateTimeOffset(2026, 2, 2, 8, 0, 0, TimeSpan.Zero), excavation.PlannedStart);
        Assert.Equal(new DateTimeOffset(2026, 2, 6, 17, 0, 0, TimeSpan.Zero), excavation.PlannedFinish);
        Assert.Equal(5, excavation.DurationDays); // PT40H0M0S / 8h-day
        Assert.Equal(500_000.00m, excavation.BudgetCost);
        Assert.Equal(civil.Id, excavation.WbsNodeId);

        var foundation = Assert.Single(schedule.Activities, a => a.Name == "Foundation");
        Assert.Equal("1.2", foundation.ActivityCode);
        Assert.Equal(new DateTimeOffset(2026, 2, 9, 8, 0, 0, TimeSpan.Zero), foundation.PlannedStart);
        Assert.Equal(new DateTimeOffset(2026, 2, 13, 17, 0, 0, TimeSpan.Zero), foundation.PlannedFinish);
        Assert.Equal(5, foundation.DurationDays);
        Assert.Equal(750_000.00m, foundation.BudgetCost);
        Assert.Equal(civil.Id, foundation.WbsNodeId);

        var electrical = Assert.Single(schedule.Activities, a => a.Name == "Electrical Rough-in");
        Assert.Equal("2.1", electrical.ActivityCode);
        Assert.Equal(new DateTimeOffset(2026, 2, 16, 8, 0, 0, TimeSpan.Zero), electrical.PlannedStart);
        Assert.Equal(new DateTimeOffset(2026, 2, 18, 17, 0, 0, TimeSpan.Zero), electrical.PlannedFinish);
        Assert.Equal(3, electrical.DurationDays); // PT24H0M0S / 8h-day
        Assert.Equal(300_000.00m, electrical.BudgetCost);
        Assert.Equal(mep.Id, electrical.WbsNodeId);

        Assert.Equal(2, schedule.Relations.Count);

        var fsRelation = Assert.Single(schedule.Relations, r => r.RelationType == RelationType.FS);
        Assert.Equal(excavation.Id, fsRelation.PredecessorActivityId);
        Assert.Equal(foundation.Id, fsRelation.SuccessorActivityId);
        Assert.Equal(0, fsRelation.LagDays);

        var ssRelation = Assert.Single(schedule.Relations, r => r.RelationType == RelationType.SS);
        Assert.Equal(foundation.Id, ssRelation.PredecessorActivityId);
        Assert.Equal(electrical.Id, ssRelation.SuccessorActivityId);
        Assert.Equal(1, ssRelation.LagDays); // PT8H0M0S lag / 8h-day

        Assert.Equal(7, schedule.RowCount);
    }
}
