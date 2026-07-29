using CMPlus.Application.Import;
using CMPlus.Domain.Common;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Parses a re-uploaded progress-update Excel template into <see cref="ParsedProgressRow"/> rows
/// (S3-BE-03, US-3.3). Implemented via EPPlus in <c>CMPlus.Infrastructure.Parsers.Excel</c>. Pure
/// parsing only - no DB access, no <c>ActivityProgressLog</c> writes here (ADR-0001; the command
/// handler resolves each <see cref="ParsedProgressRow.ActivityId"/> and calls
/// <c>Activity.RecordProgress</c> itself, per US-3.3/ADR-0009: never write the cache directly).
/// </summary>
public interface IExcelProgressImporter
{
    /// <summary><paramref name="excelContent"/> must already have passed the caller's file-size-cap
    /// check. A cell whose raw text starts with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is returned
    /// as literal text - the implementation must read cell <em>values</em> (EPPlus already refuses
    /// to evaluate a cell as a formula unless its raw content is asked for), never re-interpret a
    /// stored formula result as executable.</summary>
    Result<IReadOnlyList<ParsedProgressRow>> Parse(Stream excelContent);
}
