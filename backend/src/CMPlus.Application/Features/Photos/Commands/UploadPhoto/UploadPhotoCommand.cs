using CMPlus.Application.Features.Photos;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Photos.Commands.UploadPhoto;

/// <summary>
/// <c>POST /api/v1/projects/{projectId}/photos</c> (S12-BE-01, US-12.1) - multipart photo upload
/// through <c>IFileStorage</c> (ADR-0010). <see cref="Content"/> is the raw upload bytes; the
/// controller has already applied the pre-buffering size check against the declared
/// <c>IFormFile.Length</c> (S12-BE-01 DoD: "enforce it before buffering the whole thing into
/// memory") before this command is even constructed. <see cref="DeclaredContentLength"/> mirrors
/// <c>ImportScheduleFileCommand</c>'s identical field: the true byte length known from
/// <c>IFormFile.Length</c>, compared against the cap here as defense-in-depth even when
/// <see cref="Content"/> itself was replaced with a placeholder because the controller already
/// determined it was oversized. <see cref="Format"/>/<c>ContentType</c> are deliberately NOT
/// fields here - they are derived server-side from magic bytes inside the handler, never trusted
/// from the client-supplied filename/header (S12-BE-01 DoD).
/// </summary>
public sealed record UploadPhotoCommand(
    Guid ProjectId,
    Guid? ActivityId,
    string? Caption,
    DateTimeOffset? CapturedAt,
    byte[] Content,
    long? DeclaredContentLength = null) : IRequest<Result<PhotoDto>>;
