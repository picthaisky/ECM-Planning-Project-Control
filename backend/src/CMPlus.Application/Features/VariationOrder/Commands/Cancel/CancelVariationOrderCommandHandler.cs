using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.Cancel;

public sealed class CancelVariationOrderCommandHandler(
    IVariationOrderRepository repository,
    IApprovalActionRepository actionRepository,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<CancelVariationOrderCommand, Result<VariationOrderDto>>
{
    public async Task<Result<VariationOrderDto>> Handle(CancelVariationOrderCommand request, CancellationToken cancellationToken)
    {
        var variationOrder = await repository.FindAsync(request.VariationOrderId, cancellationToken);
        if (variationOrder is null)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.NotFound);
        }

        // L-01 (domain-rules.md §7.5, sprint-10 security review): fail closed on a null actor rather
        // than stamping Guid.Empty - structurally unreachable behind [Authorize] but never silently
        // accepted here either. Mirrors ApproveVariationOrderCommandHandler's pattern exactly.
        if (currentUser.UserId is not { } actorUserId)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ActorRequired);
        }

        var actorRole = currentUser.Role;
        var now = clock.UtcNow;

        if (variationOrder.Status != VariationOrderStatus.Draft)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.InvalidStatusForTransition);
        }

        if (actorUserId != variationOrder.CreatedByUserId && actorRole != UserRole.PM)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.NotAuthorizedForApprovalStep);
        }

        variationOrder.Cancel(actorUserId, actorRole);

        // No chain is attached in Draft (ApprovalPolicyId is null) - Guid.Empty/0 mirrors the
        // "not step-specific" convention ApprovalAction's own remarks give for Submit/Cancel.
        actionRepository.Add(new ApprovalAction(
            tenantProvider.TenantId,
            ApprovalDocumentType.VariationOrder,
            variationOrder.Id,
            variationOrder.RevisionNo,
            stepNo: 0,
            actorUserId,
            actorRole,
            ApprovalActionType.Cancel,
            request.Comment,
            now,
            variationOrder.ApprovalPolicyId ?? Guid.Empty,
            variationOrder.ApprovalPolicyVersion ?? 0));

        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ConcurrencyConflict);
        }

        return Result<VariationOrderDto>.Success(VariationOrderDto.From(variationOrder));
    }
}
