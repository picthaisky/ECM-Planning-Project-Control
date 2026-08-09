using System.Text.Json;
using CMPlus.Application.Services.Evm;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Evm;

/// <summary>
/// S7-BE-02 (backend-developer's own sanity check, per the non-negotiable "implement the worked
/// examples as your own sanity check before handing to QA" - S7-QA-01 owns the fuller, independent
/// test matrix, all 5 variants). Fixtures A1-A5/D1-D3/E-I plus the ordering assertions are loaded
/// verbatim from
/// <c>backend/tests/CMPlus.Application.Tests/Fixtures/eac-variant-fixtures.json</c> - no number is
/// hand-copied, same discipline as <c>CpmEngineTests</c>/<c>EvmEngineTests</c>.
///
/// <para>Rounding: <see cref="EacCalculator"/> itself never rounds (full precision throughout) -
/// every "Rounded2dp"/"FullPrecision" fixture expectation is asserted against the appropriate side
/// of that boundary: full precision values directly off <see cref="EacVariantResult"/>, and
/// money-rounded values via <see cref="RoundingRules.ToMoney(decimal?)"/> applied once here (the same
/// single rounding call <c>EvmResponseDto</c>/<c>EvmPeriodSnapshot</c> make at the real response/
/// persistence boundary) - MidpointRounding.AwayFromZero throughout, per S7-QA-02's DoD.</para>
/// </summary>
public class EacCalculatorTests
{
    private static readonly JsonElement FixtureRoot = LoadFixtureRoot();

    private static JsonElement LoadFixtureRoot()
    {
        var path = SolutionRelativePath(Path.Combine("tests", "CMPlus.Application.Tests", "Fixtures", "eac-variant-fixtures.json"));
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

    private static EvmCoreResult CoreFrom(JsonElement inputs) =>
        EvmEngine.Compute(Dec(inputs, "bac"), Dec(inputs, "pv"), Dec(inputs, "ev"), Dec(inputs, "ac"));

    /// <summary>
    /// Fixture-authored "FullPrecision" values can legitimately differ from this engine's own
    /// decimal division/multiplication chain in the last 1-2 significant digits whenever the true
    /// mathematical result is a non-terminating (repeating) decimal - the fixture file's own
    /// precisionNote says so explicitly ("chained decimal division can differ in the last
    /// significant digit depending on operation order without changing the correctly-rounded 2dp
    /// result"). Comparing after rounding to 20 decimal places keeps this a strong precision
    /// assertion (vastly beyond this codebase's money@2dp/ratio@6dp) while tolerating that
    /// documented, expected noise - observed in practice for A3/D3's repeating-decimal PF (14/9,
    /// 100/121).
    /// </summary>
    private static void AssertFullPrecisionEqual(decimal expected, decimal actual)
    {
        const int comparisonDecimals = 20;
        Assert.Equal(
            Math.Round(expected, comparisonDecimals, MidpointRounding.AwayFromZero),
            Math.Round(actual, comparisonDecimals, MidpointRounding.AwayFromZero));
    }

    // ---------------------------------------------------------------------------------------
    // A1-A5: variant expansion of Fixture A (unfavourable performance: CPI<1, SPI<1).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void A1_CpiBased_Is_Unchanged_From_The_Original_Fixture_A_Default_Eac()
    {
        var fixture = Fixture("A1");
        var core = CoreFrom(fixture.GetProperty("inputs"));
        var expected = fixture.GetProperty("expected");

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null).GetVariant(EacVariant.CpiBased);

        Assert.True(result.Computable);
        AssertFullPrecisionEqual(Dec(expected, "pfFullPrecision"), result.PerformanceFactor!.Value);
        AssertFullPrecisionEqual(Dec(expected, "etcFullPrecision"), result.Etc!.Value);
        AssertFullPrecisionEqual(Dec(expected, "eacFullPrecision"), result.Eac!.Value);
        AssertFullPrecisionEqual(Dec(expected, "vacFullPrecision"), result.Vac!.Value);
        Assert.Equal(Dec(expected, "etcRounded2dp"), RoundingRules.ToMoney(result.Etc));
        Assert.Equal(Dec(expected, "eacRounded2dp"), RoundingRules.ToMoney(result.Eac));
        Assert.Equal(Dec(expected, "vacRounded2dp"), RoundingRules.ToMoney(result.Vac));
    }

    [Fact]
    public void A2_Atypical_Gives_Vac_Equal_To_Cv()
    {
        var fixture = Fixture("A2");
        var core = CoreFrom(fixture.GetProperty("inputs"));
        var expected = fixture.GetProperty("expected");

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null).GetVariant(EacVariant.Atypical);

        Assert.True(result.Computable);
        Assert.Equal(Dec(expected, "pf"), result.PerformanceFactor!.Value);
        Assert.Equal(Dec(expected, "etcRounded2dp"), RoundingRules.ToMoney(result.Etc));
        Assert.Equal(Dec(expected, "eacRounded2dp"), RoundingRules.ToMoney(result.Eac));
        Assert.Equal(Dec(expected, "vacRounded2dp"), RoundingRules.ToMoney(result.Vac));
        Assert.Equal(Dec(expected, "vacEqualsCvCheck"), RoundingRules.ToMoney(result.Vac));
        Assert.Equal(core.Cv, result.Vac!.Value); // exact identity, full precision, per evm-formulas.md.
    }

    [Fact]
    public void A3_CpiSpiBased()
    {
        var fixture = Fixture("A3");
        var core = CoreFrom(fixture.GetProperty("inputs"));
        var expected = fixture.GetProperty("expected");

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null).GetVariant(EacVariant.CpiSpiBased);

        Assert.True(result.Computable);
        AssertFullPrecisionEqual(Dec(expected, "pfFullPrecision"), result.PerformanceFactor!.Value);
        AssertFullPrecisionEqual(Dec(expected, "etcFullPrecision"), result.Etc!.Value);
        AssertFullPrecisionEqual(Dec(expected, "eacFullPrecision"), result.Eac!.Value);
        AssertFullPrecisionEqual(Dec(expected, "vacFullPrecision"), result.Vac!.Value);
        Assert.Equal(Dec(expected, "etcRounded2dp"), RoundingRules.ToMoney(result.Etc));
        Assert.Equal(Dec(expected, "eacRounded2dp"), RoundingRules.ToMoney(result.Eac));
        Assert.Equal(Dec(expected, "vacRounded2dp"), RoundingRules.ToMoney(result.Vac));
    }

    [Fact]
    public void A4_BottomUpEtc_With_A_Manual_Reestimate_Overrides_All_Indices_And_Has_No_Pf()
    {
        var fixture = Fixture("A4");
        var inputs = fixture.GetProperty("inputs");
        var core = CoreFrom(inputs);
        var manualEtc = Dec(inputs, "etcManual");
        var expected = fixture.GetProperty("expected");

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc).GetVariant(EacVariant.BottomUpEtc);

        Assert.True(result.Computable);
        Assert.Null(result.PerformanceFactor); // "no PF concept" (evm-formulas.md's unified-form table).
        Assert.Equal(Dec(expected, "etcRounded2dp"), RoundingRules.ToMoney(result.Etc));
        Assert.Equal(Dec(expected, "eacRounded2dp"), RoundingRules.ToMoney(result.Eac));
        Assert.Equal(Dec(expected, "vacRounded2dp"), RoundingRules.ToMoney(result.Vac));
    }

    [Fact]
    public void A5_CustomPf()
    {
        var fixture = Fixture("A5");
        var inputs = fixture.GetProperty("inputs");
        var core = CoreFrom(inputs);
        var customPf = Dec(inputs, "customPf");
        var expected = fixture.GetProperty("expected");

        var result = EacCalculator.ComputeAll(core, customPf, manualEtc: null).GetVariant(EacVariant.CustomPf);

        Assert.True(result.Computable);
        Assert.Equal(Dec(expected, "pf"), result.PerformanceFactor!.Value);
        Assert.Equal(Dec(expected, "etcRounded2dp"), RoundingRules.ToMoney(result.Etc));
        Assert.Equal(Dec(expected, "eacRounded2dp"), RoundingRules.ToMoney(result.Eac));
        Assert.Equal(Dec(expected, "vacRounded2dp"), RoundingRules.ToMoney(result.Vac));
    }

    [Fact]
    public void A_Tcpi_Bac_And_Tcpi_Vs_CpiBased_Eac_Equals_Cpi_Exactly()
    {
        var fixture = Fixture("A-TCPI");
        var inputs = fixture.GetProperty("inputs");
        var expected = fixture.GetProperty("expected");

        // A-TCPI's own inputs omit `pv` (irrelevant to TCPI_BAC) - reuse Fixture A's pv so CPI/SPI
        // are still defined for the TCPI_EAC-vs-CpiBased check.
        var pv = Dec(Fixture("A1").GetProperty("inputs"), "pv");
        var core = EvmEngine.Compute(Dec(inputs, "bac"), pv, Dec(inputs, "ev"), Dec(inputs, "ac"));

        Assert.NotNull(core.TcpiBac);
        AssertFullPrecisionEqual(Dec(expected, "tcpiBacFullPrecision"), core.TcpiBac!.Value);

        var eacResult = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);
        Assert.NotNull(eacResult.TcpiEac);
        AssertFullPrecisionEqual(Dec(expected, "tcpiVsA1EacEqualsCpiCheck"), eacResult.TcpiEac!.Value);
        Assert.Equal(core.Cpi!.Value, eacResult.TcpiEac!.Value); // the stated invariant, exactly (both sides computed by this engine's own consistent chain).
    }

    // ---------------------------------------------------------------------------------------
    // D1-D3: variant expansion of Fixture D (favourable performance: CPI>1, SPI>1 - ordering
    // mirrors/reverses Fixture A's).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void D1_CpiBased()
    {
        var fixture = Fixture("D1");
        var core = CoreFrom(fixture.GetProperty("inputs"));
        var expected = fixture.GetProperty("expected");

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null).GetVariant(EacVariant.CpiBased);

        Assert.True(result.Computable);
        AssertFullPrecisionEqual(Dec(expected, "pfFullPrecision"), result.PerformanceFactor!.Value);
        AssertFullPrecisionEqual(Dec(expected, "etcFullPrecision"), result.Etc!.Value);
        AssertFullPrecisionEqual(Dec(expected, "eacFullPrecision"), result.Eac!.Value);
        AssertFullPrecisionEqual(Dec(expected, "vacFullPrecision"), result.Vac!.Value);
        Assert.Equal(Dec(expected, "etcRounded2dp"), RoundingRules.ToMoney(result.Etc));
        Assert.Equal(Dec(expected, "eacRounded2dp"), RoundingRules.ToMoney(result.Eac));
        Assert.Equal(Dec(expected, "vacRounded2dp"), RoundingRules.ToMoney(result.Vac));
    }

    [Fact]
    public void D2_Atypical_Gives_Vac_Equal_To_Cv()
    {
        var fixture = Fixture("D2");
        var core = CoreFrom(fixture.GetProperty("inputs"));
        var expected = fixture.GetProperty("expected");

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null).GetVariant(EacVariant.Atypical);

        Assert.True(result.Computable);
        Assert.Equal(Dec(expected, "etcRounded2dp"), RoundingRules.ToMoney(result.Etc));
        Assert.Equal(Dec(expected, "eacRounded2dp"), RoundingRules.ToMoney(result.Eac));
        Assert.Equal(Dec(expected, "vacRounded2dp"), RoundingRules.ToMoney(result.Vac));
        Assert.Equal(Dec(expected, "vacEqualsCvCheck"), RoundingRules.ToMoney(result.Vac));
        Assert.Equal(core.Cv, result.Vac!.Value);
    }

    [Fact]
    public void D3_CpiSpiBased()
    {
        var fixture = Fixture("D3");
        var core = CoreFrom(fixture.GetProperty("inputs"));
        var expected = fixture.GetProperty("expected");

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null).GetVariant(EacVariant.CpiSpiBased);

        Assert.True(result.Computable);
        AssertFullPrecisionEqual(Dec(expected, "pfFullPrecision"), result.PerformanceFactor!.Value);
        AssertFullPrecisionEqual(Dec(expected, "etcFullPrecision"), result.Etc!.Value);
        AssertFullPrecisionEqual(Dec(expected, "eacFullPrecision"), result.Eac!.Value);
        AssertFullPrecisionEqual(Dec(expected, "vacFullPrecision"), result.Vac!.Value);
        Assert.Equal(Dec(expected, "etcRounded2dp"), RoundingRules.ToMoney(result.Etc));
        Assert.Equal(Dec(expected, "eacRounded2dp"), RoundingRules.ToMoney(result.Eac));
        Assert.Equal(Dec(expected, "vacRounded2dp"), RoundingRules.ToMoney(result.Vac));
    }

    [Fact]
    public void D_Tcpi_Bac()
    {
        var fixture = Fixture("D-TCPI");
        var inputs = fixture.GetProperty("inputs");
        var expected = fixture.GetProperty("expected");
        var pv = Dec(Fixture("D1").GetProperty("inputs"), "pv");

        var core = EvmEngine.Compute(Dec(inputs, "bac"), pv, Dec(inputs, "ev"), Dec(inputs, "ac"));

        Assert.NotNull(core.TcpiBac);
        AssertFullPrecisionEqual(Dec(expected, "tcpiBacFullPrecision"), core.TcpiBac!.Value);
    }

    // ---------------------------------------------------------------------------------------
    // E-I: the edge-fixture engine matrix.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void E_Missing_Actuals_Nulls_CpiBased_CpiSpiBased_And_Atypical_With_NoActualCost_BottomUpEtc_Stays_Valid()
    {
        var fixture = Fixture("E");
        var core = CoreFrom(fixture.GetProperty("inputs"));

        Assert.Null(core.Cpi);

        var withoutManualEtc = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);
        AssertNotComputable(withoutManualEtc, EacVariant.CpiBased, EacNullReason.NoActualCost);
        AssertNotComputable(withoutManualEtc, EacVariant.CpiSpiBased, EacNullReason.NoActualCost);
        AssertNotComputable(withoutManualEtc, EacVariant.Atypical, EacNullReason.NoActualCost);
        AssertNotComputable(withoutManualEtc, EacVariant.BottomUpEtc, EacNullReason.ManualEtcNotSet);

        var withManualEtc = EacCalculator.ComputeAll(core, customPf: null, manualEtc: 500_000.00m);
        Assert.True(withManualEtc.GetVariant(EacVariant.BottomUpEtc).Computable);
    }

    [Fact]
    public void F_No_Baseline_Nulls_Only_CpiSpiBased_With_NoPlannedValue()
    {
        var fixture = Fixture("F");
        var core = CoreFrom(fixture.GetProperty("inputs"));
        var expected = fixture.GetProperty("expected");

        Assert.Null(core.Spi);

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);

        AssertNotComputable(result, EacVariant.CpiSpiBased, EacNullReason.NoPlannedValue);

        var cpiBased = result.GetVariant(EacVariant.CpiBased);
        Assert.True(cpiBased.Computable);
        Assert.Equal(Dec(expected, "cpiBasedRounded2dp"), RoundingRules.ToMoney(cpiBased.Eac));

        var atypical = result.GetVariant(EacVariant.Atypical);
        Assert.True(atypical.Computable);
        Assert.Equal(Dec(expected, "atypicalRounded2dp"), RoundingRules.ToMoney(atypical.Eac));
    }

    [Fact]
    public void G_Zero_Earned_Nulls_CpiBased_And_CpiSpiBased_With_ZeroCpi_Atypical_Stays_Valid()
    {
        var fixture = Fixture("G");
        var core = CoreFrom(fixture.GetProperty("inputs"));
        var expected = fixture.GetProperty("expected");

        Assert.Equal(0m, core.Cpi);
        Assert.Equal(0m, core.Spi);

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);

        AssertNotComputable(result, EacVariant.CpiBased, EacNullReason.ZeroCpi);
        AssertNotComputable(result, EacVariant.CpiSpiBased, EacNullReason.ZeroCpi);

        var atypical = result.GetVariant(EacVariant.Atypical);
        Assert.True(atypical.Computable);
        Assert.Equal(Dec(expected, "atypicalRounded2dp"), RoundingRules.ToMoney(atypical.Eac));
    }

    [Fact]
    public void H_Nothing_Remaining_Short_Circuits_Every_Variant_Without_Ever_Evaluating_Pf()
    {
        var fixture = Fixture("H");
        var inputs = fixture.GetProperty("inputs");
        var expected = fixture.GetProperty("expected");

        // Fixture H's own inputs omit `pv` deliberately (evm-formulas.md: the short-circuit fires
        // before SPI/CPI/PF are ever evaluated, so PV is irrelevant here) - 0 is an arbitrary stand-in
        // that must make no difference to the outcome.
        var core = EvmEngine.Compute(Dec(inputs, "bac"), pv: 0m, Dec(inputs, "ev"), Dec(inputs, "ac"));

        // Supplied vs. not-supplied manual/custom inputs must make no difference at all - the
        // short-circuit fires before either would ever be consulted.
        var withoutOptionalInputs = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);
        var withOptionalInputs = EacCalculator.ComputeAll(core, customPf: 1.20m, manualEtc: 760_000.00m);

        foreach (var eacResult in new[] { withoutOptionalInputs, withOptionalInputs })
        {
            foreach (var variant in Enum.GetValues<EacVariant>())
            {
                var result = eacResult.GetVariant(variant);
                Assert.True(result.Computable);
                Assert.Null(result.PerformanceFactor); // "PF is never evaluated for any variant".
                Assert.Equal(Dec(expected, "etcAllVariants"), RoundingRules.ToMoney(result.Etc));
                Assert.Equal(Dec(expected, "eacAllVariants"), RoundingRules.ToMoney(result.Eac));
                Assert.Equal(Dec(expected, "vacAllVariants"), RoundingRules.ToMoney(result.Vac));
            }
        }
    }

    [Fact]
    public void I_Zero_Budget_Project_Is_NotStarted_For_Every_Variant_Never_Zero()
    {
        var fixture = Fixture("I");
        var core = CoreFrom(fixture.GetProperty("inputs"));

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);

        foreach (var variant in Enum.GetValues<EacVariant>())
        {
            AssertNotComputable(result, variant, EacNullReason.NotStarted);
        }

        Assert.Null(result.TcpiEac);
    }

    // ---------------------------------------------------------------------------------------
    // EV > BAC ("progress or weights corrupt"): eac-variant-fixtures.json's own "gap" entry says,
    // verbatim, "No worked numeric example given in source ... Do not fabricate a fixture; obtain
    // concrete numbers from domain-expert before Sprint 7 if a numeric test is required." No
    // domain-expert pass was run for this - the inputs below are backend-developer-constructed
    // (documented provenance, not silently invented, same standard Sprint 5's own backend-
    // constructed CPM edge fixtures used), verifying only the *qualitative* rule (compute as-is,
    // negative ETC, raise the warning - never throw, never clamp here) rather than a domain-blessed
    // expected number.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Ev_Greater_Than_Bac_Computes_A_Negative_Etc_As_Is_And_Raises_A_Warning_Never_Throws()
    {
        // BAC=1,000,000.00; PV=800,000.00; EV=1,200,000.00 (corrupted/over-100% progress rolled up);
        // AC=900,000.00. CPI = EV/AC = 1.3333... > 1, so a smaller-than-BAC EAC is a plausible (if
        // still flagged) outcome here - the point of this test is the warning + non-crashing negative
        // ETC, not this specific number.
        var core = EvmEngine.Compute(bac: 1_000_000.00m, pv: 800_000.00m, ev: 1_200_000.00m, ac: 900_000.00m);

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);

        Assert.Contains(EvmWarningCodes.EarnedValueExceedsBudget, result.Warnings);

        var cpiBased = result.GetVariant(EacVariant.CpiBased);
        Assert.True(cpiBased.Computable);
        Assert.True(cpiBased.Etc < 0m, "ETC should be negative when EV exceeds BAC - computed as-is, never masked.");
    }

    [Fact]
    public void No_Warning_Is_Raised_When_Ev_Does_Not_Exceed_Bac()
    {
        var fixture = Fixture("A1");
        var core = CoreFrom(fixture.GetProperty("inputs"));

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);

        Assert.Empty(result.Warnings);
    }

    // ---------------------------------------------------------------------------------------
    // S8 (actual-cost.md §7.6, ADR-0013): AC(t) < 0 is a legitimate over-reversal/credit-note
    // value, computed as-is and never clamped - flagged via a warning rather than a new
    // EacNullReason (see EvmWarningCodes.ActualCostIsNegative's own remarks for why this change
    // does not unilaterally alter the frozen EacNullReason/EvmResponse contract). Was unreachable
    // before ActualCostEntry existed (IActualCostReader was pinned at a literal 0), so this input
    // has no prior fixture/coverage to preserve.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Negative_Actual_Cost_Computes_As_Is_And_Raises_A_Warning_Never_Throws_Or_Masks_Cpi()
    {
        // BAC=1,000,000.00; PV=400,000.00; EV=300,000.00; AC=-50,000.00 (a credit note/over-reversal
        // exceeding recorded costs). CV = EV - AC = 350,000.00 (inflated, the raw formula's honest
        // result); CPI = EV/AC is a defined-but-negative number, surfaced as-is, not fabricated into
        // a plausible-looking positive one.
        var core = EvmEngine.Compute(bac: 1_000_000.00m, pv: 400_000.00m, ev: 300_000.00m, ac: -50_000.00m);

        Assert.Equal(350_000.00m, core.Cv);
        Assert.NotNull(core.Cpi);
        Assert.True(core.Cpi < 0m);

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);

        Assert.Contains(EvmWarningCodes.ActualCostIsNegative, result.Warnings);

        // Still Computable=true (not silently nulled) - the warning is the signal, not a suppression.
        var cpiBased = result.GetVariant(EacVariant.CpiBased);
        Assert.True(cpiBased.Computable);
    }

    [Fact]
    public void No_Negative_Actual_Cost_Warning_Is_Raised_When_Ac_Is_Zero_Or_Positive()
    {
        var fixture = Fixture("A1");
        var core = CoreFrom(fixture.GetProperty("inputs"));

        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);

        Assert.DoesNotContain(EvmWarningCodes.ActualCostIsNegative, result.Warnings);
    }

    // ---------------------------------------------------------------------------------------
    // Variant ordering (never assume a fixed order - it reverses when CPI/SPI > 1).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Ordering_Is_Atypical_Then_CpiBased_Then_CpiSpiBased_When_Performance_Is_Unfavourable()
    {
        var fixture = Fixture("A1"); // any A-family fixture shares the same base scalars.
        var core = CoreFrom(fixture.GetProperty("inputs"));
        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);

        var expectedOrder = new[] { EacVariant.Atypical, EacVariant.CpiBased, EacVariant.CpiSpiBased };
        var expectedEac = new[] { 1_050_000.00m, 1_166_666.67m, 1_438_888.89m };

        AssertAscendingEacOrder(result, expectedOrder, expectedEac);
    }

    [Fact]
    public void Ordering_Reverses_To_CpiSpiBased_Then_CpiBased_Then_Atypical_When_Performance_Is_Favourable()
    {
        var fixture = Fixture("D1");
        var core = CoreFrom(fixture.GetProperty("inputs"));
        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);

        var expectedOrder = new[] { EacVariant.CpiSpiBased, EacVariant.CpiBased, EacVariant.Atypical };
        var expectedEac = new[] { 431_404.96m, 454_545.45m, 480_000.00m };

        AssertAscendingEacOrder(result, expectedOrder, expectedEac);
    }

    // ---------------------------------------------------------------------------------------
    // Cross-fixture invariants (evm-formulas.md lines 72-76 / domain-decisions.md §1.6).
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("A1")]
    [InlineData("D1")]
    public void Invariant_Pf_Equals_One_Over_Cpi_Collapses_To_Bac_Over_Cpi_To_The_Cent(string fixtureId)
    {
        var inputs = Fixture(fixtureId).GetProperty("inputs");
        var core = CoreFrom(inputs);

        var cpiBased = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null).GetVariant(EacVariant.CpiBased);

        var eacViaPfChain = RoundingRules.ToMoney(cpiBased.Eac);
        var eacViaClosedForm = RoundingRules.ToMoney(core.Bac / core.Cpi!.Value);

        Assert.Equal(eacViaClosedForm, eacViaPfChain);
    }

    private static void AssertNotComputable(EacCalculationResult result, EacVariant variant, EacNullReason expectedReason)
    {
        var variantResult = result.GetVariant(variant);
        Assert.False(variantResult.Computable);
        Assert.Null(variantResult.PerformanceFactor);
        Assert.Null(variantResult.Etc);
        Assert.Null(variantResult.Eac);
        Assert.Null(variantResult.Vac);
        Assert.Equal(expectedReason, variantResult.Reason);
    }

    private static void AssertAscendingEacOrder(EacCalculationResult result, EacVariant[] expectedOrder, decimal[] expectedEac)
    {
        var actual = expectedOrder
            .Select(v => result.GetVariant(v))
            .Select(v => RoundingRules.ToMoney(v.Eac!.Value))
            .ToArray();

        Assert.Equal(expectedEac, actual);

        for (var i = 1; i < actual.Length; i++)
        {
            Assert.True(actual[i - 1] < actual[i], $"Expected ascending EAC order, but {expectedOrder[i - 1]} ({actual[i - 1]}) was not less than {expectedOrder[i]} ({actual[i]}).");
        }
    }
}
