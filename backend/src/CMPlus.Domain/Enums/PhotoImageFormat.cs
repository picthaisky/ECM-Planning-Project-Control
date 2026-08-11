namespace CMPlus.Domain.Enums;

/// <summary>
/// The closed set of image formats <see cref="Entities.Photo"/> accepts (S12-BE-01, US-12.1).
/// Deliberately not an open-ended content-type string: the value here is always derived
/// server-side from the upload's actual magic bytes (<c>ImageSignatureValidator</c>), never from
/// the client-supplied filename/<c>Content-Type</c> header (both attacker-controlled) - so the
/// set of values this enum can ever hold is exactly the set of formats this codebase knows how to
/// verify and EXIF-scrub (<c>ExifScrubber</c>). Adding a third format is a deliberate, reviewed
/// change to both of those, not a data-entry decision.
/// </summary>
public enum PhotoImageFormat
{
    Jpeg = 1,
    Png = 2,
}
