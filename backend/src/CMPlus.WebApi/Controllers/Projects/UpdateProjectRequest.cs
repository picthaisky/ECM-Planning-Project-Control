using CMPlus.Domain.Enums;

namespace CMPlus.WebApi.Controllers.Projects;

/// <summary>Request body for <c>PUT /api/v1/projects/{projectId}</c> (S4-BE-02) - everything except
/// <c>projectId</c> itself, which comes from the route.</summary>
public sealed record UpdateProjectRequest(
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
    decimal? AdvanceRecoveryEndPct);
