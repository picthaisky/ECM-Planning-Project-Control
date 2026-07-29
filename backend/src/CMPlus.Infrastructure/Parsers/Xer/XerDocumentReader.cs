using System.Text;
using CMPlus.Application.Import;
using CMPlus.Domain.Common;

namespace CMPlus.Infrastructure.Parsers.Xer;

/// <summary>
/// Tokenizes a raw <c>.XER</c> stream into an <see cref="XerDocument"/> (S3-BE-01). Per the P6 XER
/// spec: <c>%T</c> starts a new table section, the very next non-<c>%T</c> line is the <c>%F</c>
/// field-name header for that table, and every subsequent <c>%R</c> line is one data row until the
/// next <c>%T</c>. Lines that start with anything else (the <c>ERMHDR</c> preamble, a trailing
/// <c>%E</c> marker some exporters emit) are skipped - this parser only needs the tables it reads
/// in <see cref="XerScheduleParser"/>, not full XER fidelity.
/// </summary>
internal static class XerDocumentReader
{
    public static Result<XerDocument> Read(Stream content)
    {
        var document = new XerDocument();

        // P6 exports XER as Windows-1252; our own hand-built fixtures are plain ASCII/UTF-8, which
        // is a strict subset, so UTF-8 (no BOM assumption) reads either correctly.
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        XerTable? currentTable = null;
        string[]? currentFields = null;
        var lineNumber = 0;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('\t');
            switch (parts[0])
            {
                case "%T":
                    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        return Result<XerDocument>.Failure(
                            $"{ImportErrorCodes.MalformedFile}: line {lineNumber} is a %T record with no table name.");
                    }

                    currentTable = document.GetOrAddTable(parts[1].Trim());
                    currentFields = null;
                    break;

                case "%F":
                    if (currentTable is null)
                    {
                        return Result<XerDocument>.Failure(
                            $"{ImportErrorCodes.MalformedFile}: line {lineNumber} is a %F header before any %T table.");
                    }

                    currentFields = parts.Skip(1).ToArray();
                    break;

                case "%R":
                    if (currentTable is null || currentFields is null)
                    {
                        return Result<XerDocument>.Failure(
                            $"{ImportErrorCodes.MalformedFile}: line {lineNumber} is a %R data row before a %T/%F header.");
                    }

                    var values = parts.Skip(1).ToArray();
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < currentFields.Length; i++)
                    {
                        row[currentFields[i]] = i < values.Length ? values[i] : string.Empty;
                    }

                    currentTable.Rows.Add(new XerRow(row));
                    break;

                default:
                    // ERMHDR preamble, %E, or any other tag this parser has no use for.
                    break;
            }
        }

        return Result<XerDocument>.Success(document);
    }
}
