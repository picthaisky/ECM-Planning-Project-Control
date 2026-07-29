using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Projects.Commands.UpdateProject;

/// <summary>
/// S4-BE-02 (US-4.3/4.4, docs/10 §2.6 "US-4.4 ขยาย"): edits the Project Info screen's full master-
/// data field set - name/code/owner/contract dates/BAC (US-4.3) plus the Retention/Advance/cap
/// commercial terms (US-4.4, Decision 2). Deliberately touches only the <c>Project</c> aggregate -
/// never a <c>PaymentCertificate</c> (none exist until Sprint 9) - so a rate change here can never
/// retroactively recalculate an already-issued certificate, by construction of this command's
/// dependencies alone. Excludes the EAC default fields (ADR-0007(c) is their own, separately
/// role-restricted and audited action, Sprint 7).
/// </summary>
public sealed record UpdateProjectCommand(
    Guid ProjectId,
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
    decimal? AdvanceRecoveryEndPct) : IRequest<Result<ProjectDto>>;
