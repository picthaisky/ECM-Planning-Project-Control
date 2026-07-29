using CMPlus.Application.Abstractions;
using CMPlus.Application.Import;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Import.Queries.GetImportJob;

public sealed class GetImportJobQueryHandler(IImportRepository repository)
    : IRequestHandler<GetImportJobQuery, Result<FileImportJobDto>>
{
    public async Task<Result<FileImportJobDto>> Handle(GetImportJobQuery request, CancellationToken cancellationToken)
    {
        var job = await repository.FindJobAsync(request.JobId, cancellationToken);

        // A job that exists but belongs to a different project is treated identically to "not
        // found" - the tenant scoping (ADR-0002) already prevents cross-tenant leakage; this
        // closes the analogous cross-project gap within the same tenant.
        if (job is null || job.ProjectId != request.ProjectId)
        {
            return Result<FileImportJobDto>.Failure(ImportErrorCodes.JobNotFound);
        }

        return Result<FileImportJobDto>.Success(FileImportJobDto.From(job));
    }
}
