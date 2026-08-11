using System.Text.Json;
using CMPlus.Application.Services.Evm;

namespace CMPlus.Application.Tests.Evm;

/// <summary>
/// S7-BE-01 (backend-developer's own sanity check, per the non-negotiable "implement the worked
/// examples as your own sanity check before handing to QA" - S7-QA-01 owns the fuller, independent
/// test matrix). Fixtures A/B/C are loaded verbatim from
/// <c>backend/tests/CMPlus.Application.Tests/Fixtures/evm-fixtures.json</c> - no number is
/// hand-copied here, the same discipline <c>CpmEngineTests</c> established for
/// <c>cpm-fixtures.json</c>.
/// </summary>
public class EvmEngineTests
{
    private static readonly JsonElement FixtureRoot = LoadFixtureRoot();

    private static JsonElement LoadFixtureRoot()
    {
        var path = SolutionRelativePath(Path.Combine("tests", "CMPlus.Application.Tests", "Fixtures", "evm-fixtures.json"));
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

    private static decimal Dec(JsonElement element, string property) => element.GetProperty(property).GetDecimal();

    /// <summary>Fixture-authored "FullPrecision" values can legitimately differ from this engine's
    /// own decimal division chain in the last 1-2 significant digits for a non-terminating (repeating)
    /// decimal result (evm-fixtures.json's own precisionNote) - compared after rounding to 20 decimal
    /// places, still vastly stricter than this codebase's money@2dp/ratio@6dp, so this remains a real
    /// precision assertion rather than a tautology.</summary>
    private static void AssertFullPrecisionEqual(decimal expected, decimal actual)
    {
        const int comparisonDecimals = 20;
        Assert.Equal(
            Math.Round(expected, comparisonDecimals, MidpointRounding.AwayFromZero),
            Math.Round(actual, comparisonDecimals, MidpointRounding.AwayFromZero));
    }

    /// <summary>Fixture A (evm-fixtures.json): SV=-100,000.00, CV=-50,000.00, SPI=0.75,
    /// CPI=0.857142857... (6/7 repeating, asserted at full decimal precision), TCPI_BAC likewise.</summary>
    [Fact]
    public void Fixture_A_Typical_Behind_Schedule_And_Over_Budget()
    {
        var fixture = Fixture("EVM-A");
        var inputs = fixture.GetProperty("inputs");
        var expected = fixture.GetProperty("expected");

        var bac = Dec(inputs, "bac");
        var pv = Dec(inputs, "pv");
        var ev = Dec(inputs, "ev");
        var ac = Dec(inputs, "ac");

        var result = EvmEngine.Compute(bac, pv, ev, ac);

        Assert.Equal(bac, result.Bac);
        Assert.Equal(pv, result.Pv);
        Assert.Equal(ev, result.Ev);
        Assert.Equal(ac, result.Ac);
        Assert.Equal(Dec(expected, "sv"), result.Sv);
        Assert.Equal(Dec(expected, "cv"), result.Cv);
        Assert.Equal(Dec(expected, "spi"), result.Spi);

        Assert.NotNull(result.Cpi);
        AssertFullPrecisionEqual(Dec(expected, "cpiFullPrecision"), result.Cpi!.Value);

        Assert.NotNull(result.TcpiBac);
        AssertFullPrecisionEqual(Dec(expected, "tcpiFullPrecision"), result.TcpiBac!.Value);
    }

    /// <summary>Fixture B (evm-fixtures.json): nothing started -> SPI/CPI return null, never 0/NaN.</summary>
    [Fact]
    public void Fixture_B_Not_Started_Returns_Null_Never_Zero_Or_NaN()
    {
        var fixture = Fixture("EVM-B");
        var inputs = fixture.GetProperty("inputs");
        var expected = fixture.GetProperty("expected");

        // BAC is deliberately non-zero and NOT part of the fixture's own inputs (evm-formulas.md's
        // "not started" edge case is about PV/EV/AC, independent of BAC) - kept far from zero here so
        // this test cannot be confused with the separate "zero-budget project" edge case
        // (EacCalculator's own Fixture I), which is a different rule entirely.
        const decimal bac = 1_000_000.00m;
        var pv = Dec(inputs, "pv");
        var ev = Dec(inputs, "ev");
        var ac = Dec(inputs, "ac");

        var result = EvmEngine.Compute(bac, pv, ev, ac);

        Assert.Equal(Dec(expected, "sv"), result.Sv);
        Assert.Equal(Dec(expected, "cv"), result.Cv);
        Assert.Null(result.Spi);
        Assert.Null(result.Cpi);
    }

    /// <summary>Fixture C (evm-fixtures.json): a zero-budget activity contributes exactly 0.00 to EV
    /// - no division, no NaN.</summary>
    [Fact]
    public void Fixture_C_Zero_Budget_Activity_Contributes_Zero_With_No_Division_By_Zero()
    {
        var fixture = Fixture("EVM-C");
        var inputs = fixture.GetProperty("inputs");
        var expected = fixture.GetProperty("expected");

        var budgetCost = Dec(inputs, "activityBudgetCost");
        var progressPercentage = Dec(inputs, "activityProgressPercentage");

        var contribution = EvmEngine.ComputeActivityEarnedValue(budgetCost, progressPercentage);

        // decimal has no NaN/Infinity representation at all (unlike double), and this formula is a
        // pure multiplication with no division by BudgetCost anywhere - "no division, no NaN" is
        // true by construction, not merely by this specific input.
        Assert.Equal(Dec(expected, "evContribution"), contribution);
    }

    [Fact]
    public void ComputeEarnedValue_Aggregates_Every_Activitys_Contribution_Including_A_Zero_Budget_One()
    {
        var activities = new[]
        {
            new EvmActivityProgressInput(Guid.NewGuid(), 200_000m, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-02-01T00:00:00Z"), 50m),
            new EvmActivityProgressInput(Guid.NewGuid(), 0m, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-02-01T00:00:00Z"), 50m),
            new EvmActivityProgressInput(Guid.NewGuid(), 100_000m, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-02-01T00:00:00Z"), 0m),
        };

        var ev = EvmEngine.ComputeEarnedValue(activities);

        Assert.Equal(100_000m, ev); // 200,000*0.50 + 0*0.50 + 100,000*0.00
    }

    [Fact]
    public void ComputeEarnedValue_Of_An_Empty_Project_Is_Zero_Not_A_Crash()
    {
        Assert.Equal(0m, EvmEngine.ComputeEarnedValue([]));
        Assert.Equal(0m, EvmEngine.ComputePlannedValue([], DateTimeOffset.UtcNow));
    }

    // ------------------------------------------------------------------------------------------
    // Planned-value straight-line interpolation: backend-developer-constructed sanity checks (see
    // EvmEngine.ComputePlannedPercentage's own remarks) - evm-formulas.md never specifies how
    // plannedPct_i(t) is derived from a schedule, so there is no domain-provided fixture for this
    // specific sub-formula; flagged for domain-expert/system-architect confirmation in the Sprint 7
    // backend-developer report, same as CPM's own backend-constructed edge fixtures were flagged.
    // ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("2025-12-31T00:00:00Z", 0)] // before start
    [InlineData("2026-01-01T00:00:00Z", 0)] // exactly at start
    [InlineData("2026-01-06T00:00:00Z", 50)] // halfway through a 10-day span
    [InlineData("2026-01-11T00:00:00Z", 100)] // exactly at finish
    [InlineData("2026-02-01T00:00:00Z", 100)] // after finish
    public void ComputePlannedPercentage_Interpolates_Straight_Line_Between_Planned_Start_And_Finish(
        string asOfIso, decimal expectedPercentage)
    {
        var plannedStart = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var plannedFinish = DateTimeOffset.Parse("2026-01-11T00:00:00Z");
        var asOf = DateTimeOffset.Parse(asOfIso);

        var percentage = EvmEngine.ComputePlannedPercentage(plannedStart, plannedFinish, asOf);

        Assert.Equal(expectedPercentage, percentage);
    }

    [Fact]
    public void ComputePlannedPercentage_Of_A_Zero_Duration_Milestone_Never_Divides_By_Zero()
    {
        var milestoneDate = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        // Same "asOf <= plannedStart -> 0" rule a normal activity uses at its own start instant
        // applies uniformly here too (asOf == plannedStart == plannedFinish still satisfies
        // "asOf <= plannedStart") - the milestone flips to 100% strictly after its own date, not
        // exactly on it. No special-casing needed, and no division is ever attempted at any of these
        // three instants.
        Assert.Equal(0m, EvmEngine.ComputePlannedPercentage(milestoneDate, milestoneDate, milestoneDate.AddDays(-1)));
        Assert.Equal(0m, EvmEngine.ComputePlannedPercentage(milestoneDate, milestoneDate, milestoneDate));
        Assert.Equal(100m, EvmEngine.ComputePlannedPercentage(milestoneDate, milestoneDate, milestoneDate.AddTicks(1)));
        Assert.Equal(100m, EvmEngine.ComputePlannedPercentage(milestoneDate, milestoneDate, milestoneDate.AddDays(1)));
    }

    [Fact]
    public void ComputePlannedValue_Sums_Every_Activitys_Planned_Contribution_At_A_Data_Date()
    {
        // A: fully planned-complete by the data date. B: halfway through its planned span.
        // C: not yet started per plan. D: zero budget (must not blow up or skew the sum).
        var dataDate = DateTimeOffset.Parse("2026-02-01T00:00:00Z");
        var activities = new[]
        {
            new EvmActivityProgressInput(Guid.NewGuid(), 100_000m, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 0m),
            new EvmActivityProgressInput(Guid.NewGuid(), 200_000m, DateTimeOffset.Parse("2026-01-22T00:00:00Z"), DateTimeOffset.Parse("2026-02-11T00:00:00Z"), 0m),
            new EvmActivityProgressInput(Guid.NewGuid(), 300_000m, DateTimeOffset.Parse("2026-03-01T00:00:00Z"), DateTimeOffset.Parse("2026-03-15T00:00:00Z"), 0m),
            new EvmActivityProgressInput(Guid.NewGuid(), 0m, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 0m),
        };

        var pv = EvmEngine.ComputePlannedValue(activities, dataDate);

        // A: 100,000 * 100% = 100,000.00. B: 20 days elapsed / 20 days total * 200,000 = 100,000.00.
        // C: 0 (not started per plan). D: 0 (zero budget). Total = 200,000.00.
        Assert.Equal(200_000.00m, pv);
    }
}
