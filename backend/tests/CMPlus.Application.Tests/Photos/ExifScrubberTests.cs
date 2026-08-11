using System.Text;
using CMPlus.Application.Photos;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Photos;

/// <summary>
/// S12-BE-01 DoD: "EXIF that carries GPS coordinates or device identifiers" must be gone BEFORE
/// storing. Every positive assertion here is against the STRIPPED OUTPUT'S RAW BYTES (searching for
/// the fixture's known ASCII markers with a plain substring scan) - never against "which code path
/// ran" - per this task's brief ("assert on the stored bytes, not on the code path").
/// </summary>
public class ExifScrubberTests
{
    private static bool ContainsAscii(byte[] content, string marker) =>
        IndexOfAscii(content, marker) >= 0;

    private static int IndexOfAscii(byte[] content, string marker)
    {
        var needle = Encoding.ASCII.GetBytes(marker);
        if (needle.Length == 0 || content.Length < needle.Length)
        {
            return -1;
        }

        for (var i = 0; i <= content.Length - needle.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (content[i + j] != needle[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return i;
            }
        }

        return -1;
    }

    // ------------------------------------------------------------------------------------
    // JPEG
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Strip_Jpeg_Genuinely_Removes_Gps_Device_And_Comment_Markers_From_The_Stored_Bytes()
    {
        var original = PhotoImageFixtures.JpegWithGpsExifAndDeviceInfo(
            out var gpsMarker, out var deviceMarker, out var commentMarker);

        // Sanity check on the fixture itself: the markers really are present before scrubbing -
        // otherwise the assertions below would pass vacuously (mutation-evidence discipline).
        Assert.True(ContainsAscii(original, gpsMarker));
        Assert.True(ContainsAscii(original, deviceMarker));
        Assert.True(ContainsAscii(original, commentMarker));
        Assert.True(ContainsAscii(original, "Exif"));
        Assert.True(ContainsAscii(original, "JFIF"));

        var stripped = ExifScrubber.Strip(original, PhotoImageFormat.Jpeg);

        Assert.False(ContainsAscii(stripped, gpsMarker));
        Assert.False(ContainsAscii(stripped, deviceMarker));
        Assert.False(ContainsAscii(stripped, commentMarker));
        Assert.False(ContainsAscii(stripped, "Exif"));
        Assert.False(ContainsAscii(stripped, "JFIF"));
    }

    [Fact]
    public void Strip_Jpeg_Preserves_The_Scan_Data_Byte_For_Byte_Including_Stuffed_And_Restart_Bytes()
    {
        var original = PhotoImageFixtures.JpegWithGpsExifAndDeviceInfo(out _, out _, out _);
        var stripped = ExifScrubber.Strip(original, PhotoImageFormat.Jpeg);

        // The scan data (after SOS) must survive completely untouched - a scrubber that
        // "recompressed" or reinterpreted entropy-coded bytes as markers would corrupt the image.
        byte[] expectedTail = [1, 0, 0, 0, 0, 0, 0x12, 0x34, 0xFF, 0x00, 0x56, 0xFF, 0xD0, 0x78, 0xFF, 0xD9];
        Assert.Equal(expectedTail, stripped[^expectedTail.Length..]);
    }

    [Fact]
    public void Strip_Jpeg_Starts_With_Soi_And_Ends_With_Eoi()
    {
        var original = PhotoImageFixtures.JpegWithGpsExifAndDeviceInfo(out _, out _, out _);
        var stripped = ExifScrubber.Strip(original, PhotoImageFormat.Jpeg);

        Assert.Equal(0xFF, stripped[0]);
        Assert.Equal(0xD8, stripped[1]);
        Assert.Equal(0xFF, stripped[^2]);
        Assert.Equal(0xD9, stripped[^1]);
    }

    [Fact]
    public void Strip_Jpeg_Is_A_Byte_Exact_No_Op_When_There_Is_No_Metadata_To_Remove()
    {
        var original = PhotoImageFixtures.JpegWithNoMetadata();
        var stripped = ExifScrubber.Strip(original, PhotoImageFormat.Jpeg);

        Assert.Equal(original, stripped);
    }

    [Fact]
    public void Strip_Jpeg_Shrinks_The_File_When_Metadata_Is_Removed()
    {
        var original = PhotoImageFixtures.JpegWithGpsExifAndDeviceInfo(out _, out _, out _);
        var stripped = ExifScrubber.Strip(original, PhotoImageFormat.Jpeg);

        Assert.True(stripped.Length < original.Length, $"Expected stripped ({stripped.Length}) < original ({original.Length}).");
    }

    [Fact]
    public void Strip_Jpeg_Throws_ImageProcessingException_When_A_Segment_Length_Overruns_The_File()
    {
        // SOI, then an APP1 marker declaring a length far larger than any bytes actually present -
        // a truncated/corrupted-upload shape that must fail closed, never silently truncate/ignore.
        byte[] truncated = [0xFF, 0xD8, 0xFF, 0xE1, 0xFF, 0xFF, 0x00, 0x01];

        Assert.Throws<ImageProcessingException>(() => ExifScrubber.Strip(truncated, PhotoImageFormat.Jpeg));
    }

    [Fact]
    public void Strip_Jpeg_Throws_ImageProcessingException_When_The_Soi_Marker_Is_Missing()
    {
        byte[] notAJpeg = [0x00, 0x01, 0x02];
        Assert.Throws<ImageProcessingException>(() => ExifScrubber.Strip(notAJpeg, PhotoImageFormat.Jpeg));
    }

    // ------------------------------------------------------------------------------------
    // H-01 (security review sprint-12.md): the scrubber must keep filtering PAST the first SOS, not
    // stop and copy the remainder of the file verbatim. Every fixture here places GPS-carrying
    // content in the exact region the pre-fix code never looked at again.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Strip_Jpeg_Removes_Gps_Exif_Placed_Immediately_After_The_Sos_Scan_Data()
    {
        var original = PhotoImageFixtures.JpegWithGpsExifAfterSos(out var gpsMarker);
        Assert.True(ContainsAscii(original, gpsMarker)); // fixture sanity check.

        var stripped = ExifScrubber.Strip(original, PhotoImageFormat.Jpeg);

        Assert.False(ContainsAscii(stripped, gpsMarker));
        Assert.False(ContainsAscii(stripped, "Exif"));
        // The scan data itself, including its stuffed/restart bytes, must survive untouched.
        Assert.Equal(0xFF, stripped[0]);
        Assert.Equal(0xD8, stripped[1]);
        Assert.Equal(0xFF, stripped[^2]);
        Assert.Equal(0xD9, stripped[^1]);
    }

    [Fact]
    public void Strip_Jpeg_Removes_Gps_Exif_Placed_Between_Two_Progressive_Scans()
    {
        var original = PhotoImageFixtures.JpegWithGpsExifBetweenTwoProgressiveScans(out var gpsMarker);
        Assert.True(ContainsAscii(original, gpsMarker));

        var stripped = ExifScrubber.Strip(original, PhotoImageFormat.Jpeg);

        Assert.False(ContainsAscii(stripped, gpsMarker));
        Assert.False(ContainsAscii(stripped, "Exif"));
        // Both scans' entropy-coded data must still be present, byte-for-byte, on either side of
        // where the stripped EXIF segment used to be.
        Assert.Contains<byte>(0xAA, stripped);
        Assert.Contains<byte>(0xDD, stripped);
        Assert.Equal(0xFF, stripped[^2]);
        Assert.Equal(0xD9, stripped[^1]);
    }

    [Fact]
    public void Strip_Jpeg_Discards_A_Second_Complete_Jpeg_Image_Appended_After_The_Primary_Eoi()
    {
        var original = PhotoImageFixtures.JpegWithSecondJpegAfterEoi(out var gpsMarker);
        Assert.True(ContainsAscii(original, gpsMarker));

        var stripped = ExifScrubber.Strip(original, PhotoImageFormat.Jpeg);

        Assert.False(ContainsAscii(stripped, gpsMarker));
        Assert.False(ContainsAscii(stripped, "Exif"));
        // Terminates at the primary image's own EOI - nothing of the secondary image survives.
        Assert.Equal(0xFF, stripped[^2]);
        Assert.Equal(0xD9, stripped[^1]);
    }

    [Fact]
    public void Strip_Jpeg_Discards_An_Arbitrary_Binary_Trailer_Appended_After_The_Eoi()
    {
        var original = PhotoImageFixtures.JpegWithArbitraryTrailerAfterEoi(out var trailerMarker);
        Assert.True(ContainsAscii(original, trailerMarker));

        var stripped = ExifScrubber.Strip(original, PhotoImageFormat.Jpeg);

        Assert.False(ContainsAscii(stripped, trailerMarker));
        Assert.False(ContainsAscii(stripped, "<script>"));
        Assert.Equal(0xFF, stripped[^2]);
        Assert.Equal(0xD9, stripped[^1]);
    }

    /// <summary>
    /// S12-SEC-02 finding N-01. <c>FF D8</c> is the one marker with no length field that the parser
    /// still read a length for, so a nested SOI mis-framed the stream and the fall-through copied
    /// the marker plus an attacker-chosen span verbatim — proven on stored bytes to carry an
    /// <c>Exif  </c>-prefixed GPS/IMEI payload through untouched. Reject outright: the only
    /// legitimate nested SOI is inside an APP1 thumbnail (stripped whole) or after EOI (truncated),
    /// so no real encoder output is caught by this.
    /// </summary>
    [Fact]
    public void Strip_Jpeg_Rejects_A_Nested_Soi_Rather_Than_Copying_Its_Mis_Framed_Payload_Verbatim()
    {
        var original = PhotoImageFixtures.JpegWithNestedSoiSmugglingMetadata(out var gpsMarker);
        Assert.True(ContainsAscii(original, gpsMarker)); // the fixture really does carry it

        var exception = Assert.Throws<ImageProcessingException>(
            () => ExifScrubber.Strip(original, PhotoImageFormat.Jpeg));

        Assert.Contains("SOI", exception.Message);
    }

    // ------------------------------------------------------------------------------------
    // PNG
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Strip_Png_Genuinely_Removes_The_ExifAndText_Chunks_From_The_Stored_Bytes()
    {
        var original = PhotoImageFixtures.PngWithExifChunkAndTextMetadata(out var gpsMarker, out var deviceMarker);

        Assert.True(ContainsAscii(original, gpsMarker));
        Assert.True(ContainsAscii(original, deviceMarker));
        Assert.True(ContainsAscii(original, "eXIf"));
        Assert.True(ContainsAscii(original, "tEXt"));

        var stripped = ExifScrubber.Strip(original, PhotoImageFormat.Png);

        Assert.False(ContainsAscii(stripped, gpsMarker));
        Assert.False(ContainsAscii(stripped, deviceMarker));
        Assert.False(ContainsAscii(stripped, "eXIf"));
        Assert.False(ContainsAscii(stripped, "tEXt"));
    }

    [Fact]
    public void Strip_Png_Preserves_The_Signature_And_Structural_Chunks()
    {
        var original = PhotoImageFixtures.PngWithExifChunkAndTextMetadata(out _, out _);
        var stripped = ExifScrubber.Strip(original, PhotoImageFormat.Png);

        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.Equal(pngSignature, stripped[..8]);
        Assert.True(ContainsAscii(stripped, "IHDR"));
        Assert.True(ContainsAscii(stripped, "IDAT"));
        Assert.True(ContainsAscii(stripped, "IEND"));
    }

    [Fact]
    public void Strip_Png_Is_A_Byte_Exact_No_Op_When_There_Is_No_Metadata_To_Remove()
    {
        var original = PhotoImageFixtures.PngWithNoMetadata();
        var stripped = ExifScrubber.Strip(original, PhotoImageFormat.Png);

        Assert.Equal(original, stripped);
    }

    [Fact]
    public void Strip_Png_Throws_ImageProcessingException_When_The_Signature_Is_Missing()
    {
        byte[] notAPng = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];
        Assert.Throws<ImageProcessingException>(() => ExifScrubber.Strip(notAPng, PhotoImageFormat.Png));
    }

    [Fact]
    public void Strip_Png_Throws_ImageProcessingException_When_Iend_Is_Missing()
    {
        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        // IHDR only, no IEND - a genuinely truncated upload.
        byte[] ihdrLength = [0, 0, 0, 13];
        byte[] ihdrType = Encoding.ASCII.GetBytes("IHDR");
        byte[] noIend = [.. pngSignature, .. ihdrLength, .. ihdrType, .. new byte[13], 0, 0, 0, 0];

        Assert.Throws<ImageProcessingException>(() => ExifScrubber.Strip(noIend, PhotoImageFormat.Png));
    }

    [Fact]
    public void Strip_Png_Throws_ImageProcessingException_When_A_Chunk_Length_Overruns_The_File()
    {
        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] hugeLength = [0x7F, 0xFF, 0xFF, 0xFF];
        byte[] type = Encoding.ASCII.GetBytes("IHDR");
        byte[] malformed = [.. pngSignature, .. hugeLength, .. type];

        Assert.Throws<ImageProcessingException>(() => ExifScrubber.Strip(malformed, PhotoImageFormat.Png));
    }

    // ------------------------------------------------------------------------------------
    // L-02 (security review sprint-12.md): an allow-listed chunk TYPE alone is not proof of its
    // DATA's shape - enforce the spec's per-type length and verify the chunk's own CRC-32.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Strip_Png_Throws_ImageProcessingException_When_A_Kept_Chunks_Data_Length_Disagrees_With_The_Spec()
    {
        // A `pHYs` chunk (spec: exactly 9 bytes) carrying 55 bytes of GPS text - the exact
        // metadata-smuggling shape L-02 named.
        var malformed = PhotoImageFixtures.PngWithOversizedPhysChunkCarryingGpsText(out var gpsMarker);
        Assert.True(ContainsAscii(malformed, gpsMarker)); // fixture sanity check.

        Assert.Throws<ImageProcessingException>(() => ExifScrubber.Strip(malformed, PhotoImageFormat.Png));
    }

    [Fact]
    public void Strip_Png_Throws_ImageProcessingException_When_A_Kept_Chunks_Crc_Does_Not_Match_Its_Type_And_Data()
    {
        var malformed = PhotoImageFixtures.PngWithPhysChunkCarryingALyingCrc();

        Assert.Throws<ImageProcessingException>(() => ExifScrubber.Strip(malformed, PhotoImageFormat.Png));
    }

    [Fact]
    public void Strip_Png_Keeps_A_Correctly_Sized_PHYs_Chunk_With_A_Correct_Crc()
    {
        // Sanity check the other direction: a genuinely spec-conformant pHYs chunk (9 bytes,
        // correct CRC) must still survive - L-02's fix must not reject legitimate chunks.
        var valid = PhotoImageFixtures.PngWithValidPhysChunk();

        var stripped = ExifScrubber.Strip(valid, PhotoImageFormat.Png);

        Assert.True(ContainsAscii(stripped, "pHYs"));
    }
}
