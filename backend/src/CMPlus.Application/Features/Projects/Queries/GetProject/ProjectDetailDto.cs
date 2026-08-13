using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Projects.Queries.GetProject;

/// <summary>
/// Response shape for <c>GET /api/v1/projects/{projectId}</c> (the single-project read the Project
/// Info screen's "view" half needs, US-4.3/4.4). Deliberately the full editable field set that
/// <c>UpdateProject</c>'s <c>ProjectDto</c> returns <b>plus</b> the ADR-0007(d) EAC configuration
/// (<see cref="EacVariantDefault"/>/<see cref="EacManualEtc"/>/<see cref="EacCustomPerformanceFactor"/>/
/// <see cref="EacManualEtcStaleSince"/>) that <c>ProjectDto</c> deliberately excludes - so this one
/// read populates the frontend's <c>ProjectDetail</c> (= <c>Project</c> &amp; <c>ProjectEacConfig</c>)
/// in a single round trip, closing the gap <c>features/info/api.ts#getProject</c> flagged.
/// </summary>
public sealed record ProjectDetailDto(
    Guid Id,
    string Name,
    string Code,
    string Owner,
    DateTimeOffset ContractStart,
    DateTimeOffset ContractFinish,
    decimal Bac,
    decimal ContractValue,
    decimal? RetentionRate,
    decimal? AdvanceRate,
    decimal? RetentionCapPercentage,
    decimal RetentionRelease1Percentage,
    int? DefectsLiabilityMonths,
    decimal? AdvanceAmountPaid,
    AdvanceRecoveryMethod AdvanceRecoveryMethod,
    decimal? AdvanceRecoveryStartPct,
    decimal? AdvanceRecoveryRatePct,
    decimal? AdvanceRecoveryEndPct,
    EacVariant EacVariantDefault,
    decimal? EacManualEtc,
    decimal? EacCustomPerformanceFactor,
    DateTimeOffset? EacManualEtcStaleSince);
