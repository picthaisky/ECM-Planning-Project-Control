using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.Withdraw;

public sealed class WithdrawVariationOrderCommandHandler(
    IVariationOrderRepository repository,
    IApprovalActionRepository actionRepository,
    ICurrentUserContext currentUser)
    : IRequestHandler<WithdrawVariationOrderCommand, Result<VariationOrderDto>>
{
    public async Task<Result<VariationOrderDto>> Handle(WithdrawVariationOrderCommand request, CancellationToken cancellationToken)
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

        // L-01 (domain-rules.md §7.5, sprint-10 security review): fail closed on a null actor rather
        // than stamping Guid.Empty - structurally unreachable behind [Authorize] but never silently
        // accepted here either. Mirrors ApproveVariationOrderCommandHandler's pattern exactly.
        if (currentUser.UserId is not { } actorUserId)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ActorRequired);
        }

        // domain-rules.md §2.3: "zero votes cast on this revision" - a guard the aggregate's own
        // CurrentStepNo <= 1 check cannot fully express on its own, because a QuorumCount > 1 first
        // step can already hold one recorded vote without having advanced CurrentStepNo at all. Checked
        // here, against the append-only ApprovalAction history, before the aggregate's own (necessary
        // but not sufficient) CurrentStepNo guard runs.
        var history = await actionRepository.GetHistoryAsync(ApprovalDocumentType.VariationOrder, variationOrder.Id, cancellationToken);
        var anyVoteCastThisRevision = history.Any(a =>
            a.RevisionNo == variationOrder.RevisionNo && (a.Action == ApprovalActionType.Approve || a.Action == ApprovalActionType.Reject));
        if (anyVoteCastThisRevision)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.WithdrawAfterVoteCast);
        }

        variationOrder.Withdraw(actorUserId);

        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ConcurrencyConflict);
        }

        return Result<VariationOrderDto>.Success(VariationOrderDto.From(variationOrder));
    }
}
