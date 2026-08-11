using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.VariationOrder;

/// <summary>One rung of <see cref="VariationOrderDto.ApprovalSteps"/> - the snapshotted
/// <see cref="VariationOrderApprovalStep"/> exactly as resolved at Submit time (H-01 fix pattern).
/// Mirrors <c>PaymentCertificateApprovalStepDto</c>.</summary>
public sealed record VariationOrderApprovalStepDto(int StepNo, UserRole RequiredRole, int QuorumCount);

/// <summary>One line of <see cref="VariationOrderDto.ScopeItems"/>.</summary>
public sealed record VariationOrderScopeItemDto(Guid ActivityId, decimal BudgetCostDelta, string? Note);

/// <summary>
/// Wire shape for a <see cref="CMPlus.Domain.Entities.VariationOrder"/>, shared by every S10-BE-01/
/// 02/03 command/query handler as its success response - mirrors <c>PaymentCertificateDto</c>'s
/// <c>From(entity)</c> pattern.
/// </summary>
public sealed record VariationOrderDto(
    Guid Id,
    Guid ProjectId,
    string VoNumber,
    string? Description,
    string? Justification,
    decimal Amount,
    VariationOrderType Type,
    int TimeImpactDays,
    VariationOrderStatus Status,
    int RevisionNo,
    int CurrentStepNo,
    int TotalSteps,
    Guid? ApprovalPolicyId,
    int? ApprovalPolicyVersion,
    bool AllowSelfApproval,
    IReadOnlyList<VariationOrderApprovalStepDto> ApprovalSteps,
    IReadOnlyList<VariationOrderScopeItemDto> ScopeItems,
    Guid CreatedByUserId,
    Guid? SubmittedByUserId,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    decimal? BacBefore,
    decimal? BacAfter,
    decimal? ContractValueBefore,
    decimal? ContractValueAfter,
    decimal? CumulativeVoPctAtApproval,
    decimal? EscalationBasisContractValue)
{
    public static VariationOrderDto From(CMPlus.Domain.Entities.VariationOrder variationOrder) => new(
        variationOrder.Id,
        variationOrder.ProjectId,
        variationOrder.VoNumber,
        variationOrder.Description,
        variationOrder.Justification,
        variationOrder.Amount,
        variationOrder.Type,
        variationOrder.TimeImpactDays,
        variationOrder.Status,
        variationOrder.RevisionNo,
        variationOrder.CurrentStepNo,
        variationOrder.TotalSteps,
        variationOrder.ApprovalPolicyId,
        variationOrder.ApprovalPolicyVersion,
        variationOrder.AllowSelfApproval,
        variationOrder.ApprovalSteps
            .OrderBy(s => s.StepNo)
            .Select(s => new VariationOrderApprovalStepDto(s.StepNo, s.RequiredRole, s.QuorumCount))
            .ToList(),
        variationOrder.ScopeItems
            .Select(i => new VariationOrderScopeItemDto(i.ActivityId, i.BudgetCostDelta, i.Note))
            .ToList(),
        variationOrder.CreatedByUserId,
        variationOrder.SubmittedByUserId,
        variationOrder.SubmittedAt,
        variationOrder.ApprovedAt,
        variationOrder.BacBefore,
        variationOrder.BacAfter,
        variationOrder.ContractValueBefore,
        variationOrder.ContractValueAfter,
        variationOrder.CumulativeVoPctAtApproval,
        variationOrder.EscalationBasisContractValue);
}
