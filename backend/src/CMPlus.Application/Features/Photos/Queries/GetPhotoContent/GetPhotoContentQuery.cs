using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Photos.Queries.GetPhotoContent;

/// <summary><c>GET /api/v1/projects/{projectId}/photos/{photoId}</c> (S12-BE-01) - streams a
/// photo's scrubbed bytes through the API; never a static-file passthrough (S12-BE-01 DoD).</summary>
public sealed record GetPhotoContentQuery(Guid ProjectId, Guid PhotoId) : IRequest<Result<PhotoContentDto>>;

/// <summary><see cref="Content"/> is the exact bytes stored (already EXIF-scrubbed at upload time) -
/// <see cref="ContentType"/> is <see cref="CMPlus.Domain.Entities.Photo.ContentType"/>'s computed
/// projection, never a stored free-text value (S12-SEC-01: "restrictive Content-Type on serve").</summary>
public sealed record PhotoContentDto(byte[] Content, string ContentType, string FileName);
