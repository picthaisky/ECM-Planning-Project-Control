namespace CMPlus.Domain.Common;

/// <summary>
/// S7-BE-01/02 (EVM/EAC engines): the single, explicit rounding boundary for every money/ratio
/// value this codebase computes through a chain of decimal arithmetic (EVM core metrics, EAC
/// variants, <c>EvmPeriodSnapshot</c>). Per `.claude/knowledge/domain/evm-formulas.md` line 104-108
/// and `docs/specs/master-plan/domain-decisions.md` §2.1: keep full <see cref="decimal"/> precision
/// through every intermediate step and round <b>once</b>, at the point a value crosses into a
/// fixed-precision representation (an API response field or a `decimal(p,s)` column) - never
/// mid-calculation, and never via the unqualified <see cref="Math.Round(decimal, int)"/> overload,
/// which defaults to <see cref="MidpointRounding.ToEven"/> ("banker's rounding") and silently
/// diverges from SQL Server's <c>ROUND()</c> and from a printed Thai certificate by 0.01 on exact
/// half-cent inputs (domain-decisions.md §2.1's worked example: 1,000,000.10 at a 5% rate rounds to
/// 50,000.01 away-from-zero vs 50,000.00 banker's).
///
/// <see cref="CMPlus.Application.Services.Evm.EvmEngine"/> and
/// <see cref="CMPlus.Application.Services.Evm.EacCalculator"/> never call this type themselves -
/// they return full-precision <see cref="decimal"/> values so intermediate identities (e.g. `PF =
/// 1/CPI` collapsing to `BAC/CPI` "to the cent") can be asserted at full precision in tests. Callers
/// at the actual boundary (the query handler's response DTO mapping, and
/// <see cref="CMPlus.Domain.Entities.EvmPeriodSnapshot"/>'s own constructor) apply these helpers
/// exactly once.
/// </summary>
public static class RoundingRules
{
    /// <summary>Money precision across this codebase: `decimal(18,2)`.</summary>
    public const int MoneyDecimals = 2;

    /// <summary>
    /// Percent precision across this codebase: `decimal(5,2)` (CLAUDE.md conventions). Numerically
    /// the same digit count as <see cref="MoneyDecimals"/>, but kept as its own named constant/
    /// helper pair (S8-BE-02, <c>WbsProgressRollupCalculator</c>) rather than reusing
    /// <see cref="ToMoney(decimal)"/> on a percentage value - the two are unrelated quantities that
    /// only coincidentally round to the same number of digits, and a future change to either
    /// precision must not silently drag the other along.
    /// </summary>
    public const int PercentDecimals = 2;

    /// <summary>
    /// Ratio precision (SPI/CPI/TCPI/Performance Factor) used for the live EVM API response and for
    /// <c>EvmPeriodSnapshot.PerformanceFactor</c> (`decimal(9,6)` per design.md §3, "to keep the
    /// audit reproducible") - distinct from `Project.EacCustomPerformanceFactor`'s own persisted
    /// `decimal(9,4)`, which is a user-entered input, not a derived/displayed value. design.md §2.1's
    /// own JSON sample is inconsistent about ratio digit counts (spi at 2dp, cpi/performanceFactor at
    /// 6dp, tcpi at 4dp) with no single stated rule; 6dp was chosen as the one precision that matches
    /// every *ratio-shaped* sample value in that document exactly (cpi "0.857143", performanceFactor
    /// "1.166667") and is applied uniformly to SPI/CPI/TCPI/PF so the API never has a per-field
    /// special case - see the Sprint 7 backend-developer report for this resolved ambiguity.
    /// </summary>
    public const int RatioDecimals = 6;

    public static decimal ToMoney(decimal value) => Math.Round(value, MoneyDecimals, MidpointRounding.AwayFromZero);

    public static decimal? ToMoney(decimal? value) => value is null ? null : ToMoney(value.Value);

    public static decimal ToRatio(decimal value) => Math.Round(value, RatioDecimals, MidpointRounding.AwayFromZero);

    public static decimal? ToRatio(decimal? value) => value is null ? null : ToRatio(value.Value);

    public static decimal ToPercentage(decimal value) => Math.Round(value, PercentDecimals, MidpointRounding.AwayFromZero);

    public static decimal? ToPercentage(decimal? value) => value is null ? null : ToPercentage(value.Value);
}
