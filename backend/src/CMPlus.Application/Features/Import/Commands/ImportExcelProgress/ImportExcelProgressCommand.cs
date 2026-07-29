using CMPlus.Application.Import;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Import.Commands.ImportExcelProgress;

/// <summary>S3-BE-03/04, US-3.3: imports a re-uploaded progress-update Excel template. Every row
/// creates an <c>ActivityProgressLog</c> entry via <c>Activity.RecordProgress</c> - never a direct
/// write to the cached <c>Activity.ProgressPercentage</c> (ADR-0009).</summary>
/// <param name="DeclaredContentLength">See <c>ImportScheduleFileCommand</c>'s remarks on this same
/// field (S3-SEC-01 finding M-02) - identical purpose/contract here.</param>
public sealed record ImportExcelProgressCommand(Guid ProjectId, string FileName, byte[] Content, long? DeclaredContentLength = null)
    : IRequest<Result<FileImportJobDto>>;
