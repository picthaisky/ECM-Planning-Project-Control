namespace CMPlus.Domain.Enums;

/// <summary>
/// Estimate-at-Completion performance-factor variant, per ADR-0007. The EAC engine
/// (<c>EacCalculator</c> in Application) computes all five from a single unified
/// <c>ETC = PF * (BAC - EV)</c> form; the Sprint 7 UI selector surfaces only the first
/// three (index-based). <see cref="BottomUpEtc"/> and <see cref="CustomPf"/> are computed
/// and returned by the API from Sprint 7 but their input UI lands in Sprint 14.
/// </summary>
public enum EacVariant
{
    /// <summary>PF = 1 / CPI. The default variant (typical remaining-work assumption).</summary>
    CpiBased = 1,

    /// <summary>PF = 1 (atypical variance assumed non-recurring; ETC = BAC - EV).</summary>
    Atypical = 2,

    /// <summary>PF = 1 / (CPI * SPI) (both cost and schedule performance drive the remaining work).</summary>
    CpiSpiBased = 3,

    /// <summary>ETC supplied directly by the project team (<c>Project.EacManualEtc</c>); no PF derived.</summary>
    BottomUpEtc = 4,

    /// <summary>PF supplied directly by the project team (<c>Project.EacCustomPerformanceFactor</c>).</summary>
    CustomPf = 5,
}
