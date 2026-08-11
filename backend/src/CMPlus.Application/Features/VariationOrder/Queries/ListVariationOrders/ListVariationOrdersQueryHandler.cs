using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Queries.ListVariationOrders;

/// <summary>Never 404s on an unknown/cross-tenant/other-project <c>ProjectId</c> - returns an empty
/// list instead, the established precedent for list reads in this codebase
/// (<c>ListPaymentCertificatesQueryHandler</c>'s own remarks apply verbatim here: the global tenant
/// filter plus this query's own ProjectId scoping already make every "nothing here" case produce the
/// identical empty result, so there is nothing to leak).</summary>
public sealed class ListVariationOrdersQueryHandler(IVariationOrderRepository repository)
    : IRequestHandler<ListVariationOrdersQuery, Result<IReadOnlyList<VariationOrderDto>>>
{
    public async Task<Result<IReadOnlyList<VariationOrderDto>>> Handle(
        ListVariationOrdersQuery request, CancellationToken cancellationToken)
    {
        var rows = await repository.ListByProjectAsync(request.ProjectId, cancellationToken);

        return Result<IReadOnlyList<VariationOrderDto>>.Success(rows.Select(VariationOrderDto.From).ToList());
    }
}
