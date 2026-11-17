using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.Runtime.Versioning;
using FastReport;
using FastReport.Export.Image;
using FastReport.Export.PdfSimple;
using FastReport.Utils;

namespace PetraERP.Reporting.OpenSource;

/// <summary>
/// Windows-only OSS renderer. Arabic shaping and printed output remain acceptance gates until
/// exported pages are visually inspected; no claim of PDF/A, encryption or digital signatures.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FastReportPdfExporter
{
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Exporters expose consistent instance entry points for application composition.")]
    public byte[] Export(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var report = Prepare(document);
        using var export = new PDFSimpleExport();
        using var output = new MemoryStream();
        report.Export(export, output);
        return output.ToArray();
    }

    /// <summary>Rasterizes the same prepared layout for visual QA without proprietary PDF/viewer components.</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Exporters expose consistent instance entry points for application composition.")]
    public IReadOnlyList<byte[]> ExportPageImages(ReportDocument document, int maximumPages = 10)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPages, 1);
        using var report = Prepare(document);
        var pages = new List<byte[]>();
        for (var index = 0; index < Math.Min(maximumPages, report.PreparedPages.Count); index++)
        {
            using var export = new ImageExport
            {
                ImageFormat = ImageExportFormat.Png, SeparateFiles = false,
                PageRange = PageRange.PageNumbers,
                PageNumbers = (index + 1).ToString(CultureInfo.InvariantCulture)
            };
            using var output = new MemoryStream();
            report.Export(export, output);
            pages.Add(output.ToArray());
        }
        return pages.AsReadOnly();
    }

    private static Report Prepare(ReportDocument document)
    {
        var report = new Report { DoublePass = true };
        try
        {
            using var table = new DataTable("Rows") { Locale = CultureInfo.InvariantCulture };
            for (var col = 0; col < document.Columns.Count; col++) table.Columns.Add($"F{col + 1:00}", typeof(string));
            foreach (var row in document.Rows)
            {
                var values = new object[document.Columns.Count];
                for (var col = 0; col < document.Columns.Count; col++)
                {
                    var definition = document.Columns[col];
                    row.TryGetValue(definition.Key, out var value);
                    values[col] = ReportValueFormatter.Display(value, definition);
                }
                table.Rows.Add(values);
            }
            report.RegisterData(table, "Rows");
            var source = report.GetDataSource("Rows");
            source.Enabled = true;

            var landscape = document.Columns.Count > 6;
            var page = new ReportPage
            {
                Name = "AccountingPage", Landscape = landscape,
                PaperWidth = landscape ? 297 : 210, PaperHeight = landscape ? 210 : 297,
                LeftMargin = 10, RightMargin = 10, TopMargin = 10, BottomMargin = 10
            };
            report.Pages.Add(page);
            var width = Units.Millimeters * (page.PaperWidth - page.LeftMargin - page.RightMargin);
            var colWidth = width / document.Columns.Count;
            page.ReportTitle = new ReportTitleBand { Name = "TitleBand", Height = Units.Millimeters * 26, CanGrow = true };
            var titleLabel = Label("ReportName", document.Title, 0, 0, width, 8, 15, true);
            titleLabel.TextColor = Color.FromArgb(30, 58, 138);
            page.ReportTitle.Objects.Add(titleLabel);
            page.ReportTitle.Objects.Add(Label("ReportCompany", document.CompanyLabel, 0, 9, width, 6, 10));
            var periodLabel = Label("ReportPeriod", document.PeriodLabel + " | " + document.CurrencyCode, 0, 16, width, 7, 9);
            periodLabel.RightToLeft = false; periodLabel.HorzAlign = HorzAlign.Right;
            page.ReportTitle.Objects.Add(periodLabel);
            var titleLine = new LineObject { Name = "TitleSep", Bounds = new RectangleF(0, 23 * Units.Millimeters, width, 0) };
            titleLine.Border.Color = Color.FromArgb(30, 58, 138);
            titleLine.Border.Width = 2;
            page.ReportTitle.Objects.Add(titleLine);
            var header = new PageHeaderBand { Name = "ColumnHeadings", Height = Units.Millimeters * 9, CanGrow = true };
            page.PageHeader = header;
            var dataBand = new DataBand { Name = "RowBand", Height = Units.Millimeters * 8, DataSource = source, CanGrow = true };
            page.Bands.Add(dataBand);
            for (var col = 0; col < document.Columns.Count; col++)
            {
                var position = (document.Columns.Count - col - 1) * colWidth;
                var definition = document.Columns[col];
                var heading = Label($"Header_{col}", definition.Heading, position, 0, colWidth, 9, 8, true);
                heading.HorzAlign = HorzAlign.Center;
                heading.Fill = new SolidFill(Color.FromArgb(241, 245, 249));
                heading.Border.Lines = BorderLines.All;
                heading.Border.Color = Color.FromArgb(203, 213, 225);
                heading.TextColor = Color.FromArgb(30, 41, 59);
                header.Objects.Add(heading);
                var numeric = definition.Kind is ReportValueKind.Amount or ReportValueKind.Quantity or ReportValueKind.Integer;
                var value = Label($"Value_{col}", $"[Rows.F{col + 1:00}]", position, 0, colWidth, 8, 8);
                // Only developer-authored field expressions are executable. Header/context strings
                // always use AllowExpressions=false, and field results are never parsed as scripts.
                value.AllowExpressions = true;
                value.RightToLeft = !numeric && definition.Kind != ReportValueKind.Date;
                value.HorzAlign = numeric ? HorzAlign.Center : value.RightToLeft ? RightToLeftStart : HorzAlign.Right;
                value.Border.Lines = BorderLines.All;
                value.Border.Color = Color.FromArgb(226, 232, 240);
                dataBand.Objects.Add(value);
            }
            AddSummary(page, document, width);
            page.PageFooter = new PageFooterBand { Name = "Footer", Height = Units.Millimeters * 16, CanGrow = true };
            var executionFooter = Label("ExecutionFooter", document.ExecutionId.ToString("D") + " | " +
                document.GeneratedAtUtc.ToString("u", CultureInfo.InvariantCulture), 0, 0, width, 7, 7);
            executionFooter.RightToLeft = false; executionFooter.HorzAlign = HorzAlign.Right;
            page.PageFooter.Objects.Add(executionFooter);
            var pageNumber = Label("PageNumber", "[Page#] / [TotalPages#]", 0, 8, width, 7, 8);
            pageNumber.AllowExpressions = true;
            pageNumber.RightToLeft = false;
            pageNumber.HorzAlign = HorzAlign.Center;
            page.PageFooter.Objects.Add(pageNumber);
            if (!report.Prepare()) throw new InvalidOperationException("FastReport could not prepare the supplied immutable snapshot.");
            return report;
        }
        catch
        {
            report.Dispose();
            throw;
        }
    }

    private static void AddSummary(ReportPage page, ReportDocument document, float width)
    {
        if (document.Summaries.Count == 0) return;
        page.ReportSummary = new ReportSummaryBand
        {
            Name = "AuthoritativeSummary", Height = Units.Millimeters * (12 + document.Summaries.Count * 9), CanGrow = true
        };
        var title = Label("SummaryExplanation", "المجاميع والمقاييس المعتمدة من الخادم", 0, 1, width, 7, 9, true);
        title.TextColor = Color.FromArgb(30, 58, 138);
        page.ReportSummary.Objects.Add(title);
        for (var index = 0; index < document.Summaries.Count; index++)
        {
            var measure = document.Summaries[index];
            var summaryCard = Label($"Measure_{index}", measure.Label + " : " +
                measure.Value.ToString(CultureInfo.InvariantCulture) + " " + measure.CurrencyCode,
                0, 9 + index * 9, width, 8, 9, bold: true);
            summaryCard.Fill = new SolidFill(Color.FromArgb(248, 250, 252));
            summaryCard.Border.Lines = BorderLines.All;
            summaryCard.Border.Color = Color.FromArgb(203, 213, 225);
            page.ReportSummary.Objects.Add(summaryCard);
        }
    }

    // With RightToLeft the renderer mirrors alignment: "Left" is the reading start, which is the right edge on the page.
    private const HorzAlign RightToLeftStart = HorzAlign.Left;

    private static TextObject Label(string name, string text, float x, float yMillimeters, float width, float heightMillimeters, float fontSize, bool bold = false) => new()
    {
        Name = name, Text = text, AllowExpressions = false, RightToLeft = true,
        TextRenderType = TextRenderType.Default, CanGrow = true,
        Bounds = new RectangleF(x, yMillimeters * Units.Millimeters, width, heightMillimeters * Units.Millimeters),
        Font = new Font("Tahoma", fontSize, bold ? FontStyle.Bold : FontStyle.Regular), HorzAlign = RightToLeftStart
    };
}
