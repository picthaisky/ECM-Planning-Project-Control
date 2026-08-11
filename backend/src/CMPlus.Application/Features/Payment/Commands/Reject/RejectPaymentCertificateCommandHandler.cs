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
///
/// <para><b>ADR-0016 / domain-rules.md §8 ("quorum binds rejection"):</b> mirrors
/// <see cref="CMPlus.Application.Features.Payment.Commands.Approve.ApprovePaymentCertificateCommandHandler"/>'s
/// H-02 quorum shape exactly - a step configured with <c>QuorumCount = q</c> now reaches
/// <c>Rejected</c> only once q distinct role-holders reject it, counted from the same append-only
/// <c>ApprovalAction</c> history Approve already reads, and the widened <c>DuplicateChainVoter</c>
/// guard (formerly <c>DuplicateChainApprover</c>, Approve-only) blocks an actor who already voted
/// either way this revision from voting again. Fixes security review sprint-09.md N-05: before this,
/// an actor who had approved 1-of-2 could then reject and terminate the step alone, and a single
/// rejector always terminated a <c>QuorumCount = 2</c> step regardless of configuration.</para>
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
        if (currentUser.UserId is not { } actorUserId)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.ActorRequired);
        }


        // approval-workflow.md §4/§6.1: only the final step's approver may reject; an intermediate
        // approver (CurrentStepNo < TotalSteps) may only ReturnForRevision, even if they do hold the
        // eventual final step's role.
        var isAtFinalStep = certificate.CurrentStepNo == certificate.TotalSteps;
        if (!isAtFinalStep || actorRole != finalStep.RequiredRole)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.NotAuthorizedForApprovalStep);
        }

        // ADR-0016 / domain-rules.md §8.3: DuplicateChainVoter (widened from the Approve-only
        // DuplicateChainApprover) - no actor may cast both an Approve and a Reject on the same
        // revision, nor reject twice. Mirrors ApprovePaymentCertificateCommandHandler's identical
        // check verbatim (same predicate, same revision scope, any StepNo).
        var history = await actionRepository.GetHistoryAsync(ApprovalDocumentType.PaymentCertificate, certificate.Id, cancellationToken);
        var alreadyVotedThisRevision = history.Any(a =>
            a.RevisionNo == certificate.RevisionNo
            && a.ActorUserId == actorUserId
            && (a.Action == ApprovalActionType.Approve || a.Action == ApprovalActionType.Reject));
        if (alreadyVotedThisRevision)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.DuplicateChainVoter);
        }

        // ADR-0016 / domain-rules.md §8.2: a step terminates via Reject only once QuorumCount DISTINCT
        // users have rejected THAT SPECIFIC (final) step - mirrors the H-02 approve-quorum count
        // exactly, just keyed on Action == Reject instead of Approve. .Distinct() is redundant given
        // the DuplicateChainVoter check just above (same reasoning as Approve's own remark) but kept
        // as the same defensive, self-documenting belt-and-braces measure.
        var priorDistinctRejectorsForThisStep = history
            .Where(a => a.RevisionNo == certificate.RevisionNo && a.StepNo == certificate.CurrentStepNo && a.Action == ApprovalActionType.Reject)
            .Select(a => a.ActorUserId)
            .Distinct()
            .Count();
        var rejectQuorumSatisfied = priorDistinctRejectorsForThisStep + 1 >= finalStep.QuorumCount;

        var now = clock.UtcNow;
        var stepNoActedOn = certificate.CurrentStepNo;
        var policyIdForAction = certificate.ApprovalPolicyId ?? Guid.Empty;
        var policyVersionForAction = certificate.ApprovalPolicyVersion ?? 0;

        certificate.Reject(actorRole, finalStep.RequiredRole, now, rejectQuorumSatisfied);

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
