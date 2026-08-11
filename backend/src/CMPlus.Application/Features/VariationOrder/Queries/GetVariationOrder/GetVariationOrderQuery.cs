using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Queries.GetVariationOrder;

/// <summary><c>GET /api/v1/variation-orders/{id}</c> - single-document read, the query-side sibling
/// of every S10-BE-01/02/03 transition command.</summary>
public sealed record GetVariationOrderQuery(Guid VariationOrderId) : IRequest<Result<VariationOrderDto>>;
