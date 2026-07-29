using CMPlus.Application.Import;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Renders the progress-update Excel template for export (S3-BE-03, US-3.3). Implemented via
/// EPPlus in <c>CMPlus.Infrastructure.Parsers.Excel</c>. Any <see cref="ActivityProgressTemplateRow"/>
/// field whose text legitimately starts with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> (e.g. an
/// <c>ActivityCode</c> or <c>ActivityName</c> that happens to begin with one of those characters)
/// must be escaped in the written cell so re-opening the file in Excel never interprets it as a
/// formula - the two-sided half of the S3-BE-03 formula-injection defense (import already treats
/// such a value as literal text; export must not re-introduce the risk on the way out).
/// </summary>
public interface IExcelProgressTemplateWriter
{
    byte[] WriteTemplate(IReadOnlyList<ActivityProgressTemplateRow> rows);
}
