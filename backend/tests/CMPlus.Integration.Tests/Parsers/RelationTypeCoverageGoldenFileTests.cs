using CMPlus.Application.Abstractions;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Parsers.Mspdi;
using CMPlus.Infrastructure.Parsers.Xer;

namespace CMPlus.Integration.Tests.Parsers;

/// <summary>
/// S3-QA-01 gap closure: <see cref="XerScheduleParserGoldenFileTests"/> and
/// <see cref="MspdiScheduleParserGoldenFileTests"/> field-for-field compare their shared sample
/// project, which only exercises <see cref="RelationType.FS"/> and <see cref="RelationType.SS"/> -
/// the domain model (<c>RelationType</c>) and both parsers' type maps
/// (<c>XerRelationTypeMap</c>/<c>MspdiRelationTypeMap</c>) also support <see cref="RelationType.FF"/>
/// and <see cref="RelationType.SF"/>, but nothing exercised them end to end through an actual parser
/// before this file - only <c>ActivityRelationTests.Constructor_Accepts_All_Relation_Types</c>
/// (Domain layer, the entity constructor only, not the parser's XER/MSPDI-vocabulary-to-enum
/// mapping). The sprint DoD ("relation type + lag" matches the golden file "any difference turns
/// the test red") implies every relation type CM+ claims to support, not just the two the original
/// sample happened to use.
///
/// <c>tests/fixtures/goldenfiles/xer/all-relation-types.xer</c> and
/// <c>.../mspdi/all-relation-types.xml</c> describe the same project: five tasks chained
/// One-&gt;Two-&gt;Three-&gt;Four-&gt;Five via FS (lag 0), SS (lag 1 day/8h), FF (lag 2 days/16h) and
/// SF (lag 3 days/24h) respectively - a straight chain (never closing back on Task One), so this
/// does not trip <c>RelationGraphValidator</c>'s cycle check. See <see cref="FixtureFiles"/> for the
/// same hand-built-fixture provenance caveat as the primary golden files.
/// </summary>
public class RelationTypeCoverageGoldenFileTests
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
    public void Xer_Parser_Maps_All_Four_Relation_Types_With_Correct_Lag()
    {
        using var stream = FixtureFiles.OpenRead("xer/all-relation-types.xer");
        var result = new XerScheduleParser(new UnlimitedImportOptions()).Parse(stream, TenantId, ProjectId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        var schedule = result.Value;

        Assert.Single(schedule.WbsNodes);
        Assert.Equal(5, schedule.Activities.Count);
        Assert.Equal(4, schedule.Relations.Count);
        Assert.Equal(10, schedule.RowCount); // 1 WBS + 5 activities + 4 relations

        var one = Assert.Single(schedule.Activities, a => a.ActivityCode == "R1010");
        var two = Assert.Single(schedule.Activities, a => a.ActivityCode == "R1020");
        var three = Assert.Single(schedule.Activities, a => a.ActivityCode == "R1030");
        var four = Assert.Single(schedule.Activities, a => a.ActivityCode == "R1040");
        var five = Assert.Single(schedule.Activities, a => a.ActivityCode == "R1050");

        // BudgetCost field-by-field too, since this fixture also carries TASKRSRC rows.
        Assert.Equal(100_000.00m, one.BudgetCost);
        Assert.Equal(200_000.00m, two.BudgetCost);
        Assert.Equal(300_000.00m, three.BudgetCost);
        Assert.Equal(400_000.00m, four.BudgetCost);
        Assert.Equal(500_000.00m, five.BudgetCost);

        var fs = Assert.Single(schedule.Relations, r => r.RelationType == RelationType.FS);
        Assert.Equal(one.Id, fs.PredecessorActivityId);
        Assert.Equal(two.Id, fs.SuccessorActivityId);
        Assert.Equal(0, fs.LagDays);

        var ss = Assert.Single(schedule.Relations, r => r.RelationType == RelationType.SS);
        Assert.Equal(two.Id, ss.PredecessorActivityId);
        Assert.Equal(three.Id, ss.SuccessorActivityId);
        Assert.Equal(1, ss.LagDays); // 8h / 8h-day

        var ff = Assert.Single(schedule.Relations, r => r.RelationType == RelationType.FF);
        Assert.Equal(three.Id, ff.PredecessorActivityId);
        Assert.Equal(four.Id, ff.SuccessorActivityId);
        Assert.Equal(2, ff.LagDays); // 16h / 8h-day

        var sf = Assert.Single(schedule.Relations, r => r.RelationType == RelationType.SF);
        Assert.Equal(four.Id, sf.PredecessorActivityId);
        Assert.Equal(five.Id, sf.SuccessorActivityId);
        Assert.Equal(3, sf.LagDays); // 24h / 8h-day
    }

    [Fact]
    public void Mspdi_Parser_Maps_All_Four_Relation_Types_With_Correct_Lag()
    {
        using var stream = FixtureFiles.OpenRead("mspdi/all-relation-types.xml");
        var result = new MspdiScheduleParser().Parse(stream, TenantId, ProjectId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        var schedule = result.Value;

        Assert.Single(schedule.WbsNodes);
        Assert.Equal(5, schedule.Activities.Count);
        Assert.Equal(4, schedule.Relations.Count);
        Assert.Equal(10, schedule.RowCount);

        var one = Assert.Single(schedule.Activities, a => a.Name == "Task One");
        var two = Assert.Single(schedule.Activities, a => a.Name == "Task Two");
        var three = Assert.Single(schedule.Activities, a => a.Name == "Task Three");
        var four = Assert.Single(schedule.Activities, a => a.Name == "Task Four");
        var five = Assert.Single(schedule.Activities, a => a.Name == "Task Five");

        Assert.Equal(100_000.00m, one.BudgetCost);
        Assert.Equal(200_000.00m, two.BudgetCost);
        Assert.Equal(300_000.00m, three.BudgetCost);
        Assert.Equal(400_000.00m, four.BudgetCost);
        Assert.Equal(500_000.00m, five.BudgetCost);

        var fs = Assert.Single(schedule.Relations, r => r.RelationType == RelationType.FS);
        Assert.Equal(one.Id, fs.PredecessorActivityId);
        Assert.Equal(two.Id, fs.SuccessorActivityId);
        Assert.Equal(0, fs.LagDays);

        var ss = Assert.Single(schedule.Relations, r => r.RelationType == RelationType.SS);
        Assert.Equal(two.Id, ss.PredecessorActivityId);
        Assert.Equal(three.Id, ss.SuccessorActivityId);
        Assert.Equal(1, ss.LagDays); // PT8H / 8h-day

        var ff = Assert.Single(schedule.Relations, r => r.RelationType == RelationType.FF);
        Assert.Equal(three.Id, ff.PredecessorActivityId);
        Assert.Equal(four.Id, ff.SuccessorActivityId);
        Assert.Equal(2, ff.LagDays); // PT16H / 8h-day

        var sf = Assert.Single(schedule.Relations, r => r.RelationType == RelationType.SF);
        Assert.Equal(four.Id, sf.PredecessorActivityId);
        Assert.Equal(five.Id, sf.SuccessorActivityId);
        Assert.Equal(3, sf.LagDays); // PT24H / 8h-day
    }
}
