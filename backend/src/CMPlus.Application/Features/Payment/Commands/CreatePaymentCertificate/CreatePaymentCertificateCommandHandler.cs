using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Payment;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Payment.Commands.CreatePaymentCertificate;

/// <summary>
/// S9-BE-05 create: builds one period's IPC in <c>Draft</c> with its money fields already computed by
/// <see cref="CertificateCalculator"/>, so the existing Submit handler (which reads
/// <c>GrossCertifiedAmount</c> directly) can route it unchanged.
///
/// <para><b>Two derivations are auto-resolved here, and neither is a free choice - each is forced by an
/// existing invariant, so this handler never guesses a money rule:</b></para>
/// <list type="number">
/// <item><b><see cref="CreatePaymentCertificateCommand.MilestoneValue"/>-scoped previous cumulative.</b>
/// <c>CertificateCalculator</c> computes the period gross as
/// <c>MilestoneValue * (thisCumulative - previousCumulative) / 100</c> - both percentages are of the
/// <i>same</i> milestone's value, so the previous cumulative must be that milestone's own prior
/// certified progress, never a project-wide figure (which would be dimensionally meaningless against
/// one milestone's value). Derived as the max <c>ApprovePct</c> among prior certificates for the
/// <b>same</b> <c>MilestoneNo</c>, or 0 if none.</item>
/// <item><b>Only <c>Certified</c>/<c>Paid</c> priors count.</b> A certificate commits its progress
/// exactly when it reaches <c>Certified</c> - that is when the finance ledger posts (S9-BE-04), which
/// is the same source <c>IProjectFinanceLedgerReader</c> sums for retention/advance below. Counting a
/// <c>Draft</c>/<c>PendingApproval</c>/<c>Rejected</c> certificate's <c>ApprovePct</c> toward the
/// monotonic floor would let an uncommitted or rejected claim block a legitimate certification, and
/// would disagree with the ledger. Applied identically to the project-wide gross sum below (only meaningful
/// for threshold-banded advance recovery).</item>
/// </list>
/// </summary>
public sealed class CreatePaymentCertificateCommandHandler(
    IProjectRepository projectRepository,
    IPaymentCertificateRepository certificateRepository,
    IProjectFinanceLedgerReader ledgerReader,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser)
    : IRequestHandler<CreatePaymentCertificateCommand, Result<PaymentCertificateDto>>
{
    private static readonly PaymentCertificateStatus[] CommittedStatuses =
        [PaymentCertificateStatus.Certified, PaymentCertificateStatus.Paid];

    public async Task<Result<PaymentCertificateDto>> Handle(
        CreatePaymentCertificateCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorUserId)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.ActorRequired);
        }

        var project = await projectRepository.FindAsync(request.ProjectId, cancellationToken);
        if (project is null)
        {
            // Tenant-scoped by the global query filter (ADR-0002): cross-tenant is indistinguishable
            // from "does not exist".
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.ProjectNotFound);
        }

        var priorCertificates = await certificateRepository.ListByProjectAsync(request.ProjectId, cancellationToken);

        var previousCumulativeApprovePct = priorCertificates
            .Where(c => c.MilestoneNo == request.MilestoneNo && CommittedStatuses.Contains(c.Status))
            .Select(c => c.ApprovePct)
            .DefaultIfEmpty(0m)
            .Max();

        var projectCumulativeGrossCertifiedBefore = priorCertificates
            .Where(c => CommittedStatuses.Contains(c.Status))
            .Sum(c => c.GrossCertifiedAmount);

        var retentionHeldBefore = await ledgerReader.GetRetentionHeldAsync(request.ProjectId, cancellationToken);
        var advanceRecoveredBefore = await ledgerReader.GetAdvanceRecoveredAsync(request.ProjectId, cancellationToken);

        var calculation = CertificateCalculator.Calculate(new CertificateCalculationInput(
            MilestoneValue: request.MilestoneValue,
            PreviousCumulativeApprovePct: previousCumulativeApprovePct,
            ThisCumulativeApprovePct: request.ThisCumulativeApprovePct,
            RetentionRatePercent: project.RetentionRate,
            RetentionCapPercentage: project.RetentionCapPercentage,
            ContractValue: project.ContractValue,
            RetentionHeldBefore: retentionHeldBefore,
            AdvanceRatePercent: project.AdvanceRate,
            AdvanceAmountPaid: project.AdvanceAmountPaid,
            AdvanceRecoveredBefore: advanceRecoveredBefore,
            AdvanceRecoveryMethod: project.AdvanceRecoveryMethod,
            AdvanceRecoveryStartPct: project.AdvanceRecoveryStartPct,
            AdvanceRecoveryRatePct: project.AdvanceRecoveryRatePct,
            AdvanceRecoveryEndPct: project.AdvanceRecoveryEndPct,
            ProjectCumulativeGrossCertifiedBefore: projectCumulativeGrossCertifiedBefore,
            ManualAdvanceRecoveryAmount: request.ManualAdvanceRecoveryAmount));

        if (calculation.IsFailure)
        {
            // RetentionRateNotConfigured / AdvanceRateNotConfigured / ApprovePctNotMonotonic /
            // AdvanceRecoveryThresholdBandsNotConfigured - all 422 (mapped in ResultProblemMapper).
            return Result<PaymentCertificateDto>.Failure(calculation.Error);
        }

        var result = calculation.Value;

        var certificate = new PaymentCertificate(
            tenantProvider.TenantId,
            request.ProjectId,
            request.MilestoneNo,
            request.Description,
            request.MilestoneValue,
            previousCumulativeApprovePct,
            actorUserId);

        certificate.SetPeriodClaim(
            request.ThisCumulativeApprovePct,
            request.ClaimPct,
            request.ActualProgressPct,
            result.GrossCertifiedAmount,
            result.RetentionAmount,
            result.AdvanceRecoveryAmount,
            result.NetPayment);

        await certificateRepository.AddAsync(certificate, cancellationToken);

        if (!await certificateRepository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.ConcurrencyConflict);
        }

        return Result<PaymentCertificateDto>.Success(PaymentCertificateDto.From(certificate));
    }
}
