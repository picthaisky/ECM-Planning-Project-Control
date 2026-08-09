using System.Text.Json;
using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Cpm;

/// <summary>
/// S5-BE-01/02/03 (backend-developer's own sanity check, per the non-negotiable "implement the
/// worked examples as your own sanity check before handing to QA" - S5-QA-01 owns the fuller,
/// independent test matrix).
///
/// <para><b>CPM-NETWORK-1</b> (the canonical A/B/C/D fixture) is loaded verbatim from
/// <c>backend/tests/CMPlus.Application.Tests/Fixtures/cpm-fixtures.json</c> - no number is
/// hand-copied here, same discipline as <c>RoutingFixtureTests</c> (Sprint 2 precedent), so a
/// future edit to the fixture file cannot silently drift out of sync with this test.</para>
///
/// <para><b>CPM-EDGE-SS-LAG / CPM-EDGE-FF-LAG / CPM-EDGE-ISOLATED-ACTIVITY</b>: the fixture file's
/// own <c>"gap"</c> field for each of these says, verbatim, that no worked network/expected numbers
/// exist yet and "do NOT fabricate expected values ... obtain a worked example from domain-expert".
/// Per this sprint's explicit instruction (no domain-expert pass was run for these three), the
/// expected numbers below are backend-developer-constructed - derived mechanically from
/// <see cref="RelationConstraints"/>'s own per-type formulas applied to the *same* four-activity
/// network shape as CPM-NETWORK-1 (only the flagged relation's type/lag changes), not sourced from
/// cpm-method.md or domain-rules.md. This is the same "documented provenance, not silently
/// invented" standard Sprint 3's golden-file fixtures used. See the Sprint 5 backend-developer
/// report for the full hand derivation and a recommendation that domain-expert confirm these
/// against a real P6 reconciliation later (S5-QA-02).</para>
/// </summary>
public class CpmEngineTests
{
    private static readonly JsonElement FixtureRoot = LoadFixtureRoot();

    private static JsonElement LoadFixtureRoot()
    {
        var path = SolutionRelativePath(Path.Combine("tests", "CMPlus.Application.Tests", "Fixtures", "cpm-fixtures.json"));
        var json = File.ReadAllText(path);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static string SolutionRelativePath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CMPlus.sln")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? throw new InvalidOperationException("Could not locate CMPlus.sln from the test output directory.")
            : Path.Combine(dir.FullName, relative);
    }

    private static JsonElement Fixture(string fixtureId) =>
        FixtureRoot.GetProperty("fixtures").EnumerateArray()
            .Single(f => f.GetProperty("fixtureId").GetString() == fixtureId);

    [Fact]
    public void CPM_NETWORK_1_Reproduces_The_Canonical_cpm_method_md_Fixture_Exactly()
    {
        var fixture = Fixture("CPM-NETWORK-1");
        var inputs = fixture.GetProperty("inputs");
        var expected = fixture.GetProperty("expected");

        var codeToId = inputs.GetProperty("activities").EnumerateArray()
            .ToDictionary(a => a.GetProperty("id").GetString()!, _ => Guid.NewGuid());
        var idToCode = codeToId.ToDictionary(kv => kv.Value, kv => kv.Key);

        var activities = inputs.GetProperty("activities").EnumerateArray()
            .Select(a => new CpmActivityInput(codeToId[a.GetProperty("id").GetString()!], a.GetProperty("durationDays").GetInt32()))
            .ToList();

        var relations = inputs.GetProperty("relations").EnumerateArray()
            .Select(r => new CpmRelationInput(
                codeToId[r.GetProperty("predecessorId").GetString()!],
                codeToId[r.GetProperty("successorId").GetString()!],
                Enum.Parse<RelationType>(r.GetProperty("type").GetString()!),
                r.GetProperty("lagDays").GetInt32()))
            .ToList();

        var result = CpmEngine.Calculate(activities, relations);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(expected.GetProperty("projectDurationDays").GetInt32(), result.Value.ProjectDurationDays);

        foreach (var expectedActivity in expected.GetProperty("activities").EnumerateArray())
        {
            var code = expectedActivity.GetProperty("id").GetString()!;
            var actual = result.Value.Activities.Single(a => idToCode[a.ActivityId] == code);

            Assert.Equal(expectedActivity.GetProperty("es").GetInt32(), actual.EarlyStart);
            Assert.Equal(expectedActivity.GetProperty("ef").GetInt32(), actual.EarlyFinish);
            Assert.Equal(expectedActivity.GetProperty("ls").GetInt32(), actual.LateStart);
            Assert.Equal(expectedActivity.GetProperty("lf").GetInt32(), actual.LateFinish);
            Assert.Equal(expectedActivity.GetProperty("tf").GetInt32(), actual.TotalFloat);
            Assert.Equal(expectedActivity.GetProperty("ff").GetInt32(), actual.FreeFloat);
            Assert.Equal(expectedActivity.GetProperty("isCritical").GetBoolean(), actual.IsCritical);
        }

        var expectedCriticalPath = expected.GetProperty("criticalPath").EnumerateArray().Select(e => e.GetString()!).ToList();
        var actualCriticalPath = result.Value.CriticalPath.Select(id => idToCode[id]).ToList();
        Assert.Equal(expectedCriticalPath, actualCriticalPath);
    }

    [Fact]
    public void CPM_EDGE_SS_LAG_Backend_Constructed_Fixture_SS_Relation_With_Lag_2()
    {
        // Same A/B/C/D network shape as CPM-NETWORK-1, with only A->B changed from FS(lag 0) to
        // SS(lag 2); A->C, B->D, C->D stay FS(lag 0) - see class remarks for provenance. Hand
        // derivation (hand-derivation kept short; full working is in the Sprint 5 report):
        //   ES_A=0,EF_A=5 (unchanged - A has no predecessor).
        //   ES_B = ES_A + lag = 0+2 = 2 (SS constraint, not EF_A) -> EF_B = 2+3 = 5.
        //   ES_C = EF_A+0 = 5 (unchanged) -> EF_C = 11.
        //   ES_D = max(EF_B+0=5, EF_C+0=11) = 11 -> EF_D = 15. Same project duration as the canonical fixture.
        //   Backward: LF_D=15,LS_D=11. LF_C=LS_D-0=11,LS_C=5,TF_C=0 (critical, unchanged).
        //   LF_B (successor D, FS lag0) = LS_D-0=11 -> LS_B=11-3=8 -> TF_B=8-2=6 (not critical - lag "used up" 2 of what was 3 slack in the canonical fixture, leaving 6).
        //   LF_A (successors B via SS lag2, C via FS lag0): SS backward constraint = LS_B - lag + D_A = 8-2+5=11; FS constraint = LS_C-0=5. min(11,5)=5 -> LS_A=0 -> TF_A=0 (critical, unchanged).
        //   Free float: FF_D=TF_D=0 (no successor). FF_C = ES_D - (EF_C+0) = 11-11=0.
        //   FF_B = ES_D - (EF_B+0) = 11-5=6. FF_A: via B (SS): ES_B - (ES_A+lag) = 2-2=0; via C (FS): ES_C-(EF_A+0)=5-5=0 -> FF_A=0.
        //   Critical path unchanged: A -> C -> D, duration 15.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();

        var activities = new[]
        {
            new CpmActivityInput(a, 5), new CpmActivityInput(b, 3), new CpmActivityInput(c, 6), new CpmActivityInput(d, 4),
        };
        var relations = new[]
        {
            new CpmRelationInput(a, b, RelationType.SS, 2),
            new CpmRelationInput(a, c, RelationType.FS, 0),
            new CpmRelationInput(b, d, RelationType.FS, 0),
            new CpmRelationInput(c, d, RelationType.FS, 0),
        };

        var result = CpmEngine.Calculate(activities, relations);

        Assert.True(result.IsSuccess);
        Assert.Equal(15, result.Value.ProjectDurationDays);

        var byId = result.Value.Activities.ToDictionary(r => r.ActivityId);
        Assert.Equal((0, 5, 0, 5, 0, 0, true), Tuple(byId[a]));
        Assert.Equal((2, 5, 8, 11, 6, 6, false), Tuple(byId[b]));
        Assert.Equal((5, 11, 5, 11, 0, 0, true), Tuple(byId[c]));
        Assert.Equal((11, 15, 11, 15, 0, 0, true), Tuple(byId[d]));
        Assert.Equal([a, c, d], result.Value.CriticalPath);
    }

    [Fact]
    public void CPM_EDGE_FF_LAG_Backend_Constructed_Fixture_FF_Relation_With_Lag_1()
    {
        // Same A/B/C/D network shape as CPM-NETWORK-1, with only B->D changed from FS(lag 0) to
        // FF(lag 1); A->B, A->C, C->D stay FS(lag 0) - see class remarks for provenance.
        //   ES_A=0,EF_A=5. ES_B=EF_A+0=5,EF_B=8 (unchanged). ES_C=5,EF_C=11 (unchanged).
        //   ES_D: from B via FF(lag1) -> EF_B+lag-D_D = 8+1-4=5; from C via FS(lag0) -> EF_C+0=11.
        //     ES_D=max(5,11)=11 -> EF_D=15 (same project duration).
        //   Backward: LF_D=15,LS_D=11. LF_C=LS_D-0=11,LS_C=5,TF_C=0 (critical, unchanged).
        //   LF_B (successor D via FF lag1) = LF_D-lag = 15-1=14 -> LS_B=14-3=11 -> TF_B=11-5=6 (not critical).
        //   LF_A (successors B FS lag0, C FS lag0): LS_B-0=11; LS_C-0=5 -> min=5 -> LS_A=0 -> TF_A=0 (critical, unchanged).
        //   Free float: FF_D=TF_D=0. FF_C=ES_D-(EF_C+0)=11-11=0.
        //   FF_B (via FF lag1): constraint=EF_B+lag-D_D=8+1-4=5 -> FF_B=ES_D-5=11-5=6.
        //   FF_A: via B(FS lag0)=ES_B-(EF_A+0)=5-5=0; via C(FS lag0)=ES_C-5=0 -> FF_A=0.
        //   Critical path unchanged: A -> C -> D, duration 15.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();

        var activities = new[]
        {
            new CpmActivityInput(a, 5), new CpmActivityInput(b, 3), new CpmActivityInput(c, 6), new CpmActivityInput(d, 4),
        };
        var relations = new[]
        {
            new CpmRelationInput(a, b, RelationType.FS, 0),
            new CpmRelationInput(a, c, RelationType.FS, 0),
            new CpmRelationInput(b, d, RelationType.FF, 1),
            new CpmRelationInput(c, d, RelationType.FS, 0),
        };

        var result = CpmEngine.Calculate(activities, relations);

        Assert.True(result.IsSuccess);
        Assert.Equal(15, result.Value.ProjectDurationDays);

        var byId = result.Value.Activities.ToDictionary(r => r.ActivityId);
        Assert.Equal((0, 5, 0, 5, 0, 0, true), Tuple(byId[a]));
        Assert.Equal((5, 8, 11, 14, 6, 6, false), Tuple(byId[b]));
        Assert.Equal((5, 11, 5, 11, 0, 0, true), Tuple(byId[c]));
        Assert.Equal((11, 15, 11, 15, 0, 0, true), Tuple(byId[d]));
        Assert.Equal([a, c, d], result.Value.CriticalPath);
    }

    [Fact]
    public void CPM_EDGE_ISOLATED_ACTIVITY_Gets_Float_Equal_To_The_Projects_Own_Total_Slack_Not_Zero_Not_A_Crash()
    {
        // Backend-developer-constructed network (fixture file names only a standalone ISO(4) with
        // no surrounding network - "concrete ES/EF/TF ... depend on the surrounding project's
        // finish date, which the source does not define in isolation"): reuses CPM-NETWORK-1's own
        // A/B/C/D network (duration 15, unaffected by ISO) plus one extra activity ISO(4) with
        // zero relations at all.
        //   ES_ISO=0 (no predecessor - project start), EF_ISO=4.
        //   Project duration stays 15 (max EF across A..D,ISO is still D's 15, since 4 < 15).
        //   LF_ISO = project finish = 15 (no successor) -> LS_ISO = 15-4=11 -> TF_ISO = 11-0 = 11.
        //   FF_ISO (no successor) = TF_ISO = 11. Not zero, not a crash, and not critical.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        var iso = Guid.NewGuid();

        var activities = new[]
        {
            new CpmActivityInput(a, 5), new CpmActivityInput(b, 3), new CpmActivityInput(c, 6),
            new CpmActivityInput(d, 4), new CpmActivityInput(iso, 4),
        };
        var relations = new[]
        {
            new CpmRelationInput(a, b, RelationType.FS, 0),
            new CpmRelationInput(a, c, RelationType.FS, 0),
            new CpmRelationInput(b, d, RelationType.FS, 0),
            new CpmRelationInput(c, d, RelationType.FS, 0),
        };

        var result = CpmEngine.Calculate(activities, relations);

        Assert.True(result.IsSuccess);
        Assert.Equal(15, result.Value.ProjectDurationDays);

        var isoResult = result.Value.Activities.Single(r => r.ActivityId == iso);
        Assert.Equal(0, isoResult.EarlyStart);
        Assert.Equal(4, isoResult.EarlyFinish);
        Assert.Equal(11, isoResult.LateStart);
        Assert.Equal(15, isoResult.LateFinish);
        Assert.Equal(11, isoResult.TotalFloat);
        Assert.Equal(11, isoResult.FreeFloat);
        Assert.False(isoResult.IsCritical);
        Assert.DoesNotContain(iso, result.Value.CriticalPath);
    }

    [Fact]
    public void Negative_Lag_Lead_Is_Supported_Arithmetically()
    {
        // S5-QA-01 explicitly calls out "lag ติดลบถ้ารองรับ" (negative lag, if supported) as a case
        // to cover. A(5)->B(3) FS with lag -2 (a 2-day lead: B may start 2 days before A finishes).
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var result = CpmEngine.Calculate(
            [new CpmActivityInput(a, 5), new CpmActivityInput(b, 3)],
            [new CpmRelationInput(a, b, RelationType.FS, -2)]);

        Assert.True(result.IsSuccess);
        var byId = result.Value.Activities.ToDictionary(r => r.ActivityId);
        Assert.Equal(0, byId[a].EarlyStart);
        Assert.Equal(5, byId[a].EarlyFinish);
        Assert.Equal(3, byId[b].EarlyStart); // EF_A + (-2) = 5 - 2 = 3
        Assert.Equal(6, byId[b].EarlyFinish);
    }

    [Fact]
    public void An_Empty_Project_With_Zero_Activities_Does_Not_Crash()
    {
        var result = CpmEngine.Calculate([], []);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Activities);
        Assert.Equal(0, result.Value.ProjectDurationDays);
        Assert.Empty(result.Value.CriticalPath);
    }

    private static (int Es, int Ef, int Ls, int Lf, int Tf, int Ff, bool IsCritical) Tuple(CpmActivityResult r) =>
        (r.EarlyStart, r.EarlyFinish, r.LateStart, r.LateFinish, r.TotalFloat, r.FreeFloat, r.IsCritical);
}
