using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Parsers.Xer;
using CMPlus.Integration.Tests.Parsers;

namespace CMPlus.Integration.Tests.Cpm;

/// <summary>
/// Sprint 5 handoff concern #3: <see cref="GraphValidator"/> (Application, S5-BE-03) does not
/// literally reuse <c>CMPlus.Infrastructure.Parsers.Common.RelationGraphValidator</c> (Sprint 3,
/// <c>internal</c> to Infrastructure - Application may not depend on Infrastructure per ADR-0001) -
/// it is an independent, mirrored re-implementation. Both are DFS-based cycle detectors, but one is
/// recursive over string external ids (Sprint 3, sized for XER/MSPDI import graphs) and the other is
/// an explicit-stack iterative DFS over <see cref="Guid"/> internal ids (Sprint 5, sized to survive a
/// 10,000-activity near-linear chain without a stack overflow) - different implementations of the
/// "is there a cycle" question are exactly the kind of place two independently-written algorithms can
/// silently disagree on an edge case. This class does not have access to
/// <c>RelationGraphValidator</c> directly (it is <c>internal</c> and there is no
/// <c>InternalsVisibleTo</c> granting this test project access - confirmed by inspection), so
/// agreement is checked the way a real caller would observe it: through <see cref="XerScheduleParser"/>'s
/// public <c>Parse</c> API (which uses <c>RelationGraphValidator</c> internally) against the identical
/// graph shape fed directly to <see cref="GraphValidator.Validate"/>.
/// </summary>
public class CycleDetectionCrossValidationTests
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
    public void Sprint3_Parser_Level_And_Sprint5_Engine_Level_Cycle_Detectors_Agree_On_The_Same_Golden_File_Cycle()
    {
        // xer/cycle-schedule.xer: three tasks A1010 -> A1020 -> A1030 -> A1010, all FS(lag 0) - a
        // straightforward 3-node cycle, the same shape backend-developer's own
        // GraphValidatorTests.A_Longer_Cycle_Through_An_Otherwise_Valid_Chain_Is_Still_Detected uses.
        //
        // Step 1: confirm the real Sprint 3 XerScheduleParser (which internally calls
        // RelationGraphValidator.FindCycle, not GraphValidator) rejects this file as a cycle and
        // reports the offending activity codes.
        using var stream = FixtureFiles.OpenRead("xer/cycle-schedule.xer");
        var parseResult = new XerScheduleParser(new UnlimitedImportOptions()).Parse(stream, TenantId, ProjectId);

        Assert.True(parseResult.IsFailure);
        Assert.StartsWith(CMPlus.Application.Import.ImportErrorCodes.RelationCycleDetected, parseResult.Error);

        // The parser's error message is "<code>: A1010 -> A1020 -> A1030 -> A1010" (or some
        // rotation/direction of the same 3-node loop, depending on DFS traversal order) - extract
        // just the activity codes it names as being part of the cycle.
        var parserCycleCodes = parseResult.Error
            .Split(':', 2)[1]
            .Split("->", StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "A1010", "A1020", "A1030" }, parserCycleCodes);

        // Step 2: build the *identical* graph shape (same three activity codes, same FS(lag0) cycle
        // A1010->A1020->A1030->A1010) directly against the Sprint 5 Application-layer GraphValidator
        // - independently of the parser, since a rejected XerScheduleParser.Parse is guaranteed to
        // return zero constructed entities (no Activity/ActivityRelation graph survives to hand to
        // GraphValidator directly) - and confirm it agrees this is a cycle, over the same set of
        // activity codes.
        var a1010 = Guid.NewGuid();
        var a1020 = Guid.NewGuid();
        var a1030 = Guid.NewGuid();
        var codeById = new Dictionary<Guid, string>
        {
            [a1010] = "A1010",
            [a1020] = "A1020",
            [a1030] = "A1030",
        };

        var engineResult = GraphValidator.Validate(
            [new CpmActivityInput(a1010, 5), new CpmActivityInput(a1020, 5), new CpmActivityInput(a1030, 3)],
            [
                new CpmRelationInput(a1010, a1020, RelationType.FS, 0),
                new CpmRelationInput(a1020, a1030, RelationType.FS, 0),
                new CpmRelationInput(a1030, a1010, RelationType.FS, 0),
            ]);

        Assert.False(engineResult.IsValid);
        Assert.Equal(CpmValidationErrorCodes.CycleDetected, engineResult.ErrorCode);
        Assert.NotNull(engineResult.CycleChain);

        var engineCycleCodes = engineResult.CycleChain!.Select(id => codeById[id]).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "A1010", "A1020", "A1030" }, engineCycleCodes);

        // Both validators agree: same three activities, same verdict (reject as a cycle). This is
        // the extent to which agreement can be demonstrated without InternalsVisibleTo access to
        // RelationGraphValidator itself; it is not a proof the two DFS implementations agree on
        // every conceivable graph shape (e.g. multiple disjoint cycles in one graph), only that they
        // agree on this golden-file's concrete cycle.
        Assert.Equal(parserCycleCodes, engineCycleCodes);
    }

    [Fact]
    public void An_Acyclic_Golden_File_Is_Accepted_By_Both_The_Parser_And_The_Engine_Validator()
    {
        // Negative-direction agreement check using the same all-relation-types.xer straight chain
        // (no cycle at all): both validators must accept it.
        using var stream = FixtureFiles.OpenRead("xer/all-relation-types.xer");
        var parseResult = new XerScheduleParser(new UnlimitedImportOptions()).Parse(stream, TenantId, ProjectId);
        Assert.True(parseResult.IsSuccess, parseResult.IsFailure ? parseResult.Error : string.Empty);

        var schedule = parseResult.Value;
        var activityInputs = schedule.Activities.Select(a => new CpmActivityInput(a.Id, a.DurationDays)).ToList();
        var relationInputs = schedule.Relations
            .Select(r => new CpmRelationInput(r.PredecessorActivityId, r.SuccessorActivityId, r.RelationType, r.LagDays))
            .ToList();

        var engineResult = GraphValidator.Validate(activityInputs, relationInputs);

        Assert.True(engineResult.IsValid);
        Assert.Null(engineResult.ErrorCode);
    }
}
