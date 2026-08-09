using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Payment.Commands.Reject;

/// <summary>
/// Note: unlike <c>Approve</c>, this handler does not re-check self-approval. Approval-workflow.md
/// §6.1's separation-of-duties rule is scoped explicitly to "may not <i>approve</i> any step" - a
/// document's creator/submitter who also happens to hold the final step's required role is not
/// barred from rejecting their own submission by that rule, and no other rule in that document
/// extends the restriction to Reject. Implemented as specified rather than assumed.
/// </summary>
public sealed class RejectPaymentCertificateCommandHandler(
    IPaymentCertificateRepository repository,
    IApprovalActionRepository actionRepository,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RejectPaymentCertificateCommand, Result<PaymentCertificateDto>>
{
    public async Task<Result<PaymentCertificateDto>> Handle(RejectPaymentCertificateCommand request, CancellationToken cancellationToken)
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

        // H-01 fix (security review sprint-09.md): resolved from the chain snapshotted onto this
        // document at Submit time - see ApprovePaymentCertificateCommandHandler's remarks.
        var finalStep = certificate.ApprovalSteps.FirstOrDefault(
            s => s.RevisionNo == certificate.RevisionNo && s.StepNo == certificate.TotalSteps);
        if (finalStep is null)
        {
            // Should be unreachable post-M-03 fix - see ApprovePaymentCertificateCommandHandler's
            // identical remark.
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.CorruptApprovalChain);
        }

        var actorRole = currentUser.Role;
        var actorUserId = currentUser.UserId ?? Guid.Empty;

        // approval-workflow.md §4/§6.1: only the final step's approver may reject; an intermediate
        // approver (CurrentStepNo < TotalSteps) may only ReturnForRevision, even if they do hold the
        // eventual final step's role.
        var isAtFinalStep = certificate.CurrentStepNo == certificate.TotalSteps;
        if (!isAtFinalStep || actorRole != finalStep.RequiredRole)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.NotAuthorizedForApprovalStep);
        }

        var now = clock.UtcNow;
        var stepNoActedOn = certificate.CurrentStepNo;
        var policyIdForAction = certificate.ApprovalPolicyId ?? Guid.Empty;
        var policyVersionForAction = certificate.ApprovalPolicyVersion ?? 0;

        certificate.Reject(actorRole, finalStep.RequiredRole);

        actionRepository.Add(new ApprovalAction(
            tenantProvider.TenantId,
            ApprovalDocumentType.PaymentCertificate,
            certificate.Id,
            certificate.RevisionNo,
            stepNoActedOn,
            actorUserId,
            actorRole,
            ApprovalActionType.Reject,
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
