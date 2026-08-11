using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Queries.GetVariationOrder;

/// <summary>Tenant-scoped via the global EF query filter (ADR-0002) - a cross-tenant/nonexistent id
/// is a bare 404 (<see cref="VariationOrderErrorCodes.NotFound"/>), identical discipline to
/// <c>GetPaymentCertificateQueryHandler</c>.</summary>
public sealed class GetVariationOrderQueryHandler(IVariationOrderRepository repository)
    : IRequestHandler<GetVariationOrderQuery, Result<VariationOrderDto>>
{
    public async Task<Result<VariationOrderDto>> Handle(GetVariationOrderQuery request, CancellationToken cancellationToken)
    {
        var variationOrder = await repository.GetByIdAsync(request.VariationOrderId, cancellationToken);
        if (variationOrder is null)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.NotFound);
        }

        return Result<VariationOrderDto>.Success(VariationOrderDto.From(variationOrder));
    }
}
