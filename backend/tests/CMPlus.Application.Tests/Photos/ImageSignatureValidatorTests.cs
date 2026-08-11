using CMPlus.Application.Photos;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Photos;

/// <summary>
/// S12-BE-01 DoD: pure unit tests for the magic-byte gate itself - "validate magic bytes, not the
/// extension or Content-Type", mirroring <c>FileSignatureValidatorTests</c>' identical role for the
/// import pipeline.
/// </summary>
public class ImageSignatureValidatorTests
{
    [Fact]
    public void DetectFormat_Recognises_A_Jpeg_By_Its_Magic_Bytes()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
        Assert.Equal(PhotoImageFormat.Jpeg, ImageSignatureValidator.DetectFormat(jpeg));
    }

    [Fact]
    public void DetectFormat_Recognises_A_Png_By_Its_Magic_Bytes()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
        Assert.Equal(PhotoImageFormat.Png, ImageSignatureValidator.DetectFormat(png));
    }

    // ------------------------------------------------------------------------------------
    // The mutation-evidence case this task's brief asked for by name: a file whose magic bytes
    // disagree with its extension/Content-Type is rejected. This validator never even sees the
    // extension/Content-Type (UploadPhotoCommand carries neither) - it can only ever answer from
    // the actual bytes, which is the point.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void DetectFormat_Returns_Null_For_An_Xlsx_Zip_Disguised_As_A_Photo()
    {
        // The exact XLSX local-file-header signature FileSignatureValidatorTests already uses for
        // the mirror-image attack on the import pipeline - here simulating a malicious upload whose
        // filename/Content-Type claim "photo.jpg" but whose real bytes are a ZIP container.
        byte[] xlsxMagicBytes = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00];
        Assert.Null(ImageSignatureValidator.DetectFormat(xlsxMagicBytes));
    }

    [Fact]
    public void DetectFormat_Returns_Null_For_An_Html_Payload_Disguised_As_A_Photo()
    {
        // The classic "stored XSS via image upload" attack shape: an attacker names the file
        // photo.jpg / sets Content-Type: image/jpeg, but the real bytes are an HTML document that
        // would render and execute script if a server ever served it back with a permissive
        // Content-Type. Rejected purely on the mismatched magic bytes.
        var htmlBytes = System.Text.Encoding.UTF8.GetBytes("<html><body><script>alert(1)</script></body></html>");
        Assert.Null(ImageSignatureValidator.DetectFormat(htmlBytes));
    }

    [Fact]
    public void DetectFormat_Returns_Null_For_Content_Shorter_Than_Either_Signature()
    {
        byte[] tooShort = [0xFF, 0xD8];
        Assert.Null(ImageSignatureValidator.DetectFormat(tooShort));
    }

    [Fact]
    public void DetectFormat_Returns_Null_For_Empty_Content()
    {
        Assert.Null(ImageSignatureValidator.DetectFormat([]));
    }
}
