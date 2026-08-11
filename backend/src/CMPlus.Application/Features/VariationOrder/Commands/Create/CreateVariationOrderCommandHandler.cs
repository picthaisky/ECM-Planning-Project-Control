using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.Create;

public sealed class CreateVariationOrderCommandHandler(
    IVariationOrderRepository repository,
    IProjectRepository projectRepository,
    ICurrentUserContext currentUser)
    : IRequestHandler<CreateVariationOrderCommand, Result<VariationOrderDto>>
{
    public async Task<Result<VariationOrderDto>> Handle(CreateVariationOrderCommand request, CancellationToken cancellationToken)
    {
        var project = await projectRepository.FindAsync(request.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ProjectNotFound);
        }

        if (await repository.ExistsWithVoNumberAsync(request.ProjectId, request.VoNumber, cancellationToken))
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.DuplicateVoNumber);
        }

        // L-01 (domain-rules.md §7.5): actor identity must never be fabricated - fail closed on a
        // null user id rather than silently stamping Guid.Empty onto CreatedByUserId, which would
        // defeat the self-approval guard forever (Guid.Empty could never legitimately equal a real
        // actor's id, but it is also evidentially worthless on a document that then claims a real
        // creator exists).
        if (currentUser.UserId is not { } createdByUserId)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ActorRequired);
        }

        if (request.ScopeItems.Count > 0)
        {
            var activityIds = request.ScopeItems.Select(i => i.ActivityId).Distinct().ToList();
            var activities = await repository.FindActivitiesForApprovalAsync(request.ProjectId, activityIds, cancellationToken);
            if (activities.Count != activityIds.Count)
            {
                return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.UnknownActivity);
            }
        }

        // Pre-checks mirroring VariationOrder's own constructor invariants (domain-rules.md §5.2/§7.3)
        // as typed Results instead of letting the DomainException escape uncaught - the same
        // defense-in-depth discipline every handler in this codebase follows.
        var scopeTotal = request.ScopeItems.Sum(i => i.BudgetCostDelta);
        if (scopeTotal != request.Amount)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ScopeBudgetMismatch);
        }

        if (request.Amount == 0m && request.TimeImpactDays == 0 && request.ScopeItems.Count == 0)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.EmptyVariation);
        }

        var variationOrder = new Domain.Entities.VariationOrder(
            project.TenantId,
            request.ProjectId,
            request.VoNumber,
            createdByUserId,
            request.Amount,
            request.Description,
            request.Justification,
            request.TimeImpactDays,
            request.ScopeItems);

        repository.Add(variationOrder);

        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ConcurrencyConflict);
        }

        return Result<VariationOrderDto>.Success(VariationOrderDto.From(variationOrder));
    }
}
