using CMPlus.Application.Abstractions;
using MediatR;

namespace CMPlus.Application.Features.Progress.Queries.GetProgressAsOf;

public sealed class GetProgressAsOfQueryHandler(IActivityProgressReader reader)
    : IRequestHandler<GetProgressAsOfQuery, decimal>
{
    public Task<decimal> Handle(GetProgressAsOfQuery request, CancellationToken cancellationToken) =>
        reader.GetProgressAsOfAsync(request.ActivityId, request.AsOf, cancellationToken);
}
