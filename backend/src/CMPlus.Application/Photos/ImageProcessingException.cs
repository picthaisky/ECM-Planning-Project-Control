namespace CMPlus.Application.Photos;

/// <summary>
/// Thrown by <see cref="ExifScrubber"/> when a supposedly-JPEG/PNG upload's internal segment/chunk
/// structure is inconsistent (truncated, an out-of-range declared length, a PNG with no terminating
/// <c>IEND</c>) after its magic bytes already passed <see cref="ImageSignatureValidator"/>. Caught
/// by <c>UploadPhotoCommandHandler</c> and turned into a modelled <c>MalformedImage</c> failure -
/// deliberately fail-closed (S12-BE-01's brief: EXIF must be stripped BEFORE storing, so if this
/// code cannot even parse the structure well enough to be sure it removed every metadata-bearing
/// segment, the safe answer is to refuse the upload, never to store the un-scrubbed original or a
/// best-effort partial scrub).
/// </summary>
public sealed class ImageProcessingException(string message) : Exception(message);
