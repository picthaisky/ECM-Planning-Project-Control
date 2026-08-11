using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Parsers.Xer;
using CMPlus.Integration.Tests.Parsers;

namespace CMPlus.Integration.Tests.Cpm;

/// <summary>
/// S5-QA-02: import a real <c>.XER</c> file, run it through the actual Sprint 5 CPM engine
/// (<see cref="CpmEngine"/>) exactly the way <c>RecalculateCpmCommandHandler</c> does (parse graph
/// -&gt; <see cref="CpmActivityInput"/>/<see cref="CpmRelationInput"/> -&gt; <see cref="CpmEngine.Calculate"/>),
/// and confirm every activity's computed ES/EF/LS/LF/TF/FF/critical flag matches an expected value.
///
/// <para><b>Honesty constraint - read before treating this as "P6 reconciliation" (docs/10 §7
/// S5-QA-02 DoD literally says "ตรงกับค่าอ้างอิงจาก P6"):</b> the DoD's own artifact path and Sprint 3's
/// <see cref="FixtureFiles"/> both already establish that no genuine Primavera P6 install/export was
/// ever available in this environment. <c>xer/all-relation-types.xer</c> and
/// <c>xer/sample-schedule.xer</c> are the same hand-built, format-spec-compliant synthetic fixtures
/// Sprint 3 authored and QA'd (see <see cref="FixtureFiles"/>'s provenance remarks) - there is no
/// real P6-computed reference schedule to diff against for either file, so this class does
/// <b>NOT</b>, and cannot honestly claim to, "reconcile against P6". What it does instead, per this
/// sprint's explicit instruction not to fabricate a P6 comparison that doesn't exist:
/// <list type="number">
/// <item><description>Parse the golden <c>.xer</c> file with the real, production
/// <see cref="XerScheduleParser"/> (not a hand-rolled test double) to get the actual
/// <c>Activity</c>/<c>ActivityRelation</c> graph a real import would produce.</description></item>
/// <item><description>Independently hand-derive the expected ES/EF/LS/LF/TF/FF/critical result for
/// that exact graph directly from <see cref="RelationConstraints"/>'s own documented forward/
/// backward formulas (worked by hand in the comments below, the same discipline
/// <c>RelationTypeCoverageGoldenFileTests</c> and <c>XerScheduleParserGoldenFileTests</c> used for
/// parser-level fields) - not by running <see cref="CpmEngine"/> and copying its output.</description></item>
/// <item><description>Assert the engine's actual output equals that independently-derived
/// expectation.</description></item>
/// </list>
/// This is a <b>golden-file consistency check</b> (engine agrees with an independent hand
/// calculation over a real parsed file), explicitly <b>pending a real P6 export</b> for true
/// reconciliation - flagged forward exactly like Sprint 3's own <see cref="FixtureFiles"/> caveat,
/// not silently upgraded to a claim this sprint cannot back up.</para>
/// </summary>
public class P6ReconciliationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private sealed class UnlimitedImportOptions : IImportOptionsProvider
    {
        public long MaxFileSizeBytes => long.MaxValue;

        public long MaxDecompressedSizeBytes => long.MaxValue;

        public long MaxEntityCount => long.MaxValue;
    }

    private static (int Es, int Ef, int Ls, int Lf, int Tf, int Ff, bool IsCritical) Tuple(CpmActivityResult r) =>
        (r.EarlyStart, r.EarlyFinish, r.LateStart, r.LateFinish, r.TotalFloat, r.FreeFloat, r.IsCritical);

    [Fact]
    public void All_Relation_Types_Golden_File_Matches_An_Independently_Hand_Derived_CPM_Result_Not_The_Engine_Itself()
    {
        // xer/all-relation-types.xer: five tasks chained One(R1010,D=2)->Two(R1020,D=1)->
        // Three(R1030,D=1)->Four(R1040,D=1)->Five(R1050,D=1) via FS(lag0)/SS(lag1)/FF(lag2)/SF(lag3)
        // respectively (16h/8h/8h/8h/8h durations and 0/8/16/24h lags on the standard 8h workday -
        // see XerScheduleParser.TryConvertHoursToWholeDays and RelationTypeCoverageGoldenFileTests,
        // which already golden-file-tests the parser's own field mapping for this exact file).
        //
        // Hand derivation against RelationConstraints (independent of CpmEngine - see class remarks):
        //   ES_1=0,EF_1=2 (no predecessor).
        //   ES_2 (FS lag0 from 1) = EF_1+0=2 -> EF_2=2+1=3.
        //   ES_3 (SS lag1 from 2) = ES_2+1=3 -> EF_3=3+1=4.
        //   ES_4 (FF lag2 from 3) = EF_3+2-D_4(1)=4+2-1=5 -> EF_4=5+1=6.
        //   ES_5 (SF lag3 from 4) = ES_4+3-D_5(1)=5+3-1=7 -> EF_5=7+1=8.
        //   Project duration = max(2,3,4,6,8) = 8.
        //   Backward (single straight chain, no branching at all - the backward pass must retrace
        //   the forward pass exactly for every activity, so TF=FF=0 throughout regardless of which
        //   relation type/lag each edge uses): LS_5=7,LF_5=8; LS_4=5,LF_4=6; LS_3=3,LF_3=4;
        //   LS_2=2,LF_2=3; LS_1=0,LF_1=2. All five activities critical.
        using var stream = FixtureFiles.OpenRead("xer/all-relation-types.xer");
        var parseResult = new XerScheduleParser(new UnlimitedImportOptions()).Parse(stream, TenantId, ProjectId);
        Assert.True(parseResult.IsSuccess, parseResult.IsFailure ? parseResult.Error : string.Empty);
        var schedule = parseResult.Value;

        var activityInputs = schedule.Activities.Select(a => new CpmActivityInput(a.Id, a.DurationDays)).ToList();
        var relationInputs = schedule.Relations
            .Select(r => new CpmRelationInput(r.PredecessorActivityId, r.SuccessorActivityId, r.RelationType, r.LagDays))
            .ToList();

        var cpmResult = CpmEngine.Calculate(activityInputs, relationInputs);
        Assert.True(cpmResult.IsSuccess, cpmResult.IsFailure ? cpmResult.Error : string.Empty);

        Assert.Equal(8, cpmResult.Value.ProjectDurationDays);

        var idByCode = schedule.Activities.ToDictionary(a => a.ActivityCode, a => a.Id);
        var resultById = cpmResult.Value.Activities.ToDictionary(r => r.ActivityId);

        Assert.Equal((0, 2, 0, 2, 0, 0, true), Tuple(resultById[idByCode["R1010"]]));
        Assert.Equal((2, 3, 2, 3, 0, 0, true), Tuple(resultById[idByCode["R1020"]]));
        Assert.Equal((3, 4, 3, 4, 0, 0, true), Tuple(resultById[idByCode["R1030"]]));
        Assert.Equal((5, 6, 5, 6, 0, 0, true), Tuple(resultById[idByCode["R1040"]]));
        Assert.Equal((7, 8, 7, 8, 0, 0, true), Tuple(resultById[idByCode["R1050"]]));

        var expectedCriticalPathCodes = new[] { "R1010", "R1020", "R1030", "R1040", "R1050" };
        var actualCriticalPathCodes = cpmResult.Value.CriticalPath
            .Select(id => schedule.Activities.Single(a => a.Id == id).ActivityCode)
            .ToList();
        Assert.Equal(expectedCriticalPathCodes, actualCriticalPathCodes);
    }

    [Fact]
    public void Sample_Schedule_Golden_File_Matches_An_Independently_Hand_Derived_CPM_Result_Including_Genuine_Non_Zero_Float()
    {
        // xer/sample-schedule.xer (Sprint 3's primary golden file - also exercised field-for-field
        // by XerScheduleParserGoldenFileTests): A1010(D=5, no predecessor) -> A1020(D=5, FS lag0
        // from A1010) -> A1030(D=3, SS lag1 from A1020). Chosen as a second fixture specifically
        // because - unlike the single-chain all-relation-types network above, where every activity
        // is trivially critical - this one has real branching-free-but-lagged structure that
        // produces a genuine non-zero float on one activity, a better test of TF/FF arithmetic
        // itself rather than only of the "no branching -> everything critical" invariant.
        //
        // Hand derivation against RelationConstraints (independent of CpmEngine):
        //   ES_101=0,EF_101=5 (no predecessor).
        //   ES_102 (FS lag0 from 101) = EF_101+0=5 -> EF_102=5+5=10.
        //   ES_103 (SS lag1 from 102) = ES_102+1=6 -> EF_103=6+3=9.
        //   Project duration = max(5,10,9) = 10.
        //   Backward: LF_103=10 (no successor), LS_103=10-3=7.
        //   LF_102 (successor 103 via SS lag1): BackwardConstraint SS = LS_103-lag+D_102 = 7-1+5=11,
        //     clamped to the project-duration ceiling (10) since 11 is looser -> LF_102=10 ->
        //     LS_102=10-5=5 -> TF_102=5-5=0 (critical).
        //   LF_101 (successor 102 via FS lag0) = LS_102-0=5 -> LF_101=5 -> LS_101=5-5=0 ->
        //     TF_101=0 (critical).
        //   TF_103 = LS_103-ES_103 = 7-6 = 1 (NOT critical - A1030 has one day of float; the
        //     project's 10-day duration is driven by A1010->A1020, not by A1030).
        //   Free float: FF_103=TF_103=1 (no successor). FF_102 (via SS lag1): forward constraint =
        //     ES_102+lag=5+1=6 -> FF_102=ES_103-6=6-6=0. FF_101 (via FS lag0): forward constraint =
        //     EF_101+0=5 -> FF_101=ES_102-5=5-5=0.
        //   Critical path: A1010 -> A1020, duration 10.
        using var stream = FixtureFiles.OpenRead("xer/sample-schedule.xer");
        var parseResult = new XerScheduleParser(new UnlimitedImportOptions()).Parse(stream, TenantId, ProjectId);
        Assert.True(parseResult.IsSuccess, parseResult.IsFailure ? parseResult.Error : string.Empty);
        var schedule = parseResult.Value;

        var activityInputs = schedule.Activities.Select(a => new CpmActivityInput(a.Id, a.DurationDays)).ToList();
        var relationInputs = schedule.Relations
            .Select(r => new CpmRelationInput(r.PredecessorActivityId, r.SuccessorActivityId, r.RelationType, r.LagDays))
            .ToList();

        var cpmResult = CpmEngine.Calculate(activityInputs, relationInputs);
        Assert.True(cpmResult.IsSuccess, cpmResult.IsFailure ? cpmResult.Error : string.Empty);

        Assert.Equal(10, cpmResult.Value.ProjectDurationDays);

        var idByCode = schedule.Activities.ToDictionary(a => a.ActivityCode, a => a.Id);
        var resultById = cpmResult.Value.Activities.ToDictionary(r => r.ActivityId);

        Assert.Equal((0, 5, 0, 5, 0, 0, true), Tuple(resultById[idByCode["A1010"]]));
        Assert.Equal((5, 10, 5, 10, 0, 0, true), Tuple(resultById[idByCode["A1020"]]));
        Assert.Equal((6, 9, 7, 10, 1, 1, false), Tuple(resultById[idByCode["A1030"]]));

        var expectedCriticalPathCodes = new[] { "A1010", "A1020" };
        var actualCriticalPathCodes = cpmResult.Value.CriticalPath
            .Select(id => schedule.Activities.Single(a => a.Id == id).ActivityCode)
            .ToList();
        Assert.Equal(expectedCriticalPathCodes, actualCriticalPathCodes);
    }
}
