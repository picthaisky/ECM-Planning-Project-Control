using CMPlus.Domain.Enums;

namespace CMPlus.Application.Services.Evm;

/// <summary>
/// S7-BE-02: pure Application-layer EAC engine implementing all five <see cref="EacVariant"/>s
/// (ADR-0007) from the single unified Performance-Factor form -
/// `ETC = PF x (BAC - EV); EAC = AC + ETC; VAC = BAC - EAC` - one code path, not five formula
/// branches (evm-formulas.md's "implement one function, not five"). No dependency on EF Core/MediatR/
/// FluentValidation; consumes only an already-computed <see cref="EvmCoreResult"/> plus the two
/// optional project-level inputs (`Project.EacCustomPerformanceFactor`/`EacManualEtc`), so it is
/// unit-testable with nothing but plain decimals, per ADR-0001.
///
/// <para><b>Evaluation order (deterministic, matches evm-formulas.md's own precedence, made explicit
/// because two of its rows can both technically apply to the same input - Fixture I):</b></para>
/// <list type="number">
/// <item>`EV = AC = PV = 0` (not started) -&gt; every variant <see langword="null"/>,
/// <see cref="EacNullReason.NotStarted"/>. Checked <b>before</b> the short-circuit below because
/// Fixture I (BAC = PV = EV = AC = 0) satisfies both this rule and rule 2 simultaneously, and
/// "not started" must never render as a computed `0.00`.</item>
/// <item>`BAC - EV = 0` (work complete, or zero-budget scope) -&gt; short-circuit before ever
/// evaluating PF/CPI/SPI: every variant gets `ETC = 0.00`, `EAC = AC`, `VAC = BAC - AC`, unconditionally
/// - including <see cref="EacVariant.BottomUpEtc"/>/<see cref="EacVariant.CustomPf"/> even when their
/// own input has not been supplied (Fixture H: there is no remaining scope for a re-estimate or a
/// custom factor to even apply to).</item>
/// <item>Otherwise, each variant applies its own independent rule (below).</item>
/// </list>
///
/// <para><b>Per-variant rules beyond steps 1-2:</b></para>
/// <list type="bullet">
/// <item><see cref="EacVariant.CpiBased"/>: needs `AC != 0` (else <see cref="EacNullReason.NoActualCost"/>),
/// then `EV != 0` i.e. `CPI != 0` (else <see cref="EacNullReason.ZeroCpi"/> - `CPI` is a defined,
/// computed zero here, not undefined, so this is a genuine division-by-a-defined-zero, distinct from
/// `NoActualCost`'s division-by-undefined).</item>
/// <item><see cref="EacVariant.CpiSpiBased"/>: same `AC`/`EV` checks as <see cref="EacVariant.CpiBased"/>
/// (checked first, sharing its reasons) plus `PV != 0` (else <see cref="EacNullReason.NoPlannedValue"/>).
/// `EV = 0` is checked once and covers both `CPI = 0` and `SPI = 0` together (both share the same `EV`
/// numerator once `AC`/`PV` are individually known non-zero), so there is no separate "SPI is zero but
/// CPI is not" branch to reason about.</item>
/// <item><see cref="EacVariant.Atypical"/>: <see cref="EacNullReason.NoActualCost"/> only for the
/// literal `AC = 0 AND EV &gt; 0` rule (evm-formulas.md's edge-case table, row 3) - deliberately
/// <i>not</i> triggered merely by `AC = 0` alone, e.g. `AC = 0, EV = 0, PV &gt; 0` ("behind schedule,
/// nothing done or spent yet") is not covered by that literal condition and remains computable
/// (`= BAC`, the plan-rate assumption for work that has not started). Otherwise always computable -
/// no `CPI`/`SPI` dependency at all.</item>
/// <item><see cref="EacVariant.BottomUpEtc"/>: computable iff `manualEtc` is supplied (else
/// <see cref="EacNullReason.ManualEtcNotSet"/>) - immune to every `AC`/`PV`/`EV` pathology, exactly
/// like <see cref="EacVariant.CustomPf"/> below, per design.md §2.1's reason enum (there is no reason
/// code for "manual/custom variant suppressed by a cost/schedule edge case").</item>
/// <item><see cref="EacVariant.CustomPf"/>: computable iff `customPf` is supplied (else
/// <see cref="EacNullReason.CustomPfNotSet"/>). `Project.SetEacCustomPerformanceFactor` already
/// rejects `PF_c &lt;= 0` at the point it is set (`DomainException`, mirrored by a DB CHECK
/// constraint) - this calculator trusts a supplied value is `&gt; 0`, the same way it trusts `BAC &gt;= 0`
/// without re-validating `MoneyGuard`'s own invariant.</item>
/// </list>
/// </summary>
public static class EacCalculator
{
    public static EacCalculationResult ComputeAll(EvmCoreResult core, decimal? customPf, decimal? manualEtc, bool manualEtcIsStale = false)
    {
        var bac = core.Bac;
        var ev = core.Ev;
        var ac = core.Ac;
        var pv = core.Pv;
        var bacMinusEv = bac - ev;

        var warnings = new List<string>();
        if (bacMinusEv < 0m)
        {
            warnings.Add(EvmWarningCodes.EarnedValueExceedsBudget);
        }

        // actual-cost.md §7.6: AC(t) < 0 is a legitimate, computed value (an over-reversal/credit
        // note) that is never clamped - but every CPI-driven figure below is "meaningless" in the
        // domain sense, so it is flagged the same way EarnedValueExceedsBudget already flags its
        // own "computed as-is, but treat with caution" case. See EvmWarningCodes.ActualCostIsNegative
        // for why this is a warning rather than a new EacNullReason.
        if (ac < 0m)
        {
            warnings.Add(EvmWarningCodes.ActualCostIsNegative);
        }

        // domain-rules.md §5.7's ⚠ rule: a VO has moved BAC since EacManualEtc was last entered
        // (Project.EacManualEtcStaleSince is set). Guarded on manualEtc != null too - defensive only,
        // since Project.SetEacManualEtc(null) always clears EacManualEtcStaleSince in the same call,
        // so the two are never expected to disagree - but this keeps the warning meaningless only
        // when BottomUpEtc could not be shown anyway, never emitted "dangling".
        if (manualEtcIsStale && manualEtc is not null)
        {
            warnings.Add(EvmWarningCodes.ManualEtcPredatesBacChange);
        }

        IReadOnlyList<EacVariantResult> variants;

        if (pv == 0m && ev == 0m && ac == 0m)
        {
            // Rule 1: not started - highest precedence (Fixture B/I).
            variants = Enum.GetValues<EacVariant>()
                .Select(v => NotComputable(v, EacNullReason.NotStarted))
                .ToList();
        }
        else if (bacMinusEv == 0m)
        {
            // Rule 2: short-circuit - PF is never evaluated for any variant (Fixture H).
            var eac = ac;
            var vac = bac - eac;
            variants = Enum.GetValues<EacVariant>()
                .Select(v => Computed(v, performanceFactor: null, etc: 0m, eac: eac, vac: vac))
                .ToList();
        }
        else
        {
            var cpi = core.Cpi; // null iff ac == 0
            var spi = core.Spi; // null iff pv == 0

            variants =
            [
                ComputeCpiBased(bac, ev, ac, cpi, bacMinusEv),
                ComputeAtypical(bac, ev, ac),
                ComputeCpiSpiBased(bac, ev, ac, pv, cpi, spi, bacMinusEv),
                ComputeBottomUpEtc(bac, ac, manualEtc),
                ComputeCustomPf(bac, ev, ac, customPf, bacMinusEv),
            ];
        }

        var resultList = variants as List<EacVariantResult> ?? variants.ToList();
        var cpiBasedResult = resultList.Single(v => v.Variant == EacVariant.CpiBased);
        decimal? tcpiEac = bacMinusEv != 0m && cpiBasedResult.Computable
            ? bacMinusEv / cpiBasedResult.Etc!.Value
            : null;

        return new EacCalculationResult(resultList, tcpiEac, warnings);
    }

    private static EacVariantResult ComputeCpiBased(decimal bac, decimal ev, decimal ac, decimal? cpi, decimal bacMinusEv)
    {
        if (ac == 0m)
        {
            return NotComputable(EacVariant.CpiBased, EacNullReason.NoActualCost);
        }

        if (ev == 0m) // cpi == 0 exactly - a defined, computed zero, not undefined.
        {
            return NotComputable(EacVariant.CpiBased, EacNullReason.ZeroCpi);
        }

        var pf = 1m / cpi!.Value;
        var etc = pf * bacMinusEv;
        var eac = ac + etc;
        return Computed(EacVariant.CpiBased, pf, etc, eac, bac - eac);
    }

    private static EacVariantResult ComputeCpiSpiBased(
        decimal bac, decimal ev, decimal ac, decimal pv, decimal? cpi, decimal? spi, decimal bacMinusEv)
    {
        if (ac == 0m)
        {
            return NotComputable(EacVariant.CpiSpiBased, EacNullReason.NoActualCost);
        }

        if (pv == 0m)
        {
            return NotComputable(EacVariant.CpiSpiBased, EacNullReason.NoPlannedValue);
        }

        if (ev == 0m) // cpi == 0 and spi == 0 together (both share the EV numerator here).
        {
            return NotComputable(EacVariant.CpiSpiBased, EacNullReason.ZeroCpi);
        }

        var pf = 1m / (cpi!.Value * spi!.Value);
        var etc = pf * bacMinusEv;
        var eac = ac + etc;
        return Computed(EacVariant.CpiSpiBased, pf, etc, eac, bac - eac);
    }

    private static EacVariantResult ComputeAtypical(decimal bac, decimal ev, decimal ac)
    {
        if (ac == 0m && ev > 0m)
        {
            return NotComputable(EacVariant.Atypical, EacNullReason.NoActualCost);
        }

        const decimal pf = 1m;
        var etc = bac - ev;
        var eac = ac + etc;
        return Computed(EacVariant.Atypical, pf, etc, eac, bac - eac);
    }

    private static EacVariantResult ComputeBottomUpEtc(decimal bac, decimal ac, decimal? manualEtc)
    {
        if (manualEtc is null)
        {
            return NotComputable(EacVariant.BottomUpEtc, EacNullReason.ManualEtcNotSet);
        }

        var etc = manualEtc.Value;
        var eac = ac + etc;

        // No PF concept for this variant (evm-formulas.md's unified-form table: PF column is "-").
        return Computed(EacVariant.BottomUpEtc, performanceFactor: null, etc, eac, bac - eac);
    }

    private static EacVariantResult ComputeCustomPf(decimal bac, decimal ev, decimal ac, decimal? customPf, decimal bacMinusEv)
    {
        if (customPf is null)
        {
            return NotComputable(EacVariant.CustomPf, EacNullReason.CustomPfNotSet);
        }

        var pf = customPf.Value;
        var etc = pf * bacMinusEv;
        var eac = ac + etc;
        return Computed(EacVariant.CustomPf, pf, etc, eac, bac - eac);
    }

    private static EacVariantResult Computed(
        EacVariant variant, decimal? performanceFactor, decimal etc, decimal eac, decimal vac) =>
        new(variant, Computable: true, performanceFactor, etc, eac, vac, Reason: null);

    private static EacVariantResult NotComputable(EacVariant variant, EacNullReason reason) =>
        new(variant, Computable: false, PerformanceFactor: null, Etc: null, Eac: null, Vac: null, reason);
}
