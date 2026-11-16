using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ClosedXML.Excel;

namespace PetraERP.Reporting.OpenSource;

public enum ExcelExportLayout { Analytical, PrintLayout }
public sealed record ExcelPrecisionWarning(string ColumnKey, int AffectedCells);
public sealed record ExcelExportResult(byte[] Content, IReadOnlyList<ExcelPrecisionWarning> PrecisionWarnings);

/// <summary>
/// Exports the same immutable snapshot to three RTL sheets. Excel's 15-digit numeric boundary
/// uses an explicit lossless text fallback, highlighted and disclosed in Filters, never rounding.
/// </summary>
public sealed class ExcelReportExporter
{
    public byte[] Export(ReportDocument report, ExcelExportLayout layout = ExcelExportLayout.Analytical) => ExportDetailed(report, layout).Content;

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Exporters expose consistent instance entry points for application composition.")]
    public ExcelExportResult ExportDetailed(ReportDocument report, ExcelExportLayout layout = ExcelExportLayout.Analytical)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!Enum.IsDefined(layout)) throw new ArgumentOutOfRangeException(nameof(layout));
        using var workbook = new XLWorkbook();
        workbook.Style.Font.FontName = "Tahoma";
        workbook.Style.Font.FontSize = 10;
        var warnings = new Dictionary<string, int>(StringComparer.Ordinal);
        WriteTransactions(workbook.Worksheets.Add("Transactions"), report, layout, warnings);
        WriteSummary(workbook.Worksheets.Add("Summary"), report, warnings);
        WriteFilters(workbook.Worksheets.Add("Filters"), report, layout, warnings);
        using var stream = new MemoryStream();
        // Save options deliberately never evaluate formulas. Every supplied string is a text cell.
        workbook.SaveAs(stream, new SaveOptions { EvaluateFormulasBeforeSaving = false });
        return new ExcelExportResult(stream.ToArray(), Array.AsReadOnly(warnings.Select(x => new ExcelPrecisionWarning(x.Key, x.Value)).ToArray()));
    }

    private static void WriteTransactions(IXLWorksheet sheet, ReportDocument report, ExcelExportLayout layout, Dictionary<string, int> warnings)
    {
        sheet.RightToLeft = true;
        var columns = report.Columns.Count;
        var headerRow = layout == ExcelExportLayout.Analytical ? 1 : 7;
        if (layout == ExcelExportLayout.PrintLayout)
        {
            MergeText(sheet, 1, columns, report.Title);
            sheet.Cell(1, 1).Style.Font.FontSize = 16;
            sheet.Cell(1, 1).Style.Font.Bold = true;
            MergeText(sheet, 2, columns, report.CompanyLabel);
            MergeText(sheet, 3, columns, report.PeriodLabel + " | " + report.CurrencyCode);
            MergeText(sheet, 4, columns, "Execution: " + report.ExecutionId.ToString("D"));
            MergeText(sheet, 5, columns, report.GeneratedAtUtc.ToString("u", CultureInfo.InvariantCulture));
        }
        for (var col = 0; col < columns; col++) SetText(sheet.Cell(headerRow, col + 1), report.Columns[col].Heading);
        var header = sheet.Range(headerRow, 1, headerRow, columns);
        header.Style.Font.SetBold();
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E5EDF3");
        header.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        sheet.Row(headerRow).Height = 26;

        for (var row = 0; row < report.Rows.Count; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                var definition = report.Columns[col];
                report.Rows[row].TryGetValue(definition.Key, out var value);
                WriteValue(sheet.Cell(headerRow + row + 1, col + 1), value, definition, warnings);
            }
        }
        if (report.Rows.Count > 0) sheet.Range(headerRow, 1, headerRow + report.Rows.Count, columns).SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRow);
        for (var col = 0; col < columns; col++)
        {
            var definition = report.Columns[col];
            sheet.Column(col + 1).Width = definition.Kind == ReportValueKind.Text ? 34 : 22;
            if (definition.Kind == ReportValueKind.Amount)
            {
                var fraction = definition.DecimalPlaces == 0 ? string.Empty : "." + new string('0', definition.DecimalPlaces);
                sheet.Column(col + 1).Style.NumberFormat.Format = "#,##0" + fraction + ";[Red](#,##0" + fraction + ");0";
            }
            else if (definition.Kind == ReportValueKind.Quantity) sheet.Column(col + 1).Style.NumberFormat.Format = "#,##0.########";
            else if (definition.Kind == ReportValueKind.Integer) sheet.Column(col + 1).Style.NumberFormat.Format = "0";
            else if (definition.Kind == ReportValueKind.Date) sheet.Column(col + 1).Style.DateFormat.Format = "yyyy-mm-dd";
        }
        // Text fallback formats must follow whole-column formats, so numeric-looking text stays text.
        sheet.PageSetup.PageOrientation = columns > 6 ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(1, headerRow);
        sheet.PageSetup.PrintAreas.Add(1, 1, Math.Max(headerRow, headerRow + report.Rows.Count), columns);
    }

    private static void WriteSummary(IXLWorksheet sheet, ReportDocument report, Dictionary<string, int> warnings)
    {
        sheet.RightToLeft = true;
        SetText(sheet.Cell(1, 1), report.Title);
        SetText(sheet.Cell(2, 1), report.CompanyLabel);
        SetText(sheet.Cell(3, 1), report.PeriodLabel);
        SetText(sheet.Cell(4, 1), "قيم معتمدة من الخادم؛ ليست مجاميع محسوبة من الحركات المصفاة");
        SetText(sheet.Cell(6, 1), "المقياس");
        SetText(sheet.Cell(6, 2), "القيمة");
        SetText(sheet.Cell(6, 3), "العملة");
        var row = 7;
        foreach (var measure in report.Summaries)
        {
            SetText(sheet.Cell(row, 1), measure.Label);
            WriteNumber(sheet.Cell(row, 2), measure.Value, "Summary_" + measure.Key, warnings);
            SetText(sheet.Cell(row++, 3), measure.CurrencyCode);
        }
        sheet.Column(1).Width = 60;
        sheet.Column(2).Width = 30;
        sheet.Column(3).Width = 18;
        sheet.SheetView.FreezeRows(6);
    }

    private static void WriteFilters(IXLWorksheet sheet, ReportDocument report, ExcelExportLayout layout, Dictionary<string, int> warnings)
    {
        sheet.RightToLeft = true;
        var metadata = new List<KeyValuePair<string, string>>
        {
            new("رمز التقرير", report.ReportCode), new("معرف التنفيذ", report.ExecutionId.ToString("D")),
            new("الشركة", report.CompanyLabel), new("الفترة", report.PeriodLabel), new("العملة", report.CurrencyCode),
            new("وقت الاستخراج UTC", report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
            new("الإخراج", layout.ToString()), new("عدد الحركات", report.Rows.Count.ToString(CultureInfo.InvariantCulture)),
            new("دقة الأرقام", "الأرقام حتى 15 خانة معنوية رقمية؛ القيم الأوسع نص كامل دون تقريب ومميزة باللون البرتقالي.")
        };
        if (report.Context is { } context)
        {
            metadata.Add(new("CompanyId", context.CompanyId.ToString("D")));
            metadata.Add(new("FiscalYearId", context.FiscalYearId.ToString("D")));
            metadata.Add(new("BranchId", context.BranchId?.ToString("D") ?? string.Empty));
            metadata.Add(new("ActorId", context.ActorId?.ToString("D") ?? string.Empty));
        }
        metadata.AddRange(report.Filters);
        metadata.AddRange(warnings.Select(x => new KeyValuePair<string, string>("LOSSLESS_TEXT: " + x.Key, x.Value.ToString(CultureInfo.InvariantCulture))));
        for (var row = 0; row < metadata.Count; row++)
        {
            SetText(sheet.Cell(row + 1, 1), metadata[row].Key);
            SetText(sheet.Cell(row + 1, 2), metadata[row].Value);
        }
        sheet.Column(1).Width = 36;
        sheet.Column(2).Width = 80;
        sheet.Column(2).Style.Alignment.WrapText = true;
    }

    private static void WriteValue(IXLCell cell, object? value, ReportColumn column, Dictionary<string, int> warnings)
    {
        if (value is null) return;
        if (column.Kind == ReportValueKind.Text) SetText(cell, ReportValueFormatter.Lossless(value, column));
        else if (column.Kind == ReportValueKind.Date) cell.Value = ReportDocument.DateValue(value);
        else WriteNumber(cell, ReportDocument.DecimalValue(value), column.Key, warnings);
    }

    private static void WriteNumber(IXLCell cell, decimal value, string key, Dictionary<string, int> warnings)
    {
        // A G15 round trip models Excel's significant-digit constraint, including exponent forms.
        var number = (double)value;
        if (decimal.TryParse(number.ToString("G15", CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var roundTrip) && roundTrip == value)
            cell.Value = number;
        else
        {
            SetText(cell, value.ToString(CultureInfo.InvariantCulture));
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF1D6");
            warnings.TryGetValue(key, out var count);
            warnings[key] = count + 1;
        }
    }

    private static void MergeText(IXLWorksheet sheet, int row, int columns, string text)
    {
        SetText(sheet.Cell(row, 1), text);
        if (columns > 1) sheet.Range(row, 1, row, columns).Merge();
    }

    private static void SetText(IXLCell cell, string value)
    {
        if (value.Length > 32_767) throw new InvalidOperationException("Excel text exceeds its 32,767 character cell limit; use CSV for lossless export.");
        // SetValue(string) always creates a text cell; no prefix modifies the original Arabic/data.
        cell.SetValue(value);
    }
}
