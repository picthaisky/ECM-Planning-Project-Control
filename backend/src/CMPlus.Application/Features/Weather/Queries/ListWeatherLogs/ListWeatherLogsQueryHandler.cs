using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Weather.Queries.ListWeatherLogs;

/// <summary>
/// Never 404s on an unknown/cross-tenant/other-project <c>ProjectId</c> - returns an empty list
/// instead, the established precedent for list reads in this codebase
/// (<c>ListVariationOrdersQueryHandler</c>/<c>ListEvmSnapshotsQueryHandler</c>'s own remarks apply
/// verbatim here: the global tenant filter plus this query's own ProjectId scoping already make
/// every "nothing here" case produce the identical empty result, so there is nothing to leak).
/// </summary>
public sealed class ListWeatherLogsQueryHandler(IDailyWeatherLogRepository repository)
    : IRequestHandler<ListWeatherLogsQuery, Result<IReadOnlyList<WeatherLogDto>>>
{
    public async Task<Result<IReadOnlyList<WeatherLogDto>>> Handle(
        ListWeatherLogsQuery request, CancellationToken cancellationToken)
    {
        // Mirrors ListEvmSnapshotsQueryHandler/GetCashFlowQueryHandler's identical handler-level
        // (not FluentValidation) range check - ValidationBehavior only ever surfaces a generic
        // "validation-error" bucket (joined message text, no per-field error code), so a distinctly
        // mapped ProblemDetails type needs a real Result.Failure(specific code) from the handler.
        if (request.From is not null && request.To is not null && request.From > request.To)
        {
            return Result<IReadOnlyList<WeatherLogDto>>.Failure(WeatherLogErrorCodes.InvalidDateRange);
        }

        var rows = await repository.ListByProjectAsync(request.ProjectId, request.From, request.To, cancellationToken);

        return Result<IReadOnlyList<WeatherLogDto>>.Success(rows.Select(WeatherLogDto.From).ToList());
    }
}
