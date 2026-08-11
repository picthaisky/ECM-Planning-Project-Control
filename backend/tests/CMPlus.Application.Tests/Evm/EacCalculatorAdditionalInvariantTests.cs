using CMPlus.Application.Services.Evm;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Evm;

/// <summary>
/// S7-QA-01 independent supplement to <see cref="EacCalculatorTests"/> (backend-developer's own
/// sanity-check suite, which already transcribes A/A1-A5/D1-D3/E-I verbatim from
/// <c>eac-variant-fixtures.json</c> and is read/verified, not duplicated, here). This file closes
/// two specific gaps found on independent review of that suite against the S7-QA-01 DoD:
///
/// <list type="number">
/// <item>The DoD explicitly asks for the <c>BAC-EV=0</c> short-circuit to be provable
/// <i>structurally</i> ("a division that never happens ... worth asserting structurally, since 'it
/// happened to not throw' is weaker than 'it provably short-circuits'"). The existing
/// <c>H_Nothing_Remaining_...</c> test is good evidence (it already proves <c>PerformanceFactor</c>
/// stays <see langword="null"/> and that a supplied <c>manualEtc</c> is ignored) but its own AC
/// (1,100,000.00) is non-zero, so it cannot distinguish the short-circuit from a hypothetical bug
/// that fell through to the normal per-variant path yet still produced the same numbers (multiplying
/// any finite PF by <c>BAC-EV=0</c> gives <c>ETC=0</c> either way). This file adds an AC=0 variant of
/// the same short-circuit condition specifically because <c>ac==0</c> is what the *normal* path uses
/// to short-circuit <see cref="EacVariant.CpiBased"/>/<see cref="EacVariant.CpiSpiBased"/>/
/// <see cref="EacVariant.Atypical"/> to <see cref="EacNullReason.NoActualCost"/> (see
/// <see cref="EacCalculator"/>'s own per-variant methods) - so if the top-level short-circuit branch
/// were ever skipped, this input would observably flip those three variants to
/// <c>Computable=false</c> instead of the correct <c>Computable=true</c>/<c>ETC=0.00</c>. Passing
/// this test is therefore evidence the short-circuit branch, not the per-variant branch, actually ran
/// - closer to "provably short-circuits" than an output-only coincidence check can be for a static,
/// non-mockable engine.</item>
/// <item>evm-formulas.md's two invariants (`Atypical` => VAC=CV; TCPI vs `CpiBased` EAC = CPI) are
/// unconditional algebraic identities, not properties that merely happen to hold for the two curated
/// worked examples (Fixture A/D). The existing suite only asserts them against those two clean
/// fixtures. This file re-asserts both against the deliberately pathological `EV &gt; BAC` input
/// <c>EacCalculatorTests.Ev_Greater_Than_Bac_...</c> already uses (same documented-provenance inputs,
/// no new fixture invented), which is a materially stronger proof that the identity is structural,
/// not coincidental to nice numbers.</item>
/// </list>
/// </summary>
public class EacCalculatorAdditionalInvariantTests
{
    [Fact]
    public void Short_Circuit_Fires_Even_When_Ac_Is_Zero_And_Never_Falls_Through_To_The_Per_Variant_NoActualCost_Path()
    {
        // BAC=EV=500,000.00 (short-circuit condition) with AC=0.00. PV is deliberately non-zero
        // (500,000.00) so this input does NOT also satisfy the separate, higher-precedence
        // "not started" rule (pv=ev=ac=0) - that would mask the exact defect this test targets behind
        // a different, also-plausible-looking null result.
        var core = EvmEngine.Compute(bac: 500_000.00m, pv: 500_000.00m, ev: 500_000.00m, ac: 0m);
        Assert.Equal(0m, core.Bac - core.Ev); // sanity: really is the BAC-EV=0 branch, not some other case.
        Assert.Null(core.Cpi); // AC=0 -> CPI undefined - exactly what would poison CpiBased/CpiSpiBased
                                // /Atypical with NoActualCost if the short-circuit were ever skipped.

        // customPf/manualEtc supplied (as Fixture H's own test also does) - the short-circuit must
        // ignore them too; if BottomUpEtc/CustomPf fell through to their own methods instead, ETC
        // would come back as manualEtc/customPf*(BAC-EV) rather than the short-circuit's fixed 0.00.
        var result = EacCalculator.ComputeAll(core, customPf: 1.20m, manualEtc: 760_000.00m);

        foreach (var variant in Enum.GetValues<EacVariant>())
        {
            var variantResult = result.GetVariant(variant);
            Assert.True(
                variantResult.Computable,
                $"{variant} must short-circuit to Computable=true even with AC=0 - a fall-through to " +
                "the per-variant path would incorrectly return NoActualCost/false here.");
            Assert.Null(variantResult.PerformanceFactor);
            Assert.Equal(0.00m, variantResult.Etc);
            Assert.Equal(0.00m, variantResult.Eac); // EAC = AC = 0.00 exactly, not a null/NoActualCost.
            Assert.Equal(500_000.00m, variantResult.Vac);
            Assert.Null(variantResult.Reason);
        }
    }

    [Fact]
    public void Atypical_Vac_Equals_Cv_Even_Under_The_Ev_Greater_Than_Bac_Pathological_Input()
    {
        // Same backend-developer-documented-provenance input as
        // EacCalculatorTests.Ev_Greater_Than_Bac_Computes_A_Negative_Etc_As_Is_And_Raises_A_Warning_Never_Throws
        // (BAC=1,000,000.00; PV=800,000.00; EV=1,200,000.00; AC=900,000.00) - reused deliberately
        // rather than inventing a new fixture, to independently stress the *invariant* on the one
        // input already known to be a genuine edge case, not just the two curated Fixture A/D
        // examples where every identity "happens" to look clean.
        var core = EvmEngine.Compute(bac: 1_000_000.00m, pv: 800_000.00m, ev: 1_200_000.00m, ac: 900_000.00m);
        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);

        var atypical = result.GetVariant(EacVariant.Atypical);
        Assert.True(atypical.Computable);
        Assert.Equal(core.Cv, atypical.Vac!.Value); // BAC-AC-BAC+EV = EV-AC = CV, unconditionally.
    }

    [Fact]
    public void Tcpi_Vs_CpiBased_Eac_Equals_Cpi_Even_Under_The_Ev_Greater_Than_Bac_Pathological_Input()
    {
        var core = EvmEngine.Compute(bac: 1_000_000.00m, pv: 800_000.00m, ev: 1_200_000.00m, ac: 900_000.00m);
        var result = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null);

        Assert.NotNull(core.Cpi);
        Assert.NotNull(result.TcpiEac);
        Assert.Equal(core.Cpi!.Value, result.TcpiEac!.Value);

        // The CpiBased variant itself must actually be Computable here (AC!=0, EV!=0) - otherwise
        // the invariant check above would be vacuously true against a null TcpiEac, which is exactly
        // the kind of "passes for the wrong reason" gap this file exists to close.
        Assert.True(result.GetVariant(EacVariant.CpiBased).Computable);
    }
}
