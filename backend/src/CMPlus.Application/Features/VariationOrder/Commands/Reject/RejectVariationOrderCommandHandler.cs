using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.Reject;

/// <summary>
/// ADR-0016 / domain-rules.md §8: quorum-bound reject with the widened <c>DuplicateChainVoter</c>
/// guard, shipped correctly for the VO from day one (the shape Sprint 9's IPC had to be retrofitted
/// for) - mirrors <c>RejectPaymentCertificateCommandHandler</c> exactly.
/// </summary>
public sealed class RejectVariationOrderCommandHandler(
    IVariationOrderRepository repository,
    IApprovalActionRepository actionRepository,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RejectVariationOrderCommand, Result<VariationOrderDto>>
{
    public async Task<Result<VariationOrderDto>> Handle(RejectVariationOrderCommand request, CancellationToken cancellationToken)
    {
        var variationOrder = await repository.FindAsync(request.VariationOrderId, cancellationToken);
        if (variationOrder is null)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.NotFound);
        }

        if (variationOrder.Status != VariationOrderStatus.PendingApproval)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.InvalidStatusForTransition);
        }

        var finalStep = variationOrder.ApprovalSteps.FirstOrDefault(
            s => s.RevisionNo == variationOrder.RevisionNo && s.StepNo == variationOrder.TotalSteps);
        if (finalStep is null)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.CorruptApprovalChain);
        }

        // L-01 (domain-rules.md §7.5, sprint-10 security review): fail closed on a null actor rather
        // than stamping Guid.Empty - structurally unreachable behind [Authorize] but never silently
        // accepted here either. Mirrors ApproveVariationOrderCommandHandler's pattern exactly.
        if (currentUser.UserId is not { } actorUserId)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ActorRequired);
        }

        var actorRole = currentUser.Role;

        var isAtFinalStep = variationOrder.CurrentStepNo == variationOrder.TotalSteps;
        if (!isAtFinalStep || actorRole != finalStep.RequiredRole)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.NotAuthorizedForApprovalStep);
        }

        var history = await actionRepository.GetHistoryAsync(ApprovalDocumentType.VariationOrder, variationOrder.Id, cancellationToken);
        var alreadyVotedThisRevision = history.Any(a =>
            a.RevisionNo == variationOrder.RevisionNo
            && a.ActorUserId == actorUserId
            && (a.Action == ApprovalActionType.Approve || a.Action == ApprovalActionType.Reject));
        if (alreadyVotedThisRevision)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.DuplicateChainVoter);
        }

        var priorDistinctRejectorsForThisStep = history
            .Where(a => a.RevisionNo == variationOrder.RevisionNo && a.StepNo == variationOrder.CurrentStepNo && a.Action == ApprovalActionType.Reject)
            .Select(a => a.ActorUserId)
            .Distinct()
            .Count();
        var rejectQuorumSatisfied = priorDistinctRejectorsForThisStep + 1 >= finalStep.QuorumCount;

        var now = clock.UtcNow;
        var stepNoActedOn = variationOrder.CurrentStepNo;
        var policyIdForAction = variationOrder.ApprovalPolicyId ?? Guid.Empty;
        var policyVersionForAction = variationOrder.ApprovalPolicyVersion ?? 0;

        variationOrder.Reject(actorRole, finalStep.RequiredRole, now, rejectQuorumSatisfied);

        actionRepository.Add(new ApprovalAction(
            tenantProvider.TenantId,
            ApprovalDocumentType.VariationOrder,
            variationOrder.Id,
            variationOrder.RevisionNo,
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
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ConcurrencyConflict);
        }

        return Result<VariationOrderDto>.Success(VariationOrderDto.From(variationOrder));
    }
}
