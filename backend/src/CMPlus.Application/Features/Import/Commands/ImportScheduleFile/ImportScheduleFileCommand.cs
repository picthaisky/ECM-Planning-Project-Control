using CMPlus.Application.Import;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Import.Commands.ImportScheduleFile;

/// <summary>
/// S3-BE-01/02/04: imports a Primavera P6 <c>.XER</c> or MS Project MSPDI XML schedule export into
/// a project's WBS/Activity/Relation tables. <see cref="Format"/> must be
/// <see cref="ImportFileFormat.Xer"/> or <see cref="ImportFileFormat.Mspdi"/> - the Excel progress
/// path is a separate command (<c>ImportExcelProgressCommand</c>) since it writes a different
/// target (<c>ActivityProgressLog</c>, not the schedule network).
/// </summary>
/// <param name="DeclaredContentLength">Sprint 3 security review finding M-02: the upload's true byte
/// length, known from <c>IFormFile.Length</c> before any body byte is read. When the controller has
/// already determined (from this same length) that the file exceeds the configured cap, it never
/// buffers the real content at all and passes a single placeholder byte as <paramref name="Content"/>
/// instead - so the handler's size-cap check must compare against this declared length, not
/// <c>Content.LongLength</c>, whenever it is present. <c>null</c> (the default, used by every
/// existing caller/test) means "trust <c>Content.LongLength</c>", preserving prior behaviour.</param>
public sealed record ImportScheduleFileCommand(
    Guid ProjectId, ImportFileFormat Format, string FileName, byte[] Content, long? DeclaredContentLength = null)
    : IRequest<Result<FileImportJobDto>>;
