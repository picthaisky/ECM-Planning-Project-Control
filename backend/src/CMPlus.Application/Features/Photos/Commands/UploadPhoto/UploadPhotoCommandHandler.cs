using CMPlus.Application.Abstractions;
using CMPlus.Application.Photos;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using MediatR;

namespace CMPlus.Application.Features.Photos.Commands.UploadPhoto;

/// <summary>
/// S12-BE-01 orchestration: validate -&gt; detect format from magic bytes -&gt; strip EXIF -&gt;
/// persist through <see cref="IFileStorage"/> -&gt; persist the <see cref="Photo"/> row. Every
/// existence/actor check happens before any byte of the upload is processed, mirroring
/// <c>RecordWeatherLogCommandHandler</c>'s "check everything, then mutate" ordering - and, unlike
/// the import pipeline's deliberately-always-succeeds-with-a-Failed-job design, a rejected photo
/// upload IS a request-level failure here (there is no "job history" concept for a single photo -
/// the client either gets the stored photo back or a clear reason it was rejected).
/// </summary>
public sealed class UploadPhotoCommandHandler(
    IPhotoRepository repository,
    IPhotoOptionsProvider options,
    IFileStorage fileStorage,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<UploadPhotoCommand, Result<PhotoDto>>
{
    public async Task<Result<PhotoDto>> Handle(UploadPhotoCommand request, CancellationToken cancellationToken)
    {
        if (!await repository.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<PhotoDto>.Failure(PhotoErrorCodes.ProjectNotFound);
        }

        // Sprint 10 L-01 fix pattern (this task's brief): fail closed on a null actor id rather
        // than fabricating Guid.Empty.
        if (currentUser.UserId is not { } uploadedByUserId)
        {
            return Result<PhotoDto>.Failure(PhotoErrorCodes.ActorRequired);
        }

        if (request.ActivityId is { } activityId
            && !await repository.ActivityExistsAsync(request.ProjectId, activityId, cancellationToken))
        {
            return Result<PhotoDto>.Failure(PhotoErrorCodes.UnknownActivity);
        }

        if (request.Content.Length == 0)
        {
            return Result<PhotoDto>.Failure(PhotoErrorCodes.FileRequired);
        }

        // S12-BE-01 DoD: "enforce a size limit" - checked here as defense-in-depth even though the
        // controller already checked the declared length before buffering (this handler's own
        // request.Content is only ever the true bytes when the controller decided to buffer them at
        // all - see UploadPhotoCommand's remarks).
        //
        // Security review sprint-12.md L-01: this used to be `request.DeclaredContentLength ??
        // request.Content.LongLength` - i.e. it trusted a CALLER-SUPPLIED number over the actual
        // buffer, so an 11 MiB payload declared as 5 bytes sailed straight past the cap. A
        // defense-in-depth check that trusts the caller's own claim about size is not defense in
        // depth. Math.Max means the larger of "what the caller claims" and "what is actually there"
        // always wins, so neither a lying declared length nor a lying buffer can under-report size.
        var effectiveContentLength = Math.Max(request.DeclaredContentLength ?? 0, request.Content.LongLength);
        if (effectiveContentLength > options.MaxFileSizeBytes)
        {
            return Result<PhotoDto>.Failure(PhotoErrorCodes.FileTooLarge);
        }

        // S12-BE-01 DoD: "validate magic bytes, not the extension or Content-Type - both are
        // attacker-controlled". The client-supplied filename/Content-Type never reach this handler
        // at all (UploadPhotoCommand carries neither).
        if (ImageSignatureValidator.DetectFormat(request.Content) is not { } format)
        {
            return Result<PhotoDto>.Failure(PhotoErrorCodes.UnsupportedFormat);
        }

        byte[] scrubbedContent;
        try
        {
            // S12-BE-01 DoD: EXIF (GPS/device identifiers) removed BEFORE anything is stored - the
            // original, un-scrubbed bytes (`request.Content`) are never passed to IFileStorage or
            // persisted anywhere from this point on.
            scrubbedContent = ExifScrubber.Strip(request.Content, format);
        }
        catch (ImageProcessingException)
        {
            return Result<PhotoDto>.Failure(PhotoErrorCodes.MalformedImage);
        }

        var photo = new Photo(
            tenantProvider.TenantId,
            request.ProjectId,
            request.ActivityId,
            request.Caption,
            format,
            scrubbedContent.LongLength,
            uploadedByUserId,
            clock.UtcNow,
            request.CapturedAt);

        await using (var scrubbedStream = new MemoryStream(scrubbedContent, writable: false))
        {
            await fileStorage.SaveAsync(photo.StorageKey, scrubbedStream, photo.ContentType, cancellationToken);
        }

        repository.Add(photo);
        await repository.SaveChangesAsync(cancellationToken);

        return Result<PhotoDto>.Success(PhotoDto.From(photo));
    }
}
