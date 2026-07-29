using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Import;

/// <summary>Wire shape for a <see cref="FileImportJob"/> (S3-BE-04) - returned by the import
/// endpoints and the history/detail queries alike, so a client polling history sees exactly the
/// same shape it got back from the original <c>POST</c>.</summary>
public sealed record FileImportJobDto(
    Guid Id,
    Guid ProjectId,
    string FileName,
    ImportFileFormat Format,
    ImportJobStatus Status,
    int RowsImported,
    string? ErrorJson,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    Guid CreatedByUserId)
{
    public static FileImportJobDto From(FileImportJob job) => new(
        job.Id, job.ProjectId, job.FileName, job.Format, job.Status,
        job.RowsImported, job.ErrorJson, job.StartedAt, job.FinishedAt, job.CreatedByUserId);
}
