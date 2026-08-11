using CMPlus.Application.Services.Evm;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Evm.Queries.GetEvm;

/// <summary>One <see cref="EacVariant"/>'s wire shape (design.md §2.1's `variants[]` array entry) -
/// a 1:1 mapping of <see cref="EacVariantResult"/> with <see cref="RoundingRules"/> applied exactly
/// once, at this boundary.</summary>
public sealed record EacVariantResponseDto(
    EacVariant Variant,
    decimal? PerformanceFactor,
    decimal? Etc,
    decimal? Eac,
    decimal? Vac,
    bool Computable,
    EacNullReason? Reason)
{
    public static EacVariantResponseDto From(EacVariantResult result) => new(
        result.Variant,
        RoundingRules.ToRatio(result.PerformanceFactor),
        RoundingRules.ToMoney(result.Etc),
        RoundingRules.ToMoney(result.Eac),
        RoundingRules.ToMoney(result.Vac),
        result.Computable,
        result.Reason);
}

/// <summary>
/// The full `GET /api/v1/projects/{projectId}/evm` response (design.md §2.1) - the 12-metric table
/// (`bac,pv,ev,ac,sv,cv,spi,cpi,tcpiBac,tcpiEac` plus the selected variant's own `eac/etc/vac`,
/// S7-FE-01's "ตาราง metric 12 ค่า") and every computable EAC variant so the frontend's variant
/// switcher never round-trips (design.md §1.1).
///
/// <para>This is the single point in the whole request pipeline where
/// <see cref="RoundingRules"/> is applied - <see cref="EvmEngine"/>/<see cref="EacCalculator"/> both
/// return full decimal precision; every field here is rounded exactly once, half-away-from-zero,
/// on the way out.</para>
/// </summary>
public sealed record EvmResponseDto(
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
    decimal? TcpiBac,
    decimal? TcpiEac,
    EacVariant SelectedVariant,
    IReadOnlyList<EacVariantResponseDto> Variants,
    IReadOnlyList<string> Warnings)
{
    public static EvmResponseDto From(
        Guid projectId,
        DateTimeOffset dataDate,
        EvmCoreResult core,
        EacCalculationResult eacResult,
        EacVariant selectedVariant) => new(
        projectId,
        dataDate,
        RoundingRules.ToMoney(core.Bac),
        RoundingRules.ToMoney(core.Pv),
        RoundingRules.ToMoney(core.Ev),
        RoundingRules.ToMoney(core.Ac),
        RoundingRules.ToMoney(core.Sv),
        RoundingRules.ToMoney(core.Cv),
        RoundingRules.ToRatio(core.Spi),
        RoundingRules.ToRatio(core.Cpi),
        RoundingRules.ToRatio(core.TcpiBac),
        RoundingRules.ToRatio(eacResult.TcpiEac),
        selectedVariant,
        eacResult.Variants.Select(EacVariantResponseDto.From).ToList(),
        eacResult.Warnings);
}
