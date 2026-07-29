using System.Text;

namespace CMPlus.Application.Import;

/// <summary>
/// Magic-byte / leading-content verification for the three import formats (Sprint 3 security review
/// DoD item 2, closed alongside M-01). Runs in each command handler before the corresponding parser
/// is ever invoked, so a file whose actual bytes do not match the format implied by the route it was
/// posted to (e.g. an <c>.xlsx</c> ZIP posted to the <c>xer</c> route) is rejected as a modelled
/// <c>Failed</c> job here - never left for whichever parser happens to run to discover the mismatch,
/// possibly by throwing (the review's M-01 trigger #1: <c>ExcelPackage</c> throws
/// <see cref="System.IO.InvalidDataException"/> for every non-OOXML input).
///
/// Deliberately lenient per format (a hand-built test fixture may omit an XML declaration, and a
/// real P6 export always but a minimal fixture need not carry the <c>ERMHDR</c> preamble) - this is
/// a coarse content-sniffing gate, not a full structural validation; the existing per-table/per-field
/// checks in each parser remain the source of truth for whether a file is actually well-formed.
/// </summary>
public static class FileSignatureValidator
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] XlsxZipLocalFileHeaderSignature = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>XER is tab-delimited text. A real P6 export starts with the <c>ERMHDR</c> preamble
    /// line; a hand-built fixture may start directly with a <c>%T</c> record (both are accepted by
    /// <c>XerDocumentReader</c> itself, so both are accepted here too).</summary>
    public static bool IsXer(byte[] content)
    {
        var text = LeadingText(content);
        return text.StartsWith("ERMHDR", StringComparison.Ordinal) || text.StartsWith("%T", StringComparison.Ordinal);
    }

    /// <summary>MSPDI is XML. Accepts either a real XML declaration or a bare leading element tag (a
    /// hand-built fixture may omit the declaration) - anything not starting with <c>&lt;</c> after a
    /// possible UTF-8 BOM cannot be XML at all.</summary>
    public static bool IsMspdi(byte[] content)
    {
        var text = LeadingText(content);
        return text.StartsWith("<", StringComparison.Ordinal);
    }

    /// <summary>XLSX (OOXML) is a ZIP container. The local file header signature is the one reliable,
    /// position-fixed magic byte sequence every valid <c>.xlsx</c> begins with - including a
    /// password-protected workbook, which is instead wrapped in an OLE2/CFB container and therefore
    /// correctly fails this check rather than reaching <c>ExcelPackage</c> at all.</summary>
    public static bool IsXlsx(byte[] content) =>
        content.Length >= XlsxZipLocalFileHeaderSignature.Length
        && content.AsSpan(0, XlsxZipLocalFileHeaderSignature.Length).SequenceEqual(XlsxZipLocalFileHeaderSignature);

    /// <summary>Decodes up to a small leading prefix as UTF-8 (skipping a UTF-8 BOM if present) and
    /// trims leading whitespace - enough to inspect a handful of leading characters without decoding
    /// (or even touching) the rest of a potentially large upload.</summary>
    private static string LeadingText(byte[] content)
    {
        const int maxLeadingBytes = 32;

        var offset = content.Length >= Utf8Bom.Length && content.AsSpan(0, Utf8Bom.Length).SequenceEqual(Utf8Bom)
            ? Utf8Bom.Length
            : 0;

        var length = Math.Min(maxLeadingBytes, content.Length - offset);
        return length <= 0 ? string.Empty : Encoding.UTF8.GetString(content, offset, length).TrimStart();
    }
}
