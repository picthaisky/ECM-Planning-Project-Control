using CMPlus.Application.Features.Evm;
using CMPlus.Application.Services.Evm;
using CMPlus.Application.Services.Wbs;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Dashboard.Queries.GetDashboard;

/// <summary>One WBS level whose children's <c>WeightPercentage</c> values did not sum to 100.00 -
/// wire shape of <see cref="WbsRollupWeightWarning"/>. Surfaced, never blocking (S8-BE-02 DoD:
/// "น้ำหนักไม่ครบ 100 → เตือน ไม่บล็อก").</summary>
public sealed record DashboardWeightWarningDto(Guid? WbsNodeId, int ChildCount, decimal WeightSum)
{
    public static DashboardWeightWarningDto From(WbsRollupWeightWarning warning) =>
        new(warning.WbsNodeId, warning.ChildCount, RoundingRules.ToPercentage(warning.WeightSum));
}

/// <summary>US-8.3's weight-based WBS progress rollup (evm-formulas.md's "Progress rollup (WBS
/// tree)" rule) - deliberately its own object, not folded into the EVM-sourced fields on
/// <see cref="DashboardResponseDto"/>, because it is not a function of `dataDate` the way those are
/// (see <c>GetDashboardQueryHandler</c>'s remarks).</summary>
public sealed record DashboardProgressRollupDto(
    decimal ProgressPercentage,
    IReadOnlyList<DashboardWeightWarningDto> WeightWarnings,
    IReadOnlyList<Guid> MixedScopeWbsNodeIds)
{
    public static DashboardProgressRollupDto From(WbsRollupResult rollup) => new(
        RoundingRules.ToPercentage(rollup.ProgressPercentage),
        rollup.WeightWarnings.Select(DashboardWeightWarningDto.From).ToList(),
        rollup.MixedScopeNodeIds);
}

/// <summary>
/// The full `GET /api/v1/projects/{projectId}/dashboard` response (S8-BE-02) - the KPI tiles. Every
/// EVM-sourced field here (everything except <see cref="ProgressRollup"/>) comes straight from
/// <c>EvmComputationService.ComputeAsync</c>'s already-computed result for
/// <c>Project.EacVariantDefault</c> specifically - see <c>GetDashboardQueryHandler</c>'s remarks.
/// Rounded exactly once, half-away-from-zero, at this boundary (<c>RoundingRules</c>) - same
/// discipline as <c>EvmResponseDto</c>.
/// </summary>
public sealed record DashboardResponseDto(
    Guid ProjectId,
    DateTimeOffset DataDate,
    decimal Bac,
    decimal Pv,
    decimal Ev,
    decimal Ac,
    decimal Sv,
    decimal Cv,
    decimal? Spi,
    decimal? Cpi,
    int ActualCostEntryCount,
    EacVariant EacVariant,
    decimal? PerformanceFactor,
    decimal? Etc,
    decimal? Eac,
    decimal? Vac,
    bool EacComputable,
    EacNullReason? EacNullReason,
    DashboardProgressRollupDto ProgressRollup,
    IReadOnlyList<string> Warnings)
{
    public static DashboardResponseDto From(
        Guid projectId,
        EvmComputation computation,
        EacVariantResult selectedVariantResult,
        WbsRollupResult rollup) => new(
        projectId,
        computation.DataDate,
        RoundingRules.ToMoney(computation.Core.Bac),
        RoundingRules.ToMoney(computation.Core.Pv),
        RoundingRules.ToMoney(computation.Core.Ev),
        RoundingRules.ToMoney(computation.Core.Ac),
        RoundingRules.ToMoney(computation.Core.Sv),
        RoundingRules.ToMoney(computation.Core.Cv),
        RoundingRules.ToRatio(computation.Core.Spi),
        RoundingRules.ToRatio(computation.Core.Cpi),
        computation.ActualCostEntryCount,
        selectedVariantResult.Variant,
        RoundingRules.ToRatio(selectedVariantResult.PerformanceFactor),
        RoundingRules.ToMoney(selectedVariantResult.Etc),
        RoundingRules.ToMoney(selectedVariantResult.Eac),
        RoundingRules.ToMoney(selectedVariantResult.Vac),
        selectedVariantResult.Computable,
        selectedVariantResult.Reason,
        DashboardProgressRollupDto.From(rollup),
        computation.EacResult.Warnings);
}
