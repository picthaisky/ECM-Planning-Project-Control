using CMPlus.Application.Abstractions;
using CMPlus.Application.Import;
using MediatR;

namespace CMPlus.Application.Features.Import.Queries.GetImportJobHistory;

public sealed class GetImportJobHistoryQueryHandler(IImportRepository repository)
    : IRequestHandler<GetImportJobHistoryQuery, IReadOnlyList<FileImportJobDto>>
{
    private const int MaxPageSize = 100;

    public async Task<IReadOnlyList<FileImportJobDto>> Handle(GetImportJobHistoryQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > MaxPageSize ? Math.Min(Math.Max(request.PageSize, 1), MaxPageSize) : request.PageSize;

        var jobs = await repository.GetJobHistoryAsync(request.ProjectId, page, pageSize, cancellationToken);

        return jobs.Select(FileImportJobDto.From).ToList();
    }
}
