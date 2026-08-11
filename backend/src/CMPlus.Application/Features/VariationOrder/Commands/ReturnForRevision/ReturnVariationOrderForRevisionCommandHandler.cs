using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.ReturnForRevision;

public sealed class ReturnVariationOrderForRevisionCommandHandler(
    IVariationOrderRepository repository,
    IApprovalActionRepository actionRepository,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ReturnVariationOrderForRevisionCommand, Result<VariationOrderDto>>
{
    public async Task<Result<VariationOrderDto>> Handle(
        ReturnVariationOrderForRevisionCommand request, CancellationToken cancellationToken)
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

        var actorRole = currentUser.Role;

        var holdsAnyPendingStepRole = variationOrder.ApprovalSteps.Any(s =>
            s.RevisionNo == variationOrder.RevisionNo && s.StepNo >= variationOrder.CurrentStepNo && s.RequiredRole == actorRole);
        if (!holdsAnyPendingStepRole)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.NotAuthorizedForApprovalStep);
        }

        var now = clock.UtcNow;
        var stepNoActedOn = variationOrder.CurrentStepNo;
        var policyIdForAction = variationOrder.ApprovalPolicyId ?? Guid.Empty;
        var policyVersionForAction = variationOrder.ApprovalPolicyVersion ?? 0;
        // Captured BEFORE ReturnForRevision() bumps RevisionNo - this action closes out the revision
        // that was PendingApproval (mirrors ReturnPaymentCertificateForRevisionCommandHandler).
        var revisionNoBeingClosed = variationOrder.RevisionNo;

        variationOrder.ReturnForRevision();

        actionRepository.Add(new ApprovalAction(
            tenantProvider.TenantId,
            ApprovalDocumentType.VariationOrder,
            variationOrder.Id,
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
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ConcurrencyConflict);
        }

        return Result<VariationOrderDto>.Success(VariationOrderDto.From(variationOrder));
    }
}
