using CMPlus.Application.Import;
using MediatR;

namespace CMPlus.Application.Features.Import.Queries.GetImportJobHistory;

/// <summary>S3-BE-04: paged, newest-first job history for a project (docs/db-conventions.md §6 -
/// "page everything that can exceed ~200 rows"). Always succeeds with an empty list rather than a
/// <c>Result</c> failure - a project with no import history yet is not an error.</summary>
public sealed record GetImportJobHistoryQuery(Guid ProjectId, int Page = 1, int PageSize = 20)
    : IRequest<IReadOnlyList<FileImportJobDto>>;
