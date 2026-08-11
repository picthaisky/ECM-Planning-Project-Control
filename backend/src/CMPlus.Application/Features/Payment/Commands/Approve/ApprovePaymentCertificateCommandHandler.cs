using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Payment.Commands.Approve;

public sealed class ApprovePaymentCertificateCommandHandler(
    IPaymentCertificateRepository repository,
    IApprovalActionRepository actionRepository,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ApprovePaymentCertificateCommand, Result<PaymentCertificateDto>>
{
    public async Task<Result<PaymentCertificateDto>> Handle(ApprovePaymentCertificateCommand request, CancellationToken cancellationToken)
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

        // H-01 fix (security review sprint-09.md): the required role/quorum for the current step
        // come from the chain exactly as resolved and snapshotted onto THIS document at Submit time
        // (PaymentCertificate.ApprovalSteps) - never re-derived from the pinned policy's entire rule
        // set, which silently dropped the amount-band filter (ApprovalRoutingService.Resolve's
        // Where(MinAmount <= amount < MaxAmount)) and let an out-of-band rule sharing the same StepNo
        // authorize the wrong role.
        var currentStep = certificate.ApprovalSteps.FirstOrDefault(
            s => s.RevisionNo == certificate.RevisionNo && s.StepNo == certificate.CurrentStepNo);
        if (currentStep is null)
        {
            // Should be unreachable: ApprovalPolicy.ValidateBands (Domain) now rejects, at
            // construction time, any rule set that could ever produce a chain with no rung at some
            // StepNo <= TotalSteps (security review sprint-09.md M-03). Mapped Result, not a throw,
            // so a corrupt/legacy chain degrades to a clear 409 instead of an unhandled 500.
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.CorruptApprovalChain);
        }

        if (currentUser.UserId is not { } actorUserId)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.ActorRequired);
        }

        var actorRole = currentUser.Role;

        // design.md §1.2 "Approve re-checks": actor holds the current step's role... - resolved
        // entirely from the pinned chain, never a static role list (S9-BE-05 DoD: no "PM always
        // approves" escape hatch of any kind).
        if (actorRole != currentStep.RequiredRole)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.NotAuthorizedForApprovalStep);
        }

        // approval-workflow.md §6.1: the creator/submitter may not approve any step unless the
        // pinned policy opted in.
        if (!certificate.AllowSelfApproval && (actorUserId == certificate.CreatedByUserId || actorUserId == certificate.SubmittedByUserId))
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.SelfApprovalNotPermitted);
        }

        // approval-workflow.md §6.1: "a single user may not satisfy two steps of the same chain
        // even if they hold both roles - each step needs a distinct human." This also guarantees the
        // H-02 quorum count below is already counting distinct actors: no actor can appear twice in
        // this revision's Approve history at all, let alone twice for the same step.
        //
        // ADR-0016 / domain-rules.md §8.3: widened from "already Approved" to "already voted at all
        // (Approve OR Reject)" this revision - closes N-05 (sprint-09.md §9.5), where an actor who
        // approved 1-of-2 could then Reject and terminate a QuorumCount=2 step alone. Renamed
        // DuplicateChainApprover -> DuplicateChainVoter to match: the predicate is no longer
        // approve-specific.
        var history = await actionRepository.GetHistoryAsync(ApprovalDocumentType.PaymentCertificate, certificate.Id, cancellationToken);
        var alreadyVotedThisRevision = history.Any(a =>
            a.RevisionNo == certificate.RevisionNo
            && a.ActorUserId == actorUserId
            && (a.Action == ApprovalActionType.Approve || a.Action == ApprovalActionType.Reject));
        if (alreadyVotedThisRevision)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.DuplicateChainVoter);
        }

        // H-02 fix (security review sprint-09.md): a step clears only once QuorumCount DISTINCT
        // users have approved THAT SPECIFIC STEP (approval-workflow.md §6.2), not merely "someone
        // approved". Counted from the append-only ApprovalAction ledger already loaded above -
        // .Distinct() is redundant given the DuplicateChainVoter check just above (which already
        // guarantees every actor appears at most once across the whole revision, in either direction)
        // but is kept as a defensive, self-documenting belt-and-braces measure.
        var priorDistinctApproversForThisStep = history
            .Where(a => a.RevisionNo == certificate.RevisionNo && a.StepNo == certificate.CurrentStepNo && a.Action == ApprovalActionType.Approve)
            .Select(a => a.ActorUserId)
            .Distinct()
            .Count();
        var quorumSatisfied = priorDistinctApproversForThisStep + 1 >= currentStep.QuorumCount;

        var now = clock.UtcNow;
        var stepNoActedOn = certificate.CurrentStepNo;
        var policyIdForAction = certificate.ApprovalPolicyId ?? Guid.Empty;
        var policyVersionForAction = certificate.ApprovalPolicyVersion ?? 0;

        certificate.Approve(actorUserId, actorRole, currentStep.RequiredRole, certificate.AllowSelfApproval, now, quorumSatisfied);

        actionRepository.Add(new ApprovalAction(
            tenantProvider.TenantId,
            ApprovalDocumentType.PaymentCertificate,
            certificate.Id,
            certificate.RevisionNo,
            stepNoActedOn,
            actorUserId,
            actorRole,
            ApprovalActionType.Approve,
            request.Comment,
            now,
            policyIdForAction,
            policyVersionForAction));

        // S9-BE-04: the chain having just been exhausted posts the retention accrual/advance
        // recovery ledger rows in the SAME unit of work as the certificate's own state change and
        // the ApprovalAction row above - all three commit atomically or not at all. Never reached
        // when quorumSatisfied is false, since certificate.Status stays PendingApproval.
        if (certificate.Status == PaymentCertificateStatus.Certified)
        {
            repository.AddLedgerEntries(certificate.CreateCertificationLedgerEntries());
        }

        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.ConcurrencyConflict);
        }

        return Result<PaymentCertificateDto>.Success(PaymentCertificateDto.From(certificate));
    }
}
