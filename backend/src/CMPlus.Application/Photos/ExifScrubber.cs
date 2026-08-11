using System.Text;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Photos;

/// <summary>
/// Removes every metadata-bearing segment/chunk from a photo upload BEFORE it is ever handed to
/// <c>IFileStorage</c> (S12-BE-01 DoD: "strip EXIF that carries GPS coordinates or device
/// identifiers before storing" - note "before", not "then clean up afterward": the original,
/// un-scrubbed bytes are never written to storage or any other durable location anywhere in this
/// codebase - <c>UploadPhotoCommandHandler</c> calls <see cref="Strip"/> before its one and only
/// <c>IFileStorage.SaveAsync</c> call).
///
/// <para><b>Why strip the whole metadata segment rather than parse and remove only the GPS/device
/// IFD tags:</b> EXIF (JPEG APP1, "Exif\0\0" header) is not the only place a JPEG/PNG can carry a
/// GPS coordinate or a device identifier - the same APP1 marker also carries XMP
/// (<c>http://ns.adobe.com/xap/1.0/</c>), which can independently embed
/// <c>exif:GPSLatitude</c>/<c>tiff:Make</c>/<c>tiff:Model</c> as XML, and APP13 carries Photoshop
/// IRB metadata that can carry similar identifying fields. Parsing every one of those formats
/// correctly and keeping only the "safe" tags is a much larger, easier-to-get-wrong surface than
/// removing the whole non-pixel-data segment outright - the same reasoning
/// <c>jpegtran -copy none</c>/most "strip metadata" tools apply by default. The one accepted
/// trade-off: a JPEG's EXIF Orientation tag (and a PNG's rarely-used EXIF-in-eXIf orientation) is
/// also removed, so a photo relying on that tag rather than physically upright pixels could display
/// rotated after stripping - flagged for the frontend to bake orientation into pixels client-side
/// before upload (already compressing photos client-side per S12-FE-01) rather than solved here by
/// partially trusting EXIF content this code does not otherwise parse.</para>
///
/// <para>Fails closed (<see cref="ImageProcessingException"/>) on any structural inconsistency
/// rather than falling back to "copy the rest verbatim" or "return the input unchanged" - either of
/// those could silently leave a metadata segment in place. No image is decoded/recompressed; only
/// the container-level marker/chunk structure is walked and filtered, so this never touches actual
/// pixel data and cannot introduce a lossy re-encode.</para>
/// </summary>
public static class ExifScrubber
{
    /// <summary>JPEG APPn (0xE0-0xEF: JFIF/EXIF/XMP/ICC/Photoshop IRB/…) and COM (0xFE) markers -
    /// every one of these carries metadata only, never pixel data, so all are stripped
    /// unconditionally (see this type's class remarks).</summary>
    private static bool IsJpegMetadataMarker(byte marker) => (marker >= 0xE0 && marker <= 0xEF) || marker == 0xFE;

    /// <summary>PNG chunk types that carry pixel/structural data this codebase keeps. Everything
    /// else - including <c>eXIf</c> (PNG's own EXIF-carrying chunk, which can hold GPS the same way
    /// JPEG's APP1 does), <c>tEXt</c>/<c>zTXt</c>/<c>iTXt</c> (free-text metadata, which commonly
    /// carries an XMP profile with the same GPS/device fields), <c>tIME</c>, and <c>iCCP</c> - is
    /// stripped. An allow-list (rather than a deny-list of "known-bad" chunk types) is the safer
    /// default: an unrecognised ancillary chunk this list has never heard of is stripped too, not
    /// silently kept on the assumption it is harmless.</summary>
    private static readonly HashSet<string> PngKeptChunkTypes = new(StringComparer.Ordinal)
    {
        "IHDR", "PLTE", "IDAT", "IEND", "tRNS", "gAMA", "cHRM", "sRGB", "pHYs", "sBIT", "hIST", "bKGD",
    };

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Security review sprint-12.md L-02: the exact/maximum data length the PNG spec
    /// defines for each small, fixed-shape ancillary chunk this scrubber keeps - see
    /// <see cref="StripPng"/>. <c>IHDR</c> and <c>IEND</c> are exact; <c>PLTE</c>/<c>IDAT</c>/
    /// <c>hIST</c> are genuinely variable-length (image-dependent) and are intentionally left
    /// unconstrained here beyond the file-size bound <see cref="StripPng"/> already enforces.</summary>
    private static readonly Dictionary<string, (uint Min, uint Max)> PngChunkDataLengthBounds = new(StringComparer.Ordinal)
    {
        ["IHDR"] = (13, 13),
        ["IEND"] = (0, 0),
        ["pHYs"] = (9, 9),
        ["gAMA"] = (4, 4),
        ["cHRM"] = (32, 32),
        ["sRGB"] = (1, 1),
        ["sBIT"] = (1, 4),
        ["bKGD"] = (1, 6),
        ["tRNS"] = (0, 256),
    };

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    /// <summary>PNG's own CRC-32 (ISO 3309 / ITU-T V.42 - the same algorithm zlib uses), computed
    /// over a chunk's 4-byte type plus its data. Per the PNG spec this never covers the 4-byte
    /// length field or the CRC field itself.</summary>
    private static uint ComputeCrc32(byte[] data, int offset, int count)
    {
        var crc = 0xFFFFFFFFu;
        for (var i = 0; i < count; i++)
        {
            crc = Crc32Table[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    public static byte[] Strip(byte[] content, PhotoImageFormat format) => format switch
    {
        PhotoImageFormat.Jpeg => StripJpeg(content),
        PhotoImageFormat.Png => StripPng(content),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported PhotoImageFormat."),
    };

    private static byte[] StripJpeg(byte[] content)
    {
        if (content.Length < 2 || content[0] != 0xFF || content[1] != 0xD8)
        {
            throw new ImageProcessingException("Not a valid JPEG (missing SOI marker).");
        }

        using var output = new MemoryStream(content.Length);
        output.WriteByte(0xFF);
        output.WriteByte(0xD8);

        var offset = 2;
        while (offset < content.Length)
        {
            if (content[offset] != 0xFF)
            {
                throw new ImageProcessingException("Malformed JPEG: expected a marker.");
            }

            // Any number of 0xFF fill bytes may legally precede the real marker code.
            while (offset < content.Length && content[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= content.Length)
            {
                throw new ImageProcessingException("Malformed JPEG: truncated marker.");
            }

            var marker = content[offset];
            offset++;

            // TEM (0x01) and RSTn (0xD0-0xD7): standalone, no length/payload.
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                continue;
            }

            if (marker == 0xD8) // A second SOI. Reject, rather than fall through.
            {
                // SOI has no length field, so the fall-through below would read the next two bytes
                // as a segment length, decide 0xD8 is not a metadata marker, and copy the marker
                // plus that attacker-chosen number of bytes straight to the output - the same
                // mis-framing-into-a-copy-verbatim shape as H-01 (sprint-12.md N-01, proven on
                // stored bytes: an `Exif\0\0`-prefixed GPS/IMEI payload survived intact).
                //
                // Rejecting is safe: the only legitimate nested SOI in a JPEG is an EXIF thumbnail,
                // which lives inside an APP1 segment that is already stripped whole, or an MPF
                // secondary image, which lives after EOI and is already truncated. No encoder emits
                // a bare nested SOI, so this cannot reject real camera output.
                throw new ImageProcessingException(
                    "Malformed JPEG: unexpected SOI marker inside the stream.");
            }

            if (marker == 0xD9) // EOI: terminate here and discard everything after it, unconditionally.
                                 //
                                 // Deliberate choice (sprint-12.md H-01): this also throws away any
                                 // legitimate trailer appended after the primary image - a Multi-
                                 // Picture-Format secondary/thumbnail image, or a motion-photo video
                                 // trailer. This product only ever stores/serves the single primary
                                 // still image (`Photo` has no concept of a secondary image or an
                                 // embedded video), so nothing downstream can read such a trailer even
                                 // if it were kept - and every byte after the primary EOI is
                                 // unparsed/unvalidated by definition, so keeping it would mean
                                 // storing attacker-controlled bytes of unknown shape verbatim next to
                                 // an `image/jpeg` label. The narrower, structure-aware alternative
                                 // (walk the trailer as its own container and strip metadata from it
                                 // too) was rejected: it would require understanding MPF/motion-photo
                                 // framing specifically, which is out of scope for a container-level
                                 // scrubber that deliberately does not parse image-specific formats
                                 // (see this type's class remarks).
            {
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                offset = content.Length;
                break;
            }

            if (offset + 2 > content.Length)
            {
                throw new ImageProcessingException("Malformed JPEG: truncated segment length.");
            }

            var length = (content[offset] << 8) | content[offset + 1];
            if (length < 2)
            {
                throw new ImageProcessingException("Malformed JPEG: invalid segment length.");
            }

            if (offset + length > content.Length)
            {
                throw new ImageProcessingException("Malformed JPEG: segment length exceeds file size.");
            }

            if (!IsJpegMetadataMarker(marker))
            {
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                output.Write(content, offset, length);
            }

            offset += length;

            if (marker == 0xDA) // Start Of Scan: the segment just written/kept above is only the
                                 // scan HEADER. The byte-stuffed entropy-coded scan data that
                                 // follows it is not covered by `length` and has to be walked
                                 // separately.
                                 //
                                 // Security review sprint-12.md H-01: this used to assume "never
                                 // another parseable marker segment follows a scan" and copied the
                                 // remainder of the file verbatim from here to EOF. That is false for
                                 // progressive JPEG (multiple SOS/scan pairs separated by ordinary -
                                 // possibly metadata-carrying - marker segments before the single
                                 // terminating EOI) and for any container with data appended after
                                 // the primary image's EOI (MPF secondary images, motion-photo
                                 // trailers, or an attacker simply placing EXIF after the scan to
                                 // bypass this filter). Instead, skip past the entropy-coded data to
                                 // the next real marker and resume this same filtering loop there.
            {
                var scanDataEnd = FindEndOfEntropyCodedData(content, offset);
                output.Write(content, offset, scanDataEnd - offset);
                offset = scanDataEnd;
            }
        }

        return output.ToArray();
    }

    /// <summary>Scans forward from <paramref name="offset"/> (the first byte of an SOS segment's
    /// entropy-coded scan data) to the offset of the 0xFF byte that begins the next REAL marker -
    /// i.e. the first <c>FF xx</c> where <c>xx</c> is not a stuffed literal 0xFF (<c>00</c>), not an
    /// RSTn restart marker (<c>D0</c>-<c>D7</c>, which legally appears unstuffed inside entropy data
    /// with no length field of its own), and not itself a fill byte (<c>FF</c>) that precedes the
    /// real marker code. Everything strictly before the returned offset is genuine scan data and is
    /// copied verbatim by the caller - nothing in it is reinterpreted as a marker or altered.
    /// Fails closed (same discipline as the rest of this parser) if the entropy-coded data runs to
    /// the end of the file with no terminating marker.</summary>
    private static int FindEndOfEntropyCodedData(byte[] content, int offset)
    {
        var i = offset;
        while (true)
        {
            while (i < content.Length && content[i] != 0xFF)
            {
                i++;
            }

            if (i >= content.Length)
            {
                throw new ImageProcessingException("Malformed JPEG: scan data runs past end of file with no terminating marker.");
            }

            var j = i + 1;
            while (j < content.Length && content[j] == 0xFF)
            {
                j++;
            }

            if (j >= content.Length)
            {
                throw new ImageProcessingException("Malformed JPEG: truncated marker after scan data.");
            }

            var next = content[j];
            if (next == 0x00 || (next >= 0xD0 && next <= 0xD7))
            {
                // Stuffed literal 0xFF, or an unstuffed restart marker - both are part of the
                // entropy-coded data, not a segment boundary. Keep scanning past them.
                i = j + 1;
                continue;
            }

            return j - 1; // The last 0xFF fill byte before the real marker code - content[j - 1] is
                           // always 0xFF, so the caller's main loop (which itself skips any further
                           // fill-byte run before reading the marker code) resumes correctly from here.
        }
    }

    private static byte[] StripPng(byte[] content)
    {
        if (content.Length < PngSignature.Length || !content.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            throw new ImageProcessingException("Not a valid PNG (missing signature).");
        }

        using var output = new MemoryStream(content.Length);
        output.Write(content, 0, PngSignature.Length);

        var offset = PngSignature.Length;
        var sawIend = false;
        while (offset < content.Length)
        {
            if (offset + 8 > content.Length)
            {
                throw new ImageProcessingException("Malformed PNG: truncated chunk header.");
            }

            var dataLength = ReadUInt32BigEndian(content, offset);
            var type = Encoding.ASCII.GetString(content, offset + 4, 4);

            // 4 (length) + 4 (type) + dataLength (data) + 4 (crc). Computed in `long` - dataLength
            // is a uint that could otherwise overflow an int/`offset + chunkTotal` addition.
            var chunkTotal = 12L + dataLength;
            if (chunkTotal > content.Length - offset)
            {
                throw new ImageProcessingException("Malformed PNG: chunk length exceeds file size.");
            }

            if (PngKeptChunkTypes.Contains(type))
            {
                // Security review sprint-12.md L-02: an allow-listed chunk TYPE alone is not proof
                // its DATA is what that type is supposed to hold - a spec-legal-looking `pHYs` chunk
                // (spec: exactly 9 bytes) can declare any data length up to the file-size bound
                // already checked above, e.g. 42 bytes of arbitrary/GPS text, and would otherwise
                // pass straight through unchanged. Enforce the exact/maximum data length the PNG
                // spec defines for each small, fixed-shape ancillary chunk this scrubber keeps, and
                // verify the chunk's own CRC-32 (previously never checked at all - a lying CRC was
                // silently accepted) so a chunk that fails either check is rejected outright rather
                // than kept.
                if (PngChunkDataLengthBounds.TryGetValue(type, out var bounds)
                    && (dataLength < bounds.Min || dataLength > bounds.Max))
                {
                    throw new ImageProcessingException(
                        $"Malformed PNG: '{type}' chunk data length {dataLength} is outside the {bounds.Min}-{bounds.Max} byte range the spec allows.");
                }

                var dataLengthInt = checked((int)dataLength);
                var declaredCrc = ReadUInt32BigEndian(content, offset + 8 + dataLengthInt);
                var computedCrc = ComputeCrc32(content, offset + 4, 4 + dataLengthInt);
                if (declaredCrc != computedCrc)
                {
                    throw new ImageProcessingException($"Malformed PNG: '{type}' chunk CRC does not match its declared type/data.");
                }

                output.Write(content, offset, checked((int)chunkTotal));
            }

            offset += checked((int)chunkTotal);

            if (type == "IEND")
            {
                sawIend = true;
                break;
            }
        }

        if (!sawIend)
        {
            throw new ImageProcessingException("Malformed PNG: missing IEND chunk.");
        }

        return output.ToArray();
    }

    private static uint ReadUInt32BigEndian(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
}
