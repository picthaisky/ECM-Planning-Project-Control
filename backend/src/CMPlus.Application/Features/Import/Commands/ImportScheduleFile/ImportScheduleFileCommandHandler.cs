using CMPlus.Application.Abstractions;
using CMPlus.Application.Import;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Import.Commands.ImportScheduleFile;

/// <summary>
/// S3-BE-01/02/04 orchestration. Deliberately format-agnostic beyond picking which
/// <see cref="IXerScheduleParser"/>/<see cref="IMspdiScheduleParser"/> to call - both produce the
/// same <see cref="ParsedSchedule"/> shape, so everything downstream (size cap, job bookkeeping,
/// bulk persistence) is shared.
///
/// A rejection (oversized file, malformed file, a relation cycle) is a legitimate outcome of
/// "processing this import", not a request-level error: this handler still returns
/// <see cref="Result{T}.Success"/> with a <see cref="FileImportJobDto"/> whose
/// <see cref="FileImportJobDto.Status"/> is <see cref="ImportJobStatus.Failed"/> and
/// <see cref="FileImportJobDto.ErrorJson"/> carries the reason - this is a deliberate modeling
/// decision (docs/10 §6 Sprint 3's DoD asks for queryable job history, not an HTTP-level failure
/// per rejected file) so a bad upload always gets a job id back and shows up in history exactly
/// like a good one. <see cref="Result{T}.Failure"/> is reserved for requests that never produced a
/// job at all (an unknown project, an unsupported <c>Format</c>).
/// </summary>
public sealed class ImportScheduleFileCommandHandler(
    IImportRepository repository,
    IXerScheduleParser xerParser,
    IMspdiScheduleParser mspdiParser,
    IImportOptionsProvider importOptions,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ImportScheduleFileCommand, Result<FileImportJobDto>>
{
    public async Task<Result<FileImportJobDto>> Handle(ImportScheduleFileCommand request, CancellationToken cancellationToken)
    {
        if (request.Format is not (ImportFileFormat.Xer or ImportFileFormat.Mspdi))
        {
            return Result<FileImportJobDto>.Failure(ImportErrorCodes.UnsupportedFormat);
        }

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
            tenantProvider.TenantId, request.ProjectId, request.FileName, request.Format,
            actorUserId, clock.UtcNow);

        // S3-BE-01 DoD: a file exceeding the configured size cap is rejected BEFORE parsing begins
        // - nothing below this point (the parser, the change tracker) ever sees the content.
        // S3-SEC-01 M-02: compared against DeclaredContentLength (the real IFormFile.Length) when the
        // controller supplies one - it never buffers an oversized upload's real bytes, so
        // request.Content.LongLength alone would under-report the size in that case.
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

        // S3-SEC-01 DoD item 2 / M-01 trigger #1's root cause: verify the upload's actual leading
        // bytes match the format implied by the route BEFORE any parser is invoked - a mismatched
        // file (e.g. an .xlsx posted to the xer route) is rejected as a modelled Failed job here,
        // never left for whichever parser happens to run to discover it, possibly by throwing.
        var formatMatchesContent = request.Format == ImportFileFormat.Xer
            ? FileSignatureValidator.IsXer(request.Content)
            : FileSignatureValidator.IsMspdi(request.Content);

        if (!formatMatchesContent)
        {
            return await FailJobAsync(
                job,
                new ImportErrorDetail(
                    ImportErrorCodes.FormatMismatch,
                    $"The file's content does not match the expected {request.Format} format."),
                cancellationToken);
        }

        using var stream = new MemoryStream(request.Content, writable: false);

        Result<ParsedSchedule> parseResult;
        try
        {
            parseResult = request.Format == ImportFileFormat.Xer
                ? xerParser.Parse(stream, tenantProvider.TenantId, request.ProjectId)
                : mspdiParser.Parse(stream, tenantProvider.TenantId, request.ProjectId);
        }
        catch (Exception)
        {
            // S3-SEC-01 M-01: a final safety net. Every currently-known throwing case is guarded
            // upstream in the parsers themselves (bounds-checked casts) - this exists for anything
            // unforeseen, so it still lands as a queryable, audited Failed job (never a raw 500) and
            // never echoes the exception's own message/stack trace into ErrorJson (conventions.md:
            // "never leak stack traces or SQL").
            return await FailJobAsync(
                job,
                new ImportErrorDetail(ImportErrorCodes.MalformedFile, "The file could not be parsed."),
                cancellationToken);
        }

        if (parseResult.IsFailure)
        {
            return await FailJobAsync(job, ImportErrorDetail.FromResultError(parseResult.Error), cancellationToken);
        }

        job.MarkSucceeded(parseResult.Value.RowCount, clock.UtcNow);
        await repository.SaveScheduleImportAsync(job, parseResult.Value, cancellationToken);

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
