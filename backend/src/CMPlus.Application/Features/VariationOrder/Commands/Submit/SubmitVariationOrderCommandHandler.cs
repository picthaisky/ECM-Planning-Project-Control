using CMPlus.Application.Abstractions;
using CMPlus.Application.Approval;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.Submit;

public sealed class SubmitVariationOrderCommandHandler(
    IVariationOrderRepository repository,
    IProjectRepository projectRepository,
    IApprovalPolicyReader policyReader,
    IApprovalRoutingService routingService,
    IApprovalActionRepository actionRepository,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<SubmitVariationOrderCommand, Result<VariationOrderDto>>
{
    public async Task<Result<VariationOrderDto>> Handle(SubmitVariationOrderCommand request, CancellationToken cancellationToken)
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

        var project = await projectRepository.FindAsync(variationOrder.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ProjectNotFound);
        }

        var now = clock.UtcNow;

        var candidatePolicies = await policyReader.GetCandidatePoliciesAsync(
            ApprovalDocumentType.VariationOrder, now, cancellationToken);

        var cumulativeApprovedVoAmount = await repository.GetApprovedNetSignedTotalAsync(variationOrder.ProjectId, cancellationToken);

        // domain-rules.md §3.1: A^route = abs(Amount) - computed INSIDE ApprovalRoutingService.Resolve
        // from the raw signed Amount handed to it here (never pre-abs()'d at this call site - see
        // ApprovalRoutingRequest.Amount's own remarks). This is the Sprint 2 service, reused unchanged
        // (S10-BE-02 DoD).
        var routingResult = routingService.Resolve(new ApprovalRoutingRequest(
            ApprovalDocumentType.VariationOrder,
            variationOrder.ProjectId,
            variationOrder.Amount,
            now,
            candidatePolicies,
            EscalationBaselineContractValue: project.EscalationBaselineContractValue,
            CumulativeApprovedVoAmount: cumulativeApprovedVoAmount));

        if (routingResult.IsFailure)
        {
            // ApprovalErrorCodes.PolicyGap / ContractValueNotConfigured - both 422, already mapped by
            // ResultProblemMapper. Fail closed: the document is left in Draft, never auto-approved.
            return Result<VariationOrderDto>.Failure(routingResult.Error);
        }

        var chain = routingResult.Value;
        var actorUserId = currentUser.UserId ?? Guid.Empty;

        // L-01 (domain-rules.md §7.5): fail closed on a null actor rather than stamping Guid.Empty -
        // structurally unreachable behind [Authorize] but never silently accepted here either.
        if (currentUser.UserId is null)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ActorRequired);
        }

        // H-01 fix pattern: snapshot the resolved chain - exactly as the routing engine produced it
        // for THIS document's routing amount - onto the document itself, rather than a bare step
        // count. R2's own point: Resolve already returned the POSITIVE routing amount
        // (chain.RoutingAmount) purely as a chain-selection input; nothing below ever writes it back
        // onto variationOrder.Amount, which stays exactly the signed value the caller submitted.
        var resolvedSteps = chain.Steps
            .Select(step => new VariationOrderApprovalStepInput(step.StepNo, step.RequiredRole, step.QuorumCount))
            .ToList();

        variationOrder.Submit(resolvedSteps, chain.ApprovalPolicyId, chain.ApprovalPolicyVersion, chain.AllowSelfApproval, actorUserId, now);

        actionRepository.Add(new ApprovalAction(
            tenantProvider.TenantId,
            ApprovalDocumentType.VariationOrder,
            variationOrder.Id,
            variationOrder.RevisionNo,
            stepNo: 0,
            actorUserId,
            currentUser.Role,
            ApprovalActionType.Submit,
            comment: null,
            now,
            chain.ApprovalPolicyId,
            chain.ApprovalPolicyVersion));

        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ConcurrencyConflict);
        }

        return Result<VariationOrderDto>.Success(VariationOrderDto.From(variationOrder));
    }
}
