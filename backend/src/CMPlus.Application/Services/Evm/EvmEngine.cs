using CMPlus.Domain.Common;

namespace CMPlus.Application.Services.Evm;

/// <summary>
/// S7-BE-01: pure Application-layer EVM engine - PV/EV/AC/SV/CV/SPI/CPI/TCPI_BAC at any data date,
/// per `.claude/knowledge/domain/evm-formulas.md`'s core metrics table. No dependency on EF Core (or
/// any other package) anywhere in this class - unit-testable with nothing but plain CLR
/// decimals/records, per ADR-0001 ("CPM/EVM engines are pure Application services") and the same
/// discipline <c>CMPlus.Application.Services.Cpm.CpmEngine</c> already established.
///
/// <para><b>Historical EV/PV (ADR-0009):</b> this engine never reads <c>ActivityProgressLog</c> or
/// re-derives the step-function reconstruction rule itself - <see cref="EvmActivityProgressInput.ProgressPercentageAsOf"/>
/// is expected to already be the caller's ADR-0009 step-function value (Sprint 1's
/// <c>IActivityProgressReader</c>/<c>GetProgressAsOf</c> reader, reused project-wide by
/// <c>IEvmDataReader</c> in Infrastructure - see that interface's remarks for why a second
/// reconstruction path was deliberately not written). This keeps the one non-negotiable rule
/// ("EV ย้อนหลังอ่านผ่าน step function ของ ADR-0009") satisfied by construction: there is nowhere else
/// in this engine progress could come from.</para>
///
/// <para><b>No rounding here.</b> Every value returned is full <see cref="decimal"/> precision -
/// <see cref="RoundingRules"/> is applied exactly once, by the caller, at the actual response/
/// persistence boundary (evm-formulas.md line 104-108; domain-decisions.md §2.1). Rounding inside
/// this engine would make chained identities (e.g. `EacCalculator`'s `PF=1/CPI` collapsing to
/// `BAC/CPI` "to the cent") untestable at full precision and would compound rounding error across
/// the SV/CV/SPI/CPI -&gt; EAC/ETC/VAC pipeline.</para>
/// </summary>
public static class EvmEngine
{
    /// <summary>
    /// One activity's Earned Value contribution: `BudgetCost x ProgressPct / 100`. Pure
    /// multiplication - never a division by <paramref name="budgetCost"/> anywhere in this formula,
    /// so a zero-budget activity contributes exactly <c>0.00</c> with no division-by-zero/NaN risk
    /// by construction (Fixture C, evm-formulas.md lines 45-46).
    /// </summary>
    public static decimal ComputeActivityEarnedValue(decimal budgetCost, decimal progressPercentage) =>
        budgetCost * progressPercentage / 100m;

    /// <summary>`EV(t) = sum_i BudgetCost_i x Pct_i(t)/100` (ADR-0009). An empty project (no
    /// activities yet) sums to exactly <c>0</c>, never throws.</summary>
    public static decimal ComputeEarnedValue(IEnumerable<EvmActivityProgressInput> activities) =>
        activities.Sum(a => ComputeActivityEarnedValue(a.BudgetCost, a.ProgressPercentageAsOf));

    /// <summary>
    /// One activity's planned-percent-complete at <paramref name="asOf"/>: straight-line (calendar-
    /// day linear) interpolation between <paramref name="plannedStart"/> and
    /// <paramref name="plannedFinish"/>, clamped to <c>[0,100]</c>.
    ///
    /// <para><b>Resolved ambiguity (flagged in the Sprint 7 backend-developer report):</b>
    /// evm-formulas.md defines `PV = sum_{i in scheduled(t)} BudgetCost_i . plannedPct_i(t)` but never
    /// specifies how `plannedPct_i(t)` itself is derived from a schedule - no `Baseline`/resource-
    /// loaded-curve entity exists before Sprint 14 (docs/10 §7, `BaselineActivitySnapshot` lands
    /// S14), so `Activity.PlannedStart`/`PlannedFinish` (the only schedule dates that exist as of
    /// Sprint 7) are the best available stand-in for "the plan". Straight-line duration-based
    /// interpolation is the standard fallback construction-scheduling tools use absent a resource-
    /// loaded S-curve, and is exactly what "an activity's planned bar overlaps `[from,to]`"
    /// (`IGanttActivityReader`'s own S6-BE-01 windowing rule) already assumes about how a bar
    /// occupies its planned span. This should be confirmed with domain-expert/system-architect before
    /// any golden-file PV comparison is signed off, the same way P6 EAC reconciliation already
    /// carries a `[ต้องยืนยัน]` per ADR-0007.</para>
    /// </summary>
    public static decimal ComputePlannedPercentage(DateTimeOffset plannedStart, DateTimeOffset plannedFinish, DateTimeOffset asOf)
    {
        if (asOf <= plannedStart)
        {
            return 0m;
        }

        if (asOf >= plannedFinish)
        {
            return 100m;
        }

        var totalTicks = (plannedFinish - plannedStart).Ticks;
        if (totalTicks <= 0)
        {
            // Defensive only - unreachable given the two guards above (asOf strictly between start
            // and finish requires finish > start), kept as a belt-and-braces division guard the same
            // way WBSNode/Project keep guards that are already unreachable via their own callers.
            return 100m;
        }

        var elapsedTicks = (asOf - plannedStart).Ticks;
        return PercentageGuard.Clamp((decimal)elapsedTicks / totalTicks * 100m);
    }

    /// <summary>`PV(t) = sum_i BudgetCost_i x plannedPct_i(t)/100` - the same "no division by
    /// BudgetCost" shape as <see cref="ComputeActivityEarnedValue"/>, so a zero-budget activity is
    /// equally safe here.</summary>
    public static decimal ComputePlannedValue(IEnumerable<EvmActivityProgressInput> activities, DateTimeOffset asOf) =>
        activities.Sum(a => a.BudgetCost * ComputePlannedPercentage(a.PlannedStart, a.PlannedFinish, asOf) / 100m);

    /// <summary>
    /// The core metrics at one data date, from already-aggregated project-wide scalars (Fixture
    /// A/B/C's own shape - <see cref="CMPlus.Domain.Common.MoneyGuard"/> is not re-applied here since
    /// these are already-computed sums of non-negative contributions, not raw user input).
    /// <see cref="EvmCoreResult.Spi"/> is <see langword="null"/> iff <paramref name="pv"/> = 0;
    /// <see cref="EvmCoreResult.Cpi"/> is <see langword="null"/> iff <paramref name="ac"/> = 0 -
    /// exactly evm-formulas.md's core metrics table, independent of any EAC variant's own,
    /// additional edge-case rules (those live in <see cref="EacCalculator"/>).
    /// </summary>
    public static EvmCoreResult Compute(decimal bac, decimal pv, decimal ev, decimal ac)
    {
        var sv = ev - pv;
        var cv = ev - ac;
        decimal? spi = pv == 0m ? null : ev / pv;
        decimal? cpi = ac == 0m ? null : ev / ac;

        var bacMinusAc = bac - ac;
        decimal? tcpiBac = bacMinusAc == 0m ? null : (bac - ev) / bacMinusAc;

        return new EvmCoreResult(bac, pv, ev, ac, sv, cv, spi, cpi, tcpiBac);
    }
}
