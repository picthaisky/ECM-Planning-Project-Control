using CMPlus.Domain.Enums;

namespace CMPlus.Application.Photos;

/// <summary>
/// Magic-byte detection for photo uploads (S12-BE-01 DoD: "validate magic bytes, not the extension
/// or Content-Type - both are attacker-controlled"). Deliberately the ONLY thing that decides
/// <see cref="PhotoImageFormat"/> for a new <c>Photo</c> - the client-supplied filename and
/// multipart <c>Content-Type</c> header are read by the controller purely for diagnostics/logging,
/// never passed into this method or trusted anywhere on the write path (mirrors
/// <c>CMPlus.Application.Import.FileSignatureValidator</c>'s identical role for the schedule/Excel
/// import path, S3-SEC-01 DoD item 2).
/// </summary>
public static class ImageSignatureValidator
{
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Returns <see langword="null"/> when the content's leading bytes match neither
    /// supported signature - the caller (<c>UploadPhotoCommandHandler</c>) rejects the upload as
    /// <c>UnsupportedFormat</c> rather than guessing or falling back to the filename/Content-Type.</summary>
    public static PhotoImageFormat? DetectFormat(byte[] content)
    {
        if (content.Length >= JpegSignature.Length && content.AsSpan(0, JpegSignature.Length).SequenceEqual(JpegSignature))
        {
            return PhotoImageFormat.Jpeg;
        }

        if (content.Length >= PngSignature.Length && content.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            return PhotoImageFormat.Png;
        }

        return null;
    }
}
