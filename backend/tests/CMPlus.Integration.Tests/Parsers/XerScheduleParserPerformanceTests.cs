using System.Diagnostics;
using System.Text;
using CMPlus.Application.Abstractions;
using CMPlus.Application.Import;
using CMPlus.Infrastructure.Parsers.Xer;

namespace CMPlus.Integration.Tests.Parsers;

/// <summary>
/// S3-SEC-01 finding H-01: the WBS re-parenting pass in <see cref="XerScheduleParser"/> was O(n^2)
/// (an O(n) linear scan plus an O(n) dictionary allocation, both run once PER node) - measured at
/// 14.3s for 24,000 legitimate PROJWBS rows in a flat depth-2 tree (well under the 50 MiB size cap).
/// This suite proves the fix (hoisting both lookups out of the loop) keeps the pass sub-quadratic,
/// and that the new <see cref="IImportOptionsProvider.MaxEntityCount"/> cap independently bounds
/// parse work for a file whose row count alone is excessive.
/// </summary>
public class XerScheduleParserPerformanceTests
{
    private sealed class ImportOptionsWithCap(long maxEntityCount) : IImportOptionsProvider
    {
        public long MaxFileSizeBytes => long.MaxValue;

        public long MaxDecompressedSizeBytes => long.MaxValue;

        public long MaxEntityCount { get; } = maxEntityCount;
    }

    /// <summary>Builds a flat depth-2 WBS tree - one "PARENT" WBSNode (itself a child of the
    /// project's own root, which is never materialized) plus <paramref name="childCount"/> children
    /// of PARENT - exactly the shape the Sprint 3 security review measured H-01 against. A minimal
    /// empty TASK table section is included so <c>XerScheduleParser</c>'s "the file contains no TASK
    /// table" check passes; no TASK rows are needed to exercise the WBS re-parenting pass.</summary>
    private static byte[] BuildFlatWbsSchedule(int childCount)
    {
        var sb = new StringBuilder();
        sb.Append("ERMHDR\t21.12\t2026-07-28\tProject\tadmin\tSample Co\tProject Management\tUSD\tBritish\n");
        sb.Append("%T\tPROJWBS\n");
        sb.Append("%F\twbs_id\tparent_wbs_id\tproj_id\twbs_short_name\twbs_name\tproj_node_flag\n");
        sb.Append("%R\tROOT\t\t500\tPROJ\tSample Project\tY\n"); // project root - not materialized as a WBSNode.
        sb.Append("%R\tPARENT\tROOT\t500\tPARENT\tTop Level\tN\n"); // one real WBSNode, child of the (skipped) root.

        for (var i = 0; i < childCount; i++)
        {
            sb.Append($"%R\tWBS{i}\tPARENT\t500\tWBS{i}\tNode {i}\tN\n");
        }

        sb.Append("%T\tTASK\n");
        sb.Append("%F\ttask_id\ttask_code\ttask_name\ttarget_start_date\ttarget_end_date\ttarget_drtn_hr_cnt\twbs_id\n");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    [Fact]
    public void Parsing_20000_Flat_Wbs_Nodes_Completes_Well_Under_A_Generous_Time_Budget()
    {
        // Sizing rationale: the Sprint 3 security review measured the UNFIXED O(n^2) algorithm at
        // 16,000 rows -> 5.7s and 24,000 rows -> 14.3s (Release build); extrapolating that curve,
        // 20,000 rows would cost the old algorithm on the order of 9-10s. An 8s budget is comfortably
        // clear of the fixed algorithm's expected sub-second runtime while still being incompatible
        // with the old quadratic behaviour, so this test cannot silently pass under a regression.
        const int childCount = 20_000;
        var content = BuildFlatWbsSchedule(childCount);
        var parser = new XerScheduleParser(new ImportOptionsWithCap(maxEntityCount: 1_000_000));

        using var stream = new MemoryStream(content);
        var stopwatch = Stopwatch.StartNew();
        var result = parser.Parse(stream, Guid.NewGuid(), Guid.NewGuid());
        stopwatch.Stop();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(childCount + 1, result.Value.WbsNodes.Count); // PARENT + every child.
        Assert.All(result.Value.WbsNodes, n => Assert.True(n.ParentWbsNodeId is not null || n.Code == "PARENT"));

        // Every child actually got re-parented under PARENT - proves the fixed loop still calls
        // WBSNode.SetParent for every node whose parent resolves, not merely that it runs fast.
        var parentNode = Assert.Single(result.Value.WbsNodes, n => n.Code == "PARENT");
        Assert.Equal(childCount, result.Value.WbsNodes.Count(n => n.ParentWbsNodeId == parentNode.Id));

        Assert.True(
            stopwatch.ElapsedMilliseconds < 8000,
            $"Expected a sub-quadratic (effectively linear) parse time for {childCount:N0} WBS nodes; " +
            $"took {stopwatch.ElapsedMilliseconds:N0} ms - this is the H-01 regression this test guards against.");
    }

    [Fact]
    public void A_Projwbs_Row_Count_Past_The_Configured_Cap_Is_Rejected_Before_Graph_Construction()
    {
        // A small injected cap (not the real 50,000 default) so this test doesn't need to build a
        // huge fixture to prove the rejection path - mirrors the pattern already used for
        // MaxDecompressedSizeBytes in HardeningTests.TinyDecompressedCapImportOptions.
        const int childCount = 11;
        var content = BuildFlatWbsSchedule(childCount);
        var parser = new XerScheduleParser(new ImportOptionsWithCap(maxEntityCount: 10));

        using var stream = new MemoryStream(content);
        var result = parser.Parse(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.StartsWith(ImportErrorCodes.EntityCountExceeded, result.Error);
        Assert.Contains("PROJWBS", result.Error);
    }

    [Fact]
    public void A_Projwbs_Row_Count_At_Exactly_The_Cap_Is_Not_Rejected()
    {
        const int childCount = 8; // + 1 PARENT + 1 ROOT = 10 PROJWBS rows total, exactly at the cap.
        var content = BuildFlatWbsSchedule(childCount);
        var parser = new XerScheduleParser(new ImportOptionsWithCap(maxEntityCount: 10));

        using var stream = new MemoryStream(content);
        var result = parser.Parse(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
    }
}
