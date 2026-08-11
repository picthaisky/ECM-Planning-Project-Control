namespace CMPlus.Domain.Enums;

/// <summary>
/// Machine-readable reason a given <see cref="EacVariant"/> was not computable at a data date -
/// never a bare `null` with no explanation (S7-BE-02 DoD, evm-formulas.md's edge-case table, and
/// `docs/specs/master-plan/design.md` §2.1's response contract, which names all six values
/// verbatim). The frontend renders every reason as "—" (evm-formulas.md, Fixture B) but keeps the
/// code for diagnostics/data-quality messaging.
/// </summary>
public enum EacNullReason
{
    /// <summary>PV = EV = AC = 0 - nothing has happened yet (evm-formulas.md Fixture B/I). Takes
    /// precedence over every other rule, including the BAC-EV=0 short-circuit (Fixture I: BAC=0 too
    /// would otherwise also satisfy the short-circuit, but "not started" must never render as a
    /// computed 0.00).</summary>
    NotStarted = 1,

    /// <summary>AC = 0 (CPI undefined - division by zero) while the project has otherwise started.
    /// Suppresses <see cref="EacVariant.CpiBased"/>, <see cref="EacVariant.CpiSpiBased"/> and, by
    /// deliberate rule, <see cref="EacVariant.Atypical"/> (arithmetically computable but would
    /// exclude the cost of work already done - evm-formulas.md's edge-case table, row 3).</summary>
    NoActualCost = 2,

    /// <summary>PV = 0 while EV &gt; 0 (no baseline/unbaselined scope - SPI undefined). Suppresses
    /// only <see cref="EacVariant.CpiSpiBased"/>.</summary>
    NoPlannedValue = 3,

    /// <summary>AC &gt; 0 and EV = 0: CPI is a defined, computed zero (not undefined), so `PF = 1/CPI`
    /// divides by zero. Suppresses <see cref="EacVariant.CpiBased"/>/<see cref="EacVariant.CpiSpiBased"/>
    /// only - <see cref="EacVariant.Atypical"/> (= AC + BAC) remains valid and is what the dashboard
    /// shows (evm-formulas.md's edge-case table, row 5).</summary>
    ZeroCpi = 4,

    /// <summary><see cref="EacVariant.BottomUpEtc"/> was requested/returned but
    /// `Project.EacManualEtc` has never been set (design.md §2.1's response contract). Its input UI
    /// lands in Sprint 14 (ADR-0007(d)) - expected to be the common case throughout Sprint 7.</summary>
    ManualEtcNotSet = 5,

    /// <summary><see cref="EacVariant.CustomPf"/> was requested/returned but
    /// `Project.EacCustomPerformanceFactor` has never been set (design.md §2.1's response contract).
    /// Same Sprint-14 UI note as <see cref="ManualEtcNotSet"/>.</summary>
    CustomPfNotSet = 6,
}
