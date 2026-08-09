using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Payment.Commands.RecordPayment;

public sealed class RecordPaymentForPaymentCertificateCommandHandler(
    IPaymentCertificateRepository repository,
    IApprovalActionRepository actionRepository,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RecordPaymentForPaymentCertificateCommand, Result<PaymentCertificateDto>>
{
    public async Task<Result<PaymentCertificateDto>> Handle(
        RecordPaymentForPaymentCertificateCommand request, CancellationToken cancellationToken)
    {
        var certificate = await repository.FindAsync(request.PaymentCertificateId, cancellationToken);
        if (certificate is null)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.NotFound);
        }

        if (certificate.Status != PaymentCertificateStatus.Certified)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.InvalidStatusForTransition);
        }

        var now = clock.UtcNow;
        var actorUserId = currentUser.UserId ?? Guid.Empty;

        // ApprovalPolicyId/Version are still the values the last successful chain pinned - Certified
        // never voids them (only ReturnForRevision/Withdraw do), so this is a real reference, not
        // the Guid.Empty fallback, for any certificate that reached Certified normally.
        var policyIdForAction = certificate.ApprovalPolicyId ?? Guid.Empty;
        var policyVersionForAction = certificate.ApprovalPolicyVersion ?? 0;

        certificate.RecordPayment(request.Reference, request.PaidAt);

        // Not a chain decision (no pending step once Certified) - StepNo 0, same convention as
        // Submit/Cancel.
        actionRepository.Add(new ApprovalAction(
            tenantProvider.TenantId,
            ApprovalDocumentType.PaymentCertificate,
            certificate.Id,
            certificate.RevisionNo,
            stepNo: 0,
            actorUserId,
            currentUser.Role,
            ApprovalActionType.RecordPayment,
            comment: null,
            now,
            policyIdForAction,
            policyVersionForAction));

        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.ConcurrencyConflict);
        }

        return Result<PaymentCertificateDto>.Success(PaymentCertificateDto.From(certificate));
    }
}
