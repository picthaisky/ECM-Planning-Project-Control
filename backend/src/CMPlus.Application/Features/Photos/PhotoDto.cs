using CMPlus.Domain.Entities;

namespace CMPlus.Application.Features.Photos;

/// <summary>Read shape for a <see cref="Photo"/> (S12-BE-01). Deliberately does NOT include
/// <see cref="Photo.StorageKey"/> - that field is an internal storage detail, never returned to a
/// client (S12-BE-01 DoD: "serving must not be a static-file passthrough" - the client always
/// fetches content through <c>GET .../photos/{photoId}</c>, never by learning the storage key).</summary>
public sealed record PhotoDto(
    Guid Id,
    Guid ProjectId,
    Guid? ActivityId,
    string? Caption,
    string ContentType,
    long FileSizeBytes,
    Guid UploadedByUserId,
    DateTimeOffset UploadedAt,
    DateTimeOffset CapturedAt)
{
    public static PhotoDto From(Photo photo) => new(
        photo.Id,
        photo.ProjectId,
        photo.ActivityId,
        photo.Caption,
        photo.ContentType,
        photo.FileSizeBytes,
        photo.UploadedByUserId,
        photo.UploadedAt,
        photo.CapturedAt);
}
