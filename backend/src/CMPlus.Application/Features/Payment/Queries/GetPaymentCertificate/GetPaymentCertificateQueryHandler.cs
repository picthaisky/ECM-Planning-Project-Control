using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Payment.Queries.GetPaymentCertificate;

/// <summary>
/// Also supplies real quorum progress ("N of M signatures collected" on the certificate's current
/// step) whenever a chain is currently attached - security review sprint-09.md §9.5(ii)
/// (informational): today a quorum step's first approver gets a plain `200` with an unchanged
/// status from `approve`, indistinguishable from a no-op, and the frontend
/// (`usePaymentCertificateActions.ts`) has to infer what happened by diffing the certificate
/// immediately before/after the call. This read supplies the real count cheaply: one extra call to
/// the already-built, already-indexed <see cref="IApprovalActionRepository.GetHistoryAsync"/>
/// (design.md §3's mandatory `ApprovalAction (TenantId, DocumentType, DocumentId, RevisionNo,
/// StepNo)` index), counting distinct <c>Approve</c> actors for the certificate's current
/// step/revision - the exact computation <c>ApprovePaymentCertificateCommandHandler</c> already
/// performs for the H-02 quorum check, simply re-read here rather than re-derived or duplicated.
///
/// <para><see langword="null"/> (never a bare <c>0</c>) whenever no chain is currently attached
/// (<c>TotalSteps == 0</c>: <c>NotDue</c>/<c>Draft</c>/a voided chain after
/// <c>ReturnForRevision</c>/<c>Withdraw</c>) - "not applicable" must never be confused with
/// "genuinely zero collected so far", the same discipline <c>IActualCostReader</c>'s
/// <c>ActualCostResult</c> already established for AC (ADR-0013(f)).</para>
///
/// <para>Deliberately does NOT touch any of the five existing S9-BE-05 command handlers
/// (Submit/Approve/ReturnForRevision/Reject/RecordPayment) to make their own responses carry a real
/// count too - those are already security-cleared, and <see cref="PaymentCertificateDto.From"/>'s
/// new parameter defaults to <see langword="null"/> precisely so this gap closure does not have to
/// reopen that surface. Mechanically cheap to add there later (the exact local variable already
/// exists in <c>ApprovePaymentCertificateCommandHandler</c>), but is left as a follow-up rather than
/// done silently here - see this task's handoff report.</para>
/// </summary>
public sealed class GetPaymentCertificateQueryHandler(
    IPaymentCertificateRepository certificates, IApprovalActionRepository approvalActions)
    : IRequestHandler<GetPaymentCertificateQuery, Result<PaymentCertificateDto>>
{
    public async Task<Result<PaymentCertificateDto>> Handle(GetPaymentCertificateQuery request, CancellationToken cancellationToken)
    {
        var certificate = await certificates.GetByIdAsync(request.PaymentCertificateId, cancellationToken);
        if (certificate is null)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.NotFound);
        }

        int? currentStepApprovalsCollected = null;
        if (certificate.TotalSteps > 0)
        {
            var history = await approvalActions.GetHistoryAsync(
                ApprovalDocumentType.PaymentCertificate, certificate.Id, cancellationToken);

            currentStepApprovalsCollected = history
                .Where(a => a.RevisionNo == certificate.RevisionNo
                            && a.StepNo == certificate.CurrentStepNo
                            && a.Action == ApprovalActionType.Approve)
                .Select(a => a.ActorUserId)
                .Distinct()
                .Count();
        }

        return Result<PaymentCertificateDto>.Success(PaymentCertificateDto.From(certificate, currentStepApprovalsCollected));
    }
}
