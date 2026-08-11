using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.UpdateContent;

public sealed class UpdateVariationOrderContentCommandHandler(IVariationOrderRepository repository)
    : IRequestHandler<UpdateVariationOrderContentCommand, Result<VariationOrderDto>>
{
    public async Task<Result<VariationOrderDto>> Handle(UpdateVariationOrderContentCommand request, CancellationToken cancellationToken)
    {
        var variationOrder = await repository.FindAsync(request.VariationOrderId, cancellationToken);
        if (variationOrder is null)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.NotFound);
        }

        if (variationOrder.Status != VariationOrderStatus.Draft)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.InvalidStatusForTransition);
        }

        if (request.ScopeItems.Count > 0)
        {
            var activityIds = request.ScopeItems.Select(i => i.ActivityId).Distinct().ToList();
            var activities = await repository.FindActivitiesForApprovalAsync(variationOrder.ProjectId, activityIds, cancellationToken);
            if (activities.Count != activityIds.Count)
            {
                return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.UnknownActivity);
            }
        }

        var scopeTotal = request.ScopeItems.Sum(i => i.BudgetCostDelta);
        if (scopeTotal != request.Amount)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ScopeBudgetMismatch);
        }

        if (request.Amount == 0m && request.TimeImpactDays == 0 && request.ScopeItems.Count == 0)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.EmptyVariation);
        }

        variationOrder.SetVariationContent(
            request.Amount, request.Description, request.Justification, request.TimeImpactDays, request.ScopeItems);

        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ConcurrencyConflict);
        }

        return Result<VariationOrderDto>.Success(VariationOrderDto.From(variationOrder));
    }
}
