using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.Create;

/// <summary>
/// Creates a new <see cref="CMPlus.Domain.Entities.VariationOrder"/> in <c>Draft</c> (S10-BE-01).
/// <paramref name="ScopeItems"/> reuses the Domain layer's own input shape
/// (<see cref="VariationOrderScopeItemInput"/>) rather than a duplicated Application-layer record -
/// same pattern <c>PaymentCertificateApprovalStepInput</c> already established for chain steps.
/// </summary>
public sealed record CreateVariationOrderCommand(
    Guid ProjectId,
    string VoNumber,
    string? Description,
    string? Justification,
    decimal Amount,
    int TimeImpactDays,
    IReadOnlyList<VariationOrderScopeItemInput> ScopeItems) : IRequest<Result<VariationOrderDto>>;
