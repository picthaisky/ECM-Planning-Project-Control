namespace CMPlus.Infrastructure.Parsers.Excel;

/// <summary>
/// The two-sided formula-injection defense shared by the importer and template writer (S3-BE-03
/// DoD; the general rule is already recorded in .claude/knowledge/patterns/conventions.md: "Excel/
/// CSV export escapes cells starting with =, +, -, @"). Import and export are two different halves
/// of the same OWASP CSV/Excel-injection concern: any cell whose text begins with one of those four
/// characters is rendered/evaluated as a formula (or, historically, a DDE command) by Excel's own
/// UI when a human opens the file - regardless of how the underlying XLSX XML actually typed the
/// cell.
/// </summary>
internal static class FormulaInjectionGuard
{
    private static readonly char[] DangerousLeadingCharacters = ['=', '+', '-', '@'];

    /// <summary>True if <paramref name="text"/> would be interpreted as a formula by Excel's UI on open.</summary>
    public static bool StartsWithFormulaTrigger(string? text) =>
        !string.IsNullOrEmpty(text) && DangerousLeadingCharacters.Contains(text[0]);

    /// <summary>
    /// Export-side escape: prefixes a leading single quote so Excel displays the value as plain
    /// text instead of evaluating it as a formula on open - e.g. an Activity whose name legitimately
    /// starts with <c>+</c> or <c>=</c> must still round-trip safely. Combined with setting the
    /// column's number format to <c>"@"</c> (Text) in <see cref="ExcelProgressTemplateWriter"/> for
    /// defense in depth.
    /// </summary>
    public static string EscapeForExport(string value) =>
        StartsWithFormulaTrigger(value) ? "'" + value : value;

    /// <summary>
    /// Import-side: undoes our own export-time escape if present (so an activity template we
    /// exported ourselves round-trips back to the exact original text, not a leading quote glued on
    /// forever), while leaving any other text - including a third-party file's raw
    /// <c>=</c>/<c>+</c>/<c>-</c>/<c>@</c>-led value - completely untouched and unexecuted: this
    /// importer never calls <see cref="OfficeOpenXml.ExcelWorksheet.Calculate"/>/<c>CalculateAsync</c>
    /// anywhere, so even a genuine formula-typed cell can only ever yield its pre-existing cached
    /// text, never a freshly executed result (S3-BE-03 DoD: "read as literal text").
    /// </summary>
    public static string StripOwnEscapeIfPresent(string text) =>
        text.Length > 1 && text[0] == '\'' && StartsWithFormulaTrigger(text[1..]) ? text[1..] : text;
}
