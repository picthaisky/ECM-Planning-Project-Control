using CMPlus.Application.Abstractions;
using CMPlus.Application.Import;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Import.Commands.ImportExcelProgress;

/// <summary>See <c>ImportScheduleFileCommandHandler</c>'s remarks on the "a rejected file is still
/// a successful `Result`, carried as a Failed job" modeling decision - identical policy here.</summary>
public sealed class ImportExcelProgressCommandHandler(
    IImportRepository repository,
    IExcelProgressImporter importer,
    IImportOptionsProvider importOptions,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ImportExcelProgressCommand, Result<FileImportJobDto>>
{
    public async Task<Result<FileImportJobDto>> Handle(ImportExcelProgressCommand request, CancellationToken cancellationToken)
    {
        if (!await repository.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<FileImportJobDto>.Failure(ImportErrorCodes.ProjectNotFound);
        }

        // Security review sprint-15.md L-01b: fail closed on a null actor id rather than fabricating
        // Guid.Empty - structurally unreachable behind [Authorize] but never trusted here either.
        if (currentUser.UserId is not { } actorUserId)
        {
            return Result<FileImportJobDto>.Failure(ImportErrorCodes.ActorRequired);
        }

        var job = new FileImportJob(
            tenantProvider.TenantId, request.ProjectId, request.FileName, ImportFileFormat.Excel,
            actorUserId, clock.UtcNow);

        // S3-SEC-01 M-02: see ImportScheduleFileCommandHandler's identical remark - compared against
        // DeclaredContentLength (the real IFormFile.Length) when the controller supplies one.
        var effectiveContentLength = request.DeclaredContentLength ?? request.Content.LongLength;
        if (effectiveContentLength > importOptions.MaxFileSizeBytes)
        {
            return await FailJobAsync(
                job,
                new ImportErrorDetail(
                    ImportErrorCodes.FileTooLarge,
                    $"File size {effectiveContentLength} bytes exceeds the configured cap of {importOptions.MaxFileSizeBytes} bytes."),
                cancellationToken);
        }

        // S3-SEC-01 DoD item 2 / M-01 trigger #1: verify the upload is actually a ZIP (OOXML)
        // container before ExcelPackage is ever constructed - closes the one format whose parser
        // does NOT degrade cleanly on a disguised/non-OOXML input (System.IO.InvalidDataException).
        if (!FileSignatureValidator.IsXlsx(request.Content))
        {
            return await FailJobAsync(
                job,
                new ImportErrorDetail(
                    ImportErrorCodes.FormatMismatch,
                    "The file's content does not match the expected XLSX (ZIP/OOXML) format."),
                cancellationToken);
        }

        using var stream = new MemoryStream(request.Content, writable: false);

        Result<IReadOnlyList<ParsedProgressRow>> parseResult;
        try
        {
            parseResult = importer.Parse(stream);
        }
        catch (Exception)
        {
            // S3-SEC-01 M-01: final safety net - see ImportScheduleFileCommandHandler's identical
            // remark. Covers, among others, a password-protected workbook that still happens to carry
            // a PK signature EPPlus itself cannot open.
            return await FailJobAsync(
                job,
                new ImportErrorDetail(ImportErrorCodes.MalformedFile, "The file could not be parsed."),
                cancellationToken);
        }

        if (parseResult.IsFailure)
        {
            return await FailJobAsync(job, ImportErrorDetail.FromResultError(parseResult.Error), cancellationToken);
        }

        var rows = parseResult.Value;
        var activityIds = rows.Select(r => r.ActivityId).Distinct().ToArray();
        var activities = await repository.LoadActivitiesForProjectAsync(request.ProjectId, activityIds, cancellationToken);

        var unknownRows = rows.Where(r => !activities.ContainsKey(r.ActivityId)).ToList();
        if (unknownRows.Count > 0)
        {
            var rowNumbers = string.Join(", ", unknownRows.Select(r => r.SourceRowNumber));
            return await FailJobAsync(
                job,
                new ImportErrorDetail(
                    ImportErrorCodes.UnknownActivity,
                    $"Row(s) {rowNumbers} reference an ActivityId that does not belong to this project."),
                cancellationToken);
        }

        // US-1.2/ADR-0009: every row goes through Activity.RecordProgress - this is the only path
        // that may append an ActivityProgressLog entry; no code here writes ProgressPercentage directly.
        var newEntries = new List<ActivityProgressLog>(rows.Count);
        foreach (var row in rows)
        {
            var activity = activities[row.ActivityId];
            var entry = activity.RecordProgress(
                row.PeriodEndDate, row.ProgressPercentage, row.ActualQuantity, actorUserId, ProgressSource.Import, clock.UtcNow);
            newEntries.Add(entry);
        }

        job.MarkSucceeded(newEntries.Count, clock.UtcNow);
        await repository.SaveProgressImportAsync(job, newEntries, cancellationToken);

        return Result<FileImportJobDto>.Success(FileImportJobDto.From(job));
    }

    private async Task<Result<FileImportJobDto>> FailJobAsync(
        FileImportJob job, ImportErrorDetail detail, CancellationToken cancellationToken)
    {
        job.MarkFailed(detail.ToJson(), clock.UtcNow);
        await repository.SaveFailedJobAsync(job, cancellationToken);
        return Result<FileImportJobDto>.Success(FileImportJobDto.From(job));
    }
}
