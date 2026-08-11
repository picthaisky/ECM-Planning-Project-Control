using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Queries.ListVariationOrders;

/// <summary><c>GET /api/v1/projects/{projectId}/variation-orders</c> - the project-scoped VO
/// register (docs/9 §6-shaped: mirrors <c>ListPaymentCertificatesQuery</c>).</summary>
public sealed record ListVariationOrdersQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<VariationOrderDto>>>;
