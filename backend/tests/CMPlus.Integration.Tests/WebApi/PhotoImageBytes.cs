using System.Text;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// Minimal hand-built JPEG byte sequences for <c>ProjectPhotosControllerTests</c> - the HTTP-level
/// sibling of <c>CMPlus.Application.Tests.Photos.PhotoImageFixtures</c> (duplicated rather than
/// shared across test assemblies, consistent with this codebase's existing per-project test-double
/// convention - see e.g. each feature folder's own <c>*TestFakes.cs</c>).
/// </summary>
internal static class PhotoImageBytes
{
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var length = payload.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)(length & 0xFF), .. payload];
    }

    /// <summary>A bare, metadata-free JPEG - good enough for tests that only care that SOME valid
    /// JPEG was accepted (role/RBAC/tenant-isolation/not-found cases).</summary>
    public static byte[] PlainJpeg()
    {
        var soi = new byte[] { 0xFF, 0xD8 };
        var sos = Segment(0xDA, [1, 0, 0, 0, 0, 0]);
        byte[] scanData = [0xAB, 0xCD, 0xEF];
        var eoi = new byte[] { 0xFF, 0xD9 };
        return [.. soi, .. sos, .. scanData, .. eoi];
    }

    /// <summary>A JPEG carrying a JFIF APP0, an EXIF APP1 embedding recognisable GPS/device ASCII
    /// markers, and a COM segment - the fixture the EXIF-stripping mutation-evidence test scans the
    /// literal on-disk file for.</summary>
    public static byte[] JpegWithGpsExifAndDeviceInfo(out string gpsMarker, out string deviceMarker, out string commentMarker)
    {
        gpsMarker = "GPSLatitude13.7563N-GPSLongitude100.5018E";
        deviceMarker = "Make=Canon;Model=EOS R5;Software=iOS 18.1";
        commentMarker = "COMMENT-SHOULD-NOT-SURVIVE-SCRUBBING";

        var soi = new byte[] { 0xFF, 0xD8 };
        var app0 = Segment(0xE0, [.. Ascii("JFIF\0"), 1, 2, 0, 0, 1, 0, 1, 0, 0]);
        var app1 = Segment(0xE1, [.. Ascii("Exif\0\0"), .. Ascii(gpsMarker), .. Ascii(deviceMarker)]);
        var com = Segment(0xFE, Ascii(commentMarker));
        var sos = Segment(0xDA, [1, 0, 0, 0, 0, 0]);
        byte[] scanData = [0x12, 0x34, 0xFF, 0x00, 0x56, 0xFF, 0xD0, 0x78];
        var eoi = new byte[] { 0xFF, 0xD9 };

        return [.. soi, .. app0, .. app1, .. com, .. sos, .. scanData, .. eoi];
    }

    /// <summary>H-01 (security review sprint-12.md): GPS-carrying EXIF placed AFTER the SOS's scan
    /// data - the exact region the pre-fix <c>ExifScrubber</c> copied to storage verbatim, unread.
    /// Used by the "stored bytes on disk" H-01 regression test, which reads the real file
    /// <c>LocalDiskFileStorage</c> wrote, not <see cref="ExifScrubber.Strip"/>'s return value.</summary>
    public static byte[] JpegWithGpsExifAfterSos(out string gpsMarker)
    {
        gpsMarker = "GPSLatitude13.9124N-AFTER-SOS-DISK-CHECK";

        var soi = new byte[] { 0xFF, 0xD8 };
        var sos = Segment(0xDA, [1, 0, 0, 0, 0, 0]);
        byte[] scanData = [0x11, 0x22, 0xFF, 0x00, 0x33, 0xFF, 0xD0, 0x44];
        var exifAfterSos = Segment(0xE1, [.. Ascii("Exif\0\0"), .. Ascii(gpsMarker)]);
        var eoi = new byte[] { 0xFF, 0xD9 };

        return [.. soi, .. sos, .. scanData, .. exifAfterSos, .. eoi];
    }

    /// <summary>H-01: a second, complete JPEG (with its own GPS-carrying EXIF) appended after the
    /// primary image's EOI - the Multi-Picture-Format/"secondary image" shape.</summary>
    public static byte[] JpegWithSecondJpegAfterEoi(out string gpsMarker)
    {
        gpsMarker = "GPSLatitude7.8804N-SECONDARY-IMAGE-DISK-CHECK";

        var primary = PlainJpeg();

        var secondarySoi = new byte[] { 0xFF, 0xD8 };
        var secondaryExif = Segment(0xE1, [.. Ascii("Exif\0\0"), .. Ascii(gpsMarker)]);
        var secondarySos = Segment(0xDA, [1, 0, 0, 0, 0, 0]);
        byte[] secondaryScanData = [0x55, 0x66, 0x77];
        var secondaryEoi = new byte[] { 0xFF, 0xD9 };
        byte[] secondaryImage = [.. secondarySoi, .. secondaryExif, .. secondarySos, .. secondaryScanData, .. secondaryEoi];

        return [.. primary, .. secondaryImage];
    }
}
