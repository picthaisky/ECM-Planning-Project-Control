using CMPlus.Application.Abstractions;
using CMPlus.Application.Import;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace CMPlus.Infrastructure.Parsers.Excel;

/// <summary>
/// S3-BE-03/US-3.3 (export half): renders the blank progress-update template. Column layout (row 1
/// = header, data from row 2): <c>ActivityId</c> (hidden, machine key), <c>ActivityCode</c>,
/// <c>ActivityName</c>, <c>CurrentProgressPercentage</c> (informational only - ignored on
/// re-import), <c>PeriodEndDate</c>/<c>ProgressPercentage</c>/<c>ActualQuantity</c> (the three the
/// field team fills in). <see cref="ExcelProgressImporter"/> is this writer's exact inverse.
/// </summary>
public sealed class ExcelProgressTemplateWriter : IExcelProgressTemplateWriter
{
    private const int ColActivityId = 1;
    private const int ColActivityCode = 2;
    private const int ColActivityName = 3;
    private const int ColCurrentProgressPercentage = 4;
    private const int ColPeriodEndDate = 5;
    private const int ColProgressPercentage = 6;
    private const int ColActualQuantity = 7;

    public byte[] WriteTemplate(IReadOnlyList<ActivityProgressTemplateRow> rows)
    {
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Progress");

        string[] headers =
        [
            "ActivityId", "ActivityCode", "ActivityName", "CurrentProgressPercentage",
            "PeriodEndDate", "ProgressPercentage", "ActualQuantity",
        ];

        for (var col = 0; col < headers.Length; col++)
        {
            var cell = sheet.Cells[1, col + 1];
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(0xF7, 0xF8, 0xFA));
        }

        // Free-text columns get an explicit Text number format so Excel never re-interprets a
        // legitimately '='/'+'/'-'/'@'-led ActivityCode/ActivityName as a formula on reopen -
        // belt-and-braces alongside the per-value leading-quote escape below (S3-BE-03 DoD: the
        // export side of the two-sided formula-injection defense).
        sheet.Column(ColActivityCode).Style.Numberformat.Format = "@";
        sheet.Column(ColActivityName).Style.Numberformat.Format = "@";
        sheet.Column(ColPeriodEndDate).Style.Numberformat.Format = "yyyy-mm-dd";

        var row = 2;
        foreach (var activity in rows)
        {
            sheet.Cells[row, ColActivityId].Value = activity.ActivityId.ToString();
            sheet.Cells[row, ColActivityCode].Value = FormulaInjectionGuard.EscapeForExport(activity.ActivityCode);
            sheet.Cells[row, ColActivityName].Value = FormulaInjectionGuard.EscapeForExport(activity.ActivityName);
            sheet.Cells[row, ColCurrentProgressPercentage].Value = activity.CurrentProgressPercentage;

            // PeriodEndDate/ProgressPercentage/ActualQuantity are deliberately left blank - the
            // field team fills these in before re-uploading.
            row++;
        }

        sheet.Column(ColActivityId).Hidden = true;

        for (var col = 1; col <= headers.Length; col++)
        {
            sheet.Column(col).Width = col == ColActivityId ? 0 : 22;
        }

        sheet.View.FreezePanes(2, 1);

        return package.GetAsByteArray();
    }
}
