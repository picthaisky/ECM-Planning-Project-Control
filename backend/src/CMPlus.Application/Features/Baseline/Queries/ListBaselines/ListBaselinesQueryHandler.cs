using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Baseline.Commands.CaptureBaseline;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Baseline.Queries.ListBaselines;

/// <summary>
/// Never 404s on an unknown/cross-tenant/other-project <c>ProjectId</c> - returns an empty list, the
/// established list-read precedent in this codebase (<c>ListVariationOrdersQueryHandler</c>'s remarks
/// apply verbatim: the global tenant filter plus this query's own <c>ProjectId</c> scoping already
/// make every "nothing here" case produce the identical empty result, so there is nothing to leak).
/// </summary>
public sealed class ListBaselinesQueryHandler(IBaselineRepository repository)
    : IRequestHandler<ListBaselinesQuery, Result<IReadOnlyList<BaselineDto>>>
{
    public async Task<Result<IReadOnlyList<BaselineDto>>> Handle(
        ListBaselinesQuery request, CancellationToken cancellationToken)
    {
        var rows = await repository.ListByProjectAsync(request.ProjectId, cancellationToken);

        var dtos = rows
            .Select(r => new BaselineDto(
                r.Id, r.ProjectId, r.Name, r.IsActive, r.CapturedAt, r.CapturedByUserId, r.Bac, r.ActivityCount))
            .ToList();

        return Result<IReadOnlyList<BaselineDto>>.Success(dtos);
    }
}
