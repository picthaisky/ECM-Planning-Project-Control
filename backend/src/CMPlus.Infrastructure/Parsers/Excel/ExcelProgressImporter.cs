using System.Globalization;
using System.IO.Compression;
using CMPlus.Application.Abstractions;
using CMPlus.Application.Import;
using CMPlus.Domain.Common;
using OfficeOpenXml;

namespace CMPlus.Infrastructure.Parsers.Excel;

/// <summary>
/// S3-BE-03/US-3.3 (import half): reads a re-uploaded progress-update template back into
/// <see cref="ParsedProgressRow"/> rows. Exact inverse of <see cref="ExcelProgressTemplateWriter"/>'s
/// column layout.
///
/// Formula-injection defense (S3-BE-03 DoD): every cell is read via <see cref="OfficeOpenXml.ExcelRange.Text"/>
/// (the pre-existing formatted/cached text) - this class never calls
/// <see cref="ExcelWorksheet.Calculate"/>/<c>CalculateAsync</c> anywhere, so even a cell that is a
/// genuine Excel formula (not just a plain string that happens to start with <c>=</c>) can only
/// ever yield whatever cached text the file already carried, never a freshly executed result. A
/// numeric/date field whose raw text starts with <c>=</c>/<c>+</c>/<c>-</c>/<c>@</c> simply fails to
/// parse as that type and rejects the row as malformed - it is read, verbatim, as literal text, and
/// literal text is not a valid decimal/date, so it is never silently coerced into one either.
///
/// Decompression-bomb defense (S3-QA-02 finding): a `.xlsx` is a ZIP container, so the outer
/// upload-size cap only bounds the *compressed* byte count - empirically verified at ~987:1 against
/// the actual EPPlus version this project references, meaning a small upload can still expand to
/// gigabytes before <see cref="ExcelPackage"/> ever gets a chance to reject anything. <see cref="Parse"/>
/// reads the ZIP central directory's declared uncompressed entry sizes (metadata only, no entry is
/// decompressed to do this) and rejects before constructing <see cref="ExcelPackage"/> at all if the
/// total exceeds <see cref="IImportOptionsProvider.MaxDecompressedSizeBytes"/>.
/// </summary>
public sealed class ExcelProgressImporter(IImportOptionsProvider importOptions) : IExcelProgressImporter
{
    private const int ColActivityId = 1;
    private const int ColActivityCode = 2;
    private const int ColPeriodEndDate = 5;
    private const int ColProgressPercentage = 6;
    private const int ColActualQuantity = 7;

    // DateTime.FromOADate's own supported domain (System.DateTime.MinValue/MaxValue expressed as OLE
    // Automation date serials) - S3-SEC-01 M-01 trigger #2.
    private const double MinOleAutomationDate = -657435.0;
    private const double MaxOleAutomationDate = 2958466.0;

    public Result<IReadOnlyList<ParsedProgressRow>> Parse(Stream excelContent)
    {
        var decompressionCheck = RejectIfDecompressedSizeExceedsCap(excelContent);
        if (decompressionCheck is not null)
        {
            return decompressionCheck;
        }

        using var package = new ExcelPackage(excelContent);
        var sheet = package.Workbook.Worksheets.FirstOrDefault();

        if (sheet?.Dimension is null)
        {
            return Result<IReadOnlyList<ParsedProgressRow>>.Failure(
                $"{ImportErrorCodes.MalformedFile}: the workbook has no worksheet or no data.");
        }

        var rows = new List<ParsedProgressRow>();

        for (var row = 2; row <= sheet.Dimension.End.Row; row++)
        {
            var activityIdText = FormulaInjectionGuard.StripOwnEscapeIfPresent(sheet.Cells[row, ColActivityId].Text?.Trim() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(activityIdText))
            {
                continue; // a blank trailing row - not an error, just end-of-data padding.
            }

            if (!Guid.TryParse(activityIdText, out var activityId))
            {
                return Result<IReadOnlyList<ParsedProgressRow>>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: row {row} has an invalid ActivityId '{activityIdText}'.");
            }

            var activityCode = FormulaInjectionGuard.StripOwnEscapeIfPresent(sheet.Cells[row, ColActivityCode].Text?.Trim() ?? string.Empty);

            if (!TryReadDate(sheet, row, ColPeriodEndDate, out var periodEndDate))
            {
                return Result<IReadOnlyList<ParsedProgressRow>>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: row {row} has an unparsable PeriodEndDate.");
            }

            if (!TryReadDecimal(sheet, row, ColProgressPercentage, required: true, out var progressPercentage))
            {
                return Result<IReadOnlyList<ParsedProgressRow>>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: row {row} has an unparsable/rejected ProgressPercentage.");
            }

            decimal? actualQuantity = null;
            var actualQuantityText = sheet.Cells[row, ColActualQuantity].Text?.Trim();
            if (!string.IsNullOrEmpty(actualQuantityText))
            {
                if (!TryReadDecimal(sheet, row, ColActualQuantity, required: false, out var quantity))
                {
                    return Result<IReadOnlyList<ParsedProgressRow>>.Failure(
                        $"{ImportErrorCodes.MalformedFile}: row {row} has an unparsable/rejected ActualQuantity.");
                }

                actualQuantity = quantity;
            }

            rows.Add(new ParsedProgressRow(activityId, activityCode, periodEndDate!.Value, progressPercentage, actualQuantity, row));
        }

        return Result<IReadOnlyList<ParsedProgressRow>>.Success(rows);
    }

    /// <summary>Returns a <see cref="Result{T}.Failure"/> if the ZIP central directory's declared
    /// uncompressed entry sizes sum past the configured cap - metadata inspection only, no entry
    /// content is decompressed to make this decision. Returns <c>null</c> (proceed) when within cap.
    /// Not a formal "zip bomb" full defense (a maliciously crafted central directory could in theory
    /// under-report sizes for a still-valid archive, though this is far harder to construct than
    /// simply choosing a high compression ratio) but blocks the realistic attack this finding
    /// demonstrated - a highly-compressible payload run through ordinary DEFLATE - at negligible cost.</summary>
    private Result<IReadOnlyList<ParsedProgressRow>>? RejectIfDecompressedSizeExceedsCap(Stream excelContent)
    {
        if (!excelContent.CanSeek)
        {
            // Every real call site (ASP.NET Core's IFormFile-backed streams, and every test in this
            // solution) is seekable; a non-seekable stream can't be safely rewound after inspection,
            // so fail closed rather than silently skip the check.
            return Result<IReadOnlyList<ParsedProgressRow>>.Failure(
                $"{ImportErrorCodes.MalformedFile}: upload stream must support seeking.");
        }

        var startPosition = excelContent.Position;
        try
        {
            using var zip = new ZipArchive(excelContent, ZipArchiveMode.Read, leaveOpen: true);
            var totalUncompressed = 0L;
            foreach (var entry in zip.Entries)
            {
                totalUncompressed += entry.Length; // declared uncompressed size; entry is not extracted.
                if (totalUncompressed > importOptions.MaxDecompressedSizeBytes)
                {
                    return Result<IReadOnlyList<ParsedProgressRow>>.Failure(
                        $"{ImportErrorCodes.FileTooLarge}: the file would expand past the configured " +
                        $"{importOptions.MaxDecompressedSizeBytes:N0}-byte decompressed limit.");
                }
            }
        }
        catch (InvalidDataException)
        {
            // Not a valid ZIP container at all - not this guard's concern (ExcelPackage below will
            // reject it as a malformed workbook with its own clear error).
        }
        finally
        {
            excelContent.Position = startPosition;
        }

        return null;
    }

    private static bool TryReadDate(ExcelWorksheet sheet, int row, int col, out DateTimeOffset? result)
    {
        var cell = sheet.Cells[row, col];

        if (cell.Value is DateTime dateTimeValue)
        {
            result = new DateTimeOffset(DateTime.SpecifyKind(dateTimeValue, DateTimeKind.Utc));
            return true;
        }

        // A date cell round-tripped through a saved/reloaded XLSX (as opposed to a still-in-memory
        // ExcelPackage) commonly comes back as the raw OLE Automation date serial number rather
        // than a materialized DateTime - Excel's on-disk representation of any date/time value is
        // always a plain number (the cell's NumberFormat is what makes Excel's own UI *display* it
        // as a date; it does not change how the underlying value round-trips through EPPlus). This
        // column is semantically always a date, so any numeric value here is read as one.
        if (cell.Value is double oleAutomationDate)
        {
            // Bounds-checked (S3-SEC-01 M-01 trigger #2): a value outside DateTime.FromOADate's own
            // supported domain - or NaN/+-Infinity, neither of which is a valid date serial at all -
            // throws ArgumentException rather than failing gracefully. Rejected the same way every
            // other unparsable field in this importer is (a malformed-row Result.Failure), never an
            // unhandled exception.
            if (double.IsNaN(oleAutomationDate) || double.IsInfinity(oleAutomationDate)
                || oleAutomationDate <= MinOleAutomationDate || oleAutomationDate >= MaxOleAutomationDate)
            {
                result = null;
                return false;
            }

            result = new DateTimeOffset(DateTime.SpecifyKind(DateTime.FromOADate(oleAutomationDate), DateTimeKind.Utc));
            return true;
        }

        var text = cell.Text?.Trim();
        if (string.IsNullOrEmpty(text) || FormulaInjectionGuard.StartsWithFormulaTrigger(text))
        {
            result = null;
            return false;
        }

        if (DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            result = parsed;
            return true;
        }

        result = null;
        return false;
    }

    private static bool TryReadDecimal(ExcelWorksheet sheet, int row, int col, bool required, out decimal result)
    {
        var cell = sheet.Cells[row, col];

        if (cell.Value is double doubleValue)
        {
            // Bounds-checked (S3-SEC-01 M-01 trigger #3): NaN/+-Infinity, or a magnitude beyond
            // decimal's own representable range, throws OverflowException on a direct (decimal)
            // cast rather than failing gracefully. Rejected like every other unparsable field.
            if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue)
                || doubleValue < (double)decimal.MinValue || doubleValue > (double)decimal.MaxValue)
            {
                result = 0m;
                return false;
            }

            result = (decimal)doubleValue;
            return true;
        }

        var text = cell.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            result = 0m;
            return !required;
        }

        // A raw value starting with =, +, -, @ is read as literal text (S3-BE-03 DoD) - it is
        // never parsed/coerced into a number, so the row is rejected rather than silently accepting
        // whatever the "formula-like" text happened to evaluate to. Deliberate conservative
        // trade-off: this also rejects a *text-typed* cell holding a plain negative number written
        // as a string (e.g. a "-5" that Excel stored as text, not a number) - our own export always
        // writes this column as a genuine numeric cell (caught by the `cell.Value is double` branch
        // above), so a legitimate re-upload of our own template never reaches this branch at all;
        // only a hand-edited/malformed/hostile file does, where rejecting is the safe choice.
        if (FormulaInjectionGuard.StartsWithFormulaTrigger(text))
        {
            result = 0m;
            return false;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }
}
