using CMPlus.Application.Abstractions;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Parsers.Xer;

namespace CMPlus.Integration.Tests.Parsers;

/// <summary>
/// S3-QA-01/S3-BE-01: field-for-field comparison against
/// <c>tests/fixtures/goldenfiles/xer/sample-schedule.xer</c> - see <see cref="FixtureFiles"/>'s
/// provenance note. Expected values below are the hand-computed sanity check
/// (CLAUDE.md: "implement the worked examples as your own sanity check") for that fixture:
/// 8-hour workday duration conversion (40h -&gt; 5 days, 24h -&gt; 3 days), TASKRSRC cost summed per
/// task (two resource rows on A1010 -&gt; 550,000.00), SS relation with a 1-day (8h) lag.
/// </summary>
public class XerScheduleParserGoldenFileTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private sealed class UnlimitedImportOptions : IImportOptionsProvider
    {
        public long MaxFileSizeBytes => long.MaxValue;

        public long MaxDecompressedSizeBytes => long.MaxValue;

        public long MaxEntityCount => long.MaxValue;
    }

    [Fact]
    public void Parses_The_Golden_File_Matching_Every_Hand_Computed_Field()
    {
        using var stream = FixtureFiles.OpenRead("xer/sample-schedule.xer");
        var result = new XerScheduleParser(new UnlimitedImportOptions()).Parse(stream, TenantId, ProjectId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        var schedule = result.Value;

        Assert.Equal(2, schedule.WbsNodes.Count);
        var civil = Assert.Single(schedule.WbsNodes, n => n.Code == "CIVIL");
        Assert.Equal("Civil Works", civil.Title);
        Assert.Null(civil.ParentWbsNodeId); // PROJWBS root (proj_node_flag=Y) is not materialized as a WBSNode.
        var mep = Assert.Single(schedule.WbsNodes, n => n.Code == "MEP");
        Assert.Equal("MEP Works", mep.Title);

        Assert.Equal(3, schedule.Activities.Count);

        var excavation = Assert.Single(schedule.Activities, a => a.ActivityCode == "A1010");
        Assert.Equal("Excavation", excavation.Name);
        Assert.Equal(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero), excavation.PlannedStart);
        Assert.Equal(new DateTimeOffset(2026, 1, 9, 17, 0, 0, TimeSpan.Zero), excavation.PlannedFinish);
        Assert.Equal(5, excavation.DurationDays); // 40h / 8h-day
        Assert.Equal(550_000.00m, excavation.BudgetCost); // TASKRSRC 500,000 + 50,000 summed
        Assert.Equal(civil.Id, excavation.WbsNodeId);

        var foundation = Assert.Single(schedule.Activities, a => a.ActivityCode == "A1020");
        Assert.Equal("Foundation", foundation.Name);
        Assert.Equal(new DateTimeOffset(2026, 1, 12, 8, 0, 0, TimeSpan.Zero), foundation.PlannedStart);
        Assert.Equal(new DateTimeOffset(2026, 1, 16, 17, 0, 0, TimeSpan.Zero), foundation.PlannedFinish);
        Assert.Equal(5, foundation.DurationDays);
        Assert.Equal(750_000.00m, foundation.BudgetCost);
        Assert.Equal(civil.Id, foundation.WbsNodeId);

        var electrical = Assert.Single(schedule.Activities, a => a.ActivityCode == "A1030");
        Assert.Equal("Electrical Rough-in", electrical.Name);
        Assert.Equal(new DateTimeOffset(2026, 1, 19, 8, 0, 0, TimeSpan.Zero), electrical.PlannedStart);
        Assert.Equal(new DateTimeOffset(2026, 1, 21, 17, 0, 0, TimeSpan.Zero), electrical.PlannedFinish);
        Assert.Equal(3, electrical.DurationDays); // 24h / 8h-day
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
        Assert.Equal(1, ssRelation.LagDays); // 8h lag / 8h-day

        Assert.Equal(7, schedule.RowCount); // 2 WBS + 3 activities + 2 relations
    }
}
