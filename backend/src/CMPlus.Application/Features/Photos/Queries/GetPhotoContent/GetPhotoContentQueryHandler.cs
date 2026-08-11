using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CMPlus.Application.Features.Photos.Queries.GetPhotoContent;

/// <summary>
/// S12-BE-01 serve path. Tenant scoping comes from <see cref="IPhotoRepository.FindAsync"/>'s
/// ambient global query filter (ADR-0002); this handler additionally checks
/// <see cref="CMPlus.Domain.Entities.Photo.ProjectId"/> against the route's <c>ProjectId</c> so a
/// cross-tenant id, an unknown id, and a real id from a *different project in the same tenant* are
/// all indistinguishable - one bare <see cref="PhotoErrorCodes.NotFound"/> (S12-SEC-01: "photo URLs
/// must not be guessable in a way that permits cross-tenant access" - this is the narrower same-
/// tenant-wrong-project case, closed at no extra cost since the route already carries a project id).
/// </summary>
public sealed class GetPhotoContentQueryHandler(
    IPhotoRepository repository, IFileStorage fileStorage, ILogger<GetPhotoContentQueryHandler> logger)
    : IRequestHandler<GetPhotoContentQuery, Result<PhotoContentDto>>
{
    public async Task<Result<PhotoContentDto>> Handle(GetPhotoContentQuery request, CancellationToken cancellationToken)
    {
        var photo = await repository.FindAsync(request.PhotoId, cancellationToken);
        if (photo is null || photo.ProjectId != request.ProjectId)
        {
            return Result<PhotoContentDto>.Failure(PhotoErrorCodes.NotFound);
        }

        Stream stream;
        try
        {
            stream = await fileStorage.OpenReadAsync(photo.StorageKey, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // Security review sprint-12.md L-04: the Photo row exists but its blob is missing from
            // storage (e.g. M-03's Storage:LocalRootPath defaulting to an ephemeral temp directory).
            // That is a server-side data-integrity gap, never the caller's fault - report it with the
            // same shape "unknown id" already uses (never let the raw FileNotFoundException escape
            // to GlobalExceptionHandler as an opaque 500). Logged at Warning with the PHOTO ID, never
            // the storage key, so the log itself does not become a second place the key leaks.
            logger.LogWarning("Photo {PhotoId} row exists but its stored content is missing.", photo.Id);
            return Result<PhotoContentDto>.Failure(PhotoErrorCodes.NotFound);
        }

        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);

            // The file's own extension is derived server-side from Photo.ContentType, never from any
            // client-supplied original filename (this entity does not even store one) - the download
            // name cannot carry attacker-controlled content.
            var extension = photo.ContentType == "image/png" ? "png" : "jpg";
            return Result<PhotoContentDto>.Success(new PhotoContentDto(buffer.ToArray(), photo.ContentType, $"{photo.Id:N}.{extension}"));
        }
    }
}
