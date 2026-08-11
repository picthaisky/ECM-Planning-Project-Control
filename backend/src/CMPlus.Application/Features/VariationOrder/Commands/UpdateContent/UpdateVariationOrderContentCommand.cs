using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.UpdateContent;

/// <summary>
/// Re-prices/edits a <c>Draft</c> variation order via <see cref="VariationOrder.SetVariationContent"/> -
/// the same method used for the initial content, and the only writer of the frozen field set
/// (domain-rules.md §2.4). The natural caller after <c>ReturnForRevision</c> (fixture V-10: "QS
/// re-prices ... and resubmits").
/// </summary>
public sealed record UpdateVariationOrderContentCommand(
    Guid VariationOrderId,
    string? Description,
    string? Justification,
    decimal Amount,
    int TimeImpactDays,
    IReadOnlyList<VariationOrderScopeItemInput> ScopeItems) : IRequest<Result<VariationOrderDto>>;
