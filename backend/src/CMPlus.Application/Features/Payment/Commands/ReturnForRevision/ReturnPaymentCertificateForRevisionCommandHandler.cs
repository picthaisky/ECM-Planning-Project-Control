using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Payment.Commands.ReturnForRevision;

public sealed class ReturnPaymentCertificateForRevisionCommandHandler(
    IPaymentCertificateRepository repository,
    IApprovalActionRepository actionRepository,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ReturnPaymentCertificateForRevisionCommand, Result<PaymentCertificateDto>>
{
    public async Task<Result<PaymentCertificateDto>> Handle(
        ReturnPaymentCertificateForRevisionCommand request, CancellationToken cancellationToken)
    {
        var certificate = await repository.FindAsync(request.PaymentCertificateId, cancellationToken);
        if (certificate is null)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.NotFound);
        }

        if (certificate.Status != PaymentCertificateStatus.PendingApproval)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.InvalidStatusForTransition);
        }

        var actorRole = currentUser.Role;
        var actorUserId = currentUser.UserId ?? Guid.Empty;

        // approval-workflow.md §4: "actor holds any pending step's role" - every step from
        // CurrentStepNo (inclusive) through TotalSteps, not only the current one. Once a step is
        // cleared (Approved), its role no longer counts as "pending". H-01 fix (security review
        // sprint-09.md): resolved from the chain snapshotted onto this document at Submit time -
        // never re-derived from the pinned policy's entire rule set (see
        // ApprovePaymentCertificateCommandHandler's remarks for why that was the H-01 bug). No
        // "corrupt chain" guard is needed here the way Approve/Reject need one: Any() degrades safely
        // to false (403 NotAuthorizedForApprovalStep) rather than throwing when the snapshot is
        // empty/missing a rung.
        var holdsAnyPendingStepRole = certificate.ApprovalSteps.Any(s =>
            s.RevisionNo == certificate.RevisionNo && s.StepNo >= certificate.CurrentStepNo && s.RequiredRole == actorRole);
        if (!holdsAnyPendingStepRole)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.NotAuthorizedForApprovalStep);
        }

        var now = clock.UtcNow;
        var stepNoActedOn = certificate.CurrentStepNo;
        var policyIdForAction = certificate.ApprovalPolicyId ?? Guid.Empty;
        var policyVersionForAction = certificate.ApprovalPolicyVersion ?? 0;
        // Captured BEFORE ReturnForRevision() bumps RevisionNo: this action closes out the revision
        // that was PendingApproval, so it belongs on that revision's history, not the new one -
        // exactly what makes R9's "QS approval voided" timeline readable in ApprovalAction order.
        var revisionNoBeingClosed = certificate.RevisionNo;

        certificate.ReturnForRevision();

        actionRepository.Add(new ApprovalAction(
            tenantProvider.TenantId,
            ApprovalDocumentType.PaymentCertificate,
            certificate.Id,
            revisionNoBeingClosed,
            stepNoActedOn,
            actorUserId,
            actorRole,
            ApprovalActionType.ReturnForRevision,
            request.Comment,
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
