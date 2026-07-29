using CMPlus.Application.Import;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Import.Queries.GetImportJob;

/// <summary>S3-BE-04: single job status/detail lookup for the import history screen.</summary>
public sealed record GetImportJobQuery(Guid ProjectId, Guid JobId) : IRequest<Result<FileImportJobDto>>;
