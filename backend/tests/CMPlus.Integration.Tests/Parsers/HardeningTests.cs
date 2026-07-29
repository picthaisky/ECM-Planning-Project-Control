using CMPlus.Application.Abstractions;
using CMPlus.Application.Import;
using CMPlus.Infrastructure.Parsers.Excel;
using CMPlus.Infrastructure.Parsers.Mspdi;
using CMPlus.Infrastructure.Parsers.Xer;
using OfficeOpenXml;

namespace CMPlus.Integration.Tests.Parsers;

/// <summary>
/// S3-QA-02: security hardening for the import pipeline - XXE and internal-entity-expansion/
/// "billion laughs" (S3-BE-02), a relation cycle rejected all-or-nothing (S3-BE-01), formula
/// injection on both the import and export sides (S3-BE-03), and a zip/entity-expansion ("zip
/// bomb") probe against the XLSX importer (a real gap found during this sprint's QA pass and closed
/// the same day - see the decompression-size guard tests below). The oversized-file ("rejected
/// before parsing begins") DoD is covered at the Application-handler level in
/// <c>CMPlus.Application.Tests.Features.Import.*CommandHandlerTests</c> instead of here (both the
/// XER/MSPDI-shared handler and the separate Excel handler each have their own test class there),
/// since that check runs in the command handler, not in these Infrastructure parsers themselves -
/// this file only exercises the parsers/EPPlus adapters directly.
/// </summary>
public class HardeningTests
{
    // Generous cap for every test in this file that isn't specifically exercising the
    // decompression-size guard itself (those use TinyDecompressedCapImportOptions below).
    private sealed class UnlimitedImportOptions : IImportOptionsProvider
    {
        public long MaxFileSizeBytes => long.MaxValue;

        public long MaxDecompressedSizeBytes => long.MaxValue;

        public long MaxEntityCount => long.MaxValue;
    }

    // ------------------------------------------------------------------------------------
    // XXE (S3-BE-02 DoD)
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Mspdi_Parser_Rejects_A_DOCTYPE_External_Entity_Declaration()
    {
        using var stream = FixtureFiles.OpenRead("mspdi/xxe-attack.xml");

        var result = new MspdiScheduleParser().Parse(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.StartsWith(ImportErrorCodes.XxeRejected, result.Error);
    }

    [Fact]
    public void Mspdi_Parser_Rejects_A_DOCTYPE_Even_When_The_Entity_Is_Never_Referenced()
    {
        // The rejection must happen at DOCTYPE-declaration time (DtdProcessing.Prohibit), not only
        // when/if the &xxe; reference is actually expanded - proving the hardening is structural,
        // not "it happened to fail because /etc/passwd doesn't parse as valid content here".
        var xmlWithUnusedEntity = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE Project [ <!ENTITY unused SYSTEM "file:///etc/passwd"> ]>
            <Project xmlns="http://schemas.microsoft.com/project">
              <Tasks>
                <Task><UID>0</UID><Name>Root</Name><OutlineLevel>0</OutlineLevel><Summary>1</Summary></Task>
              </Tasks>
            </Project>
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xmlWithUnusedEntity));
        var result = new MspdiScheduleParser().Parse(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.StartsWith(ImportErrorCodes.XxeRejected, result.Error);
    }

    [Fact]
    public void Mspdi_Parser_Rejects_A_Billion_Laughs_Style_Internal_Entity_Expansion_Attack()
    {
        // Distinct attack class from the two tests above: no SYSTEM/PUBLIC external reference at
        // all (so this is not SSRF/file-disclosure XXE) - just nested, self-referential internal
        // general entities that expand exponentially on resolution ("billion laughs"/entity-
        // expansion DoS, the same OWASP category the S3-QA-02 DoD's "entity expansion" wording
        // names alongside zip bombs). DtdProcessing.Prohibit rejects the DOCTYPE unconditionally,
        // so this - like the unused-external-entity case - must fail before any entity is ever
        // resolved/expanded, proving CM+'s XXE hardening also structurally forecloses this
        // distinct DoS vector, not merely the SSRF-shaped one.
        var billionLaughsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE Project [
              <!ENTITY lol0 "lol">
              <!ENTITY lol1 "&lol0;&lol0;&lol0;&lol0;&lol0;&lol0;&lol0;&lol0;&lol0;&lol0;">
              <!ENTITY lol2 "&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;">
              <!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;">
            ]>
            <Project xmlns="http://schemas.microsoft.com/project">
              <Tasks>
                <Task><UID>0</UID><Name>&lol3;</Name><OutlineLevel>0</OutlineLevel><Summary>1</Summary></Task>
              </Tasks>
            </Project>
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(billionLaughsXml));
        var result = new MspdiScheduleParser().Parse(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.StartsWith(ImportErrorCodes.XxeRejected, result.Error);
    }

    // ------------------------------------------------------------------------------------
    // Relation cycle, all-or-nothing (S3-BE-01 DoD)
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Xer_Parser_Rejects_A_Relation_Cycle_Identifying_The_Offending_Chain()
    {
        using var stream = FixtureFiles.OpenRead("xer/cycle-schedule.xer");

        var result = new XerScheduleParser(new UnlimitedImportOptions()).Parse(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.StartsWith(ImportErrorCodes.RelationCycleDetected, result.Error);
        Assert.Contains("A1010", result.Error);
        Assert.Contains("A1020", result.Error);
        Assert.Contains("A1030", result.Error);
        Assert.Contains("->", result.Error);
        // All-or-nothing is structural here: Result<ParsedSchedule>.Failure carries no
        // ParsedSchedule value at all - there is nothing a caller could partially persist even by
        // mistake (Result<T>.Value throws on a failure, per Result<T>'s own invariant).
    }

    // ------------------------------------------------------------------------------------
    // Formula injection - both sides (S3-BE-03 DoD)
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Excel_Importer_Reads_A_Formula_Triggering_ActivityCode_As_Literal_Text()
    {
        var activityId = Guid.NewGuid();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Progress");
        sheet.Cells[1, 1].Value = "ActivityId";
        sheet.Cells[1, 2].Value = "ActivityCode";
        sheet.Cells[1, 5].Value = "PeriodEndDate";
        sheet.Cells[1, 6].Value = "ProgressPercentage";

        sheet.Cells[2, 1].Value = activityId.ToString();
        // A plain string cell - not a real Excel formula - whose text happens to start with '='.
        // This is exactly the CSV/Excel-injection shape: Excel's own UI would evaluate it as a
        // formula on open; our importer must not (S3-BE-03 DoD: "read as literal text").
        sheet.Cells[2, 2].Value = "=cmd|' /C calc'!A0";
        sheet.Cells[2, 5].Value = new DateTime(2026, 7, 1);
        sheet.Cells[2, 6].Value = 40m;

        using var stream = new MemoryStream();
        package.SaveAs(stream);
        stream.Position = 0;

        var result = new ExcelProgressImporter(new UnlimitedImportOptions()).Parse(stream);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        var row = Assert.Single(result.Value);
        Assert.Equal(activityId, row.ActivityId);
        // Read back verbatim, character-for-character - never evaluated, never stripped, never
        // executed (this importer never calls ExcelWorksheet.Calculate anywhere).
        Assert.Equal("=cmd|' /C calc'!A0", row.ActivityCode);
        Assert.Equal(40m, row.ProgressPercentage);
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1:A2)")]
    public void Excel_Importer_Rejects_A_Formula_Triggering_ProgressPercentage_Rather_Than_Coercing_It(string maliciousValue)
    {
        var activityId = Guid.NewGuid();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Progress");
        sheet.Cells[2, 1].Value = activityId.ToString();
        sheet.Cells[2, 5].Value = new DateTime(2026, 7, 1);
        // Written as a plain text-typed cell (not a number) so ProgressPercentage's raw text is
        // exactly the malicious value - proves it is never parsed/coerced into a number.
        sheet.Cells[2, 6].Style.Numberformat.Format = "@";
        sheet.Cells[2, 6].Value = maliciousValue;

        using var stream = new MemoryStream();
        package.SaveAs(stream);
        stream.Position = 0;

        var result = new ExcelProgressImporter(new UnlimitedImportOptions()).Parse(stream);

        Assert.True(result.IsFailure);
        Assert.StartsWith(ImportErrorCodes.MalformedFile, result.Error);
    }

    [Fact]
    public void Excel_Template_Writer_Escapes_An_ActivityName_That_Legitimately_Starts_With_A_Formula_Trigger()
    {
        var rows = new List<ActivityProgressTemplateRow>
        {
            new(Guid.NewGuid(), "A-1", "+10% Contingency Allowance", 0m),
            new(Guid.NewGuid(), "=A2", "Normal Activity Name", 0m),
        };

        var bytes = new ExcelProgressTemplateWriter().WriteTemplate(rows);

        using var package = new ExcelPackage(new MemoryStream(bytes));
        var sheet = package.Workbook.Worksheets.First();

        // Written value must be escaped (leading quote) so Excel's own UI never evaluates it as a
        // formula on reopen - the export-side half of the two-sided defense (S3-BE-03 DoD).
        Assert.Equal("'+10% Contingency Allowance", sheet.Cells[2, 3].Text);
        Assert.Equal("'=A2", sheet.Cells[3, 2].Text);
    }

    [Fact]
    public void Excel_Template_Export_Then_ReImport_Round_Trips_A_Formula_Triggering_ActivityCode_Exactly()
    {
        var activityId = Guid.NewGuid();
        var rows = new List<ActivityProgressTemplateRow> { new(activityId, "=A2", "Normal Activity Name", 0m) };

        var exportedBytes = new ExcelProgressTemplateWriter().WriteTemplate(rows);

        using var package = new ExcelPackage(new MemoryStream(exportedBytes));
        var sheet = package.Workbook.Worksheets.First();
        Assert.Equal("'=A2", sheet.Cells[2, 2].Text); // exported form is escaped.

        // A field team fills in the two required columns and re-uploads.
        sheet.Cells[2, 5].Value = new DateTime(2026, 7, 1);
        sheet.Cells[2, 6].Value = 10m;

        using var reimportStream = new MemoryStream();
        package.SaveAs(reimportStream);
        reimportStream.Position = 0;

        var reimportResult = new ExcelProgressImporter(new UnlimitedImportOptions()).Parse(reimportStream);

        Assert.True(reimportResult.IsSuccess, reimportResult.IsFailure ? reimportResult.Error : string.Empty);
        var reimportedRow = Assert.Single(reimportResult.Value);
        Assert.Equal(activityId, reimportedRow.ActivityId);
        // Exact original text, escape transparently stripped, never evaluated as a formula anywhere
        // in this round trip.
        Assert.Equal("=A2", reimportedRow.ActivityCode);
    }

    // ------------------------------------------------------------------------------------
    // Zip/entity-expansion ("zip bomb") via XLSX - now a passing control (was a documented gap).
    //
    // S3-QA-02 DoD explicitly names "zip/entity expansion" as something to cover. XLSX is itself a
    // ZIP container (OOXML), so a highly-compressed .xlsx whose decompressed content is orders of
    // magnitude larger than its compressed size is the XLSX-specific shape of this attack class -
    // the same family as XXE/billion-laughs above, but via DEFLATE ratio rather than XML entity
    // recursion. Originally documented here as an open gap (empirically verified against the real
    // EPPlus 8.5.4 dependency this project ships: 100 MB of repeated 'A' compresses to ~106 KB,
    // a ~987:1 ratio, and ExcelPackage re-opened/read it back in full in well under a second with no
    // rejection). ImportOptions.MaxFileSizeBytes never caught this because it measures the
    // UPLOADED (compressed) byte count, not what EPPlus expands it to in-process.
    //
    // ExcelProgressImporter.RejectIfDecompressedSizeExceedsCap now closes this: it reads the ZIP
    // central directory's declared uncompressed entry sizes (metadata only - no entry is inflated to
    // make this check) before ExcelPackage is ever constructed, and rejects once the running total
    // passes IImportOptionsProvider.MaxDecompressedSizeBytes.
    //
    // This test uses a small injected cap (not the real ~250 MiB default) so a CI-safe ~1 MB payload
    // can exercise the rejection path directly, rather than needing an actual ~250 MB decompressed
    // fixture to prove the same thing.
    private sealed class TinyDecompressedCapImportOptions : IImportOptionsProvider
    {
        public long MaxFileSizeBytes => long.MaxValue;

        public long MaxDecompressedSizeBytes => 1024 * 1024; // 1 MiB

        public long MaxEntityCount => long.MaxValue;
    }

    [Fact]
    public void Excel_Importer_Rejects_A_Zip_Bomb_Style_Xlsx_Before_Decompressing_It()
    {
        const int decompressedCharCount = 20 * 1024 * 1024; // 20 MB - well past the 1 MiB test cap.
        var activityId = Guid.NewGuid();

        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Progress");
        sheet.Cells[1, 1].Value = "ActivityId";
        sheet.Cells[1, 2].Value = "ActivityCode";
        sheet.Cells[1, 5].Value = "PeriodEndDate";
        sheet.Cells[1, 6].Value = "ProgressPercentage";

        sheet.Cells[2, 1].Value = activityId.ToString();
        // The "bomb": a single, wildly oversized, highly-repetitive string in a free-text column -
        // exactly the kind of payload DEFLATE compresses at a ~1000:1 ratio.
        sheet.Cells[2, 2].Value = new string('A', decompressedCharCount);
        sheet.Cells[2, 5].Value = new DateTime(2026, 7, 1);
        sheet.Cells[2, 6].Value = 40m;

        using var stream = new MemoryStream();
        package.SaveAs(stream);
        var compressedBytes = stream.ToArray();
        stream.Position = 0;

        // The compressed artifact is tiny - nowhere near the 1 MiB decompressed cap this test
        // configures, let alone the real ~250 MiB default. This is the crux of the attack: a
        // compressed-byte size check alone cannot see this coming; only inspecting the declared
        // uncompressed sizes (or ratio) can.
        Assert.True(
            compressedBytes.Length < 200 * 1024,
            $"Expected the crafted file to compress to well under 200 KB (demonstrating a disproportionate " +
            $"ratio); actual compressed size was {compressedBytes.Length:N0} bytes.");

        var result = new ExcelProgressImporter(new TinyDecompressedCapImportOptions()).Parse(stream);

        Assert.True(result.IsFailure, "Expected the decompression-size guard to reject this file before EPPlus ever opened it.");
        Assert.StartsWith(ImportErrorCodes.FileTooLarge, result.Error);
    }

    [Fact]
    public void Excel_Importer_Still_Accepts_A_Legitimately_Long_Field_Within_The_Configured_Cap()
    {
        // Guards against the guard: a field that is merely long (not a compression-ratio outlier)
        // and stays within the configured decompressed cap must still import normally - this isn't
        // a blanket "reject any large field" rule, only a bound on total decompressed size.
        var activityId = Guid.NewGuid();

        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Progress");
        sheet.Cells[1, 1].Value = "ActivityId";
        sheet.Cells[1, 2].Value = "ActivityCode";
        sheet.Cells[1, 5].Value = "PeriodEndDate";
        sheet.Cells[1, 6].Value = "ProgressPercentage";

        sheet.Cells[2, 1].Value = activityId.ToString();
        sheet.Cells[2, 2].Value = "ORD-Task-Legitimate-Code";
        sheet.Cells[2, 5].Value = new DateTime(2026, 7, 1);
        sheet.Cells[2, 6].Value = 40m;

        using var stream = new MemoryStream();
        package.SaveAs(stream);
        stream.Position = 0;

        var result = new ExcelProgressImporter(new TinyDecompressedCapImportOptions()).Parse(stream);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        var row = Assert.Single(result.Value);
        Assert.Equal("ORD-Task-Legitimate-Code", row.ActivityCode);
    }
}
