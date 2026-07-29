using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Projects.Commands.UpdateProject;

/// <summary>
/// Response shape for a successful <see cref="UpdateProjectCommand"/> - the Project Info screen's
/// full editable field set (US-4.3/4.4), so the caller can re-render without a second round trip.
/// Deliberately excludes the EAC fields (<c>EacVariantDefault</c>/<c>EacCustomPerformanceFactor</c>/
/// <c>EacManualEtc</c>) - those are ADR-0007(c)'s own dedicated, separately-audited action from
/// Sprint 7, not part of this command.
/// </summary>
public sealed record ProjectDto(
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
    decimal? AdvanceRecoveryEndPct)
{
    public static ProjectDto From(Project project) => new(
        project.Id,
        project.Name,
        project.Code,
        project.Owner,
        project.ContractStart,
        project.ContractFinish,
        project.BAC,
        project.ContractValue,
        project.RetentionRate,
        project.AdvanceRate,
        project.RetentionCapPercentage,
        project.RetentionRelease1Percentage,
        project.DefectsLiabilityMonths,
        project.AdvanceAmountPaid,
        project.AdvanceRecoveryMethod,
        project.AdvanceRecoveryStartPct,
        project.AdvanceRecoveryRatePct,
        project.AdvanceRecoveryEndPct);
}
