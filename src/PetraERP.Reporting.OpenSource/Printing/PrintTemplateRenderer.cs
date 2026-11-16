using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using FastReport;
using FastReport.Export.Image;
using FastReport.Export.PdfSimple;
using FastReport.Utils;

namespace PetraERP.Reporting.OpenSource.Printing;

/// <summary>
/// Renders a <see cref="PrintTemplate"/> with a <see cref="PrintData"/> through FastReport. The same prepared layout feeds the page images
/// (preview and printing) and the PDF, so what is previewed is what is printed. The renderer prints the supplied strings exactly: it never
/// formats numbers, recalculates a total or runs anything found in the data (data fields are rendered, never parsed as expressions).
/// </summary>
[SupportedOSPlatform("windows")]
public static class PrintTemplateRenderer
{
    public const int MaximumPages = 200;

    private static readonly Regex Token = new(@"\[([A-Za-z]+(?:\.[A-Za-z]+)?#?)\]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>Renders every page as a PNG at the given resolution (150 for preview, 200 or more for printing).</summary>
    public static IReadOnlyList<byte[]> RenderPages(PrintTemplate template, PrintData data, int resolution = 150)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfLessThan(resolution, 50);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(resolution, 600);
        using var report = Prepare(template.Validated(), data);
        var pages = new List<byte[]>();
        for (var index = 0; index < Math.Min(MaximumPages, report.PreparedPages.Count); index++)
        {
            using var export = new ImageExport
            {
                ImageFormat = ImageExportFormat.Png, SeparateFiles = false, Resolution = resolution,
                PageRange = PageRange.PageNumbers, PageNumbers = (index + 1).ToString(CultureInfo.InvariantCulture),
            };
            using var output = new MemoryStream();
            report.Export(export, output);
            pages.Add(output.ToArray());
        }

        return pages;
    }

    /// <summary>Renders the document as a PDF.</summary>
    public static byte[] RenderPdf(PrintTemplate template, PrintData data)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(data);
        using var report = Prepare(template.Validated(), data);
        using var export = new PDFSimpleExport();
        using var output = new MemoryStream();
        report.Export(export, output);
        return output.ToArray();
    }

    private static Report Prepare(PrintTemplate template, PrintData data)
    {
        var report = new Report { DoublePass = true };
        try
        {
            var settings = template.Page;
            var table = new System.Data.DataTable("Lines") { Locale = CultureInfo.InvariantCulture };
            for (var column = 0; column < template.Table.Columns.Count; column++) table.Columns.Add(ColumnName(column), typeof(string));
            foreach (var line in data.Lines)
            {
                var values = new object[template.Table.Columns.Count];
                for (var column = 0; column < values.Length; column++)
                    values[column] = line.TryGetValue(template.Table.Columns[column].Field, out var value) ? value : string.Empty;
                table.Rows.Add(values);
            }

            report.RegisterData(table, "Lines");
            var source = report.GetDataSource("Lines");
            source.Enabled = true;

            var page = new ReportPage
            {
                Name = "PrintPage", Landscape = settings.Landscape, TitleBeforeHeader = false,
                PaperWidth = (float)settings.EffectivePaperWidthMm, PaperHeight = (float)settings.EffectivePaperHeightMm,
                LeftMargin = (float)settings.MarginLeftMm, RightMargin = (float)settings.MarginRightMm,
                TopMargin = (float)settings.MarginTopMm, BottomMargin = (float)settings.MarginBottomMm,
            };
            report.Pages.Add(page);
            var width = (float)settings.PrintableWidthMm;
            var counter = 0;

            if (template.PageHeader.HeightMm > 0)
            {
                page.PageHeader = new PageHeaderBand { Name = "PageHeader", Height = Mm(template.PageHeader.HeightMm) };
                AddElements(page.PageHeader.Objects, template.PageHeader, template, data, ref counter);
            }

            if (template.DocumentHeader.HeightMm > 0)
            {
                page.ReportTitle = new ReportTitleBand { Name = "DocumentHeader", Height = Mm(template.DocumentHeader.HeightMm) };
                AddElements(page.ReportTitle.Objects, template.DocumentHeader, template, data, ref counter);
            }

            if (template.Table.Columns.Count > 0)
            {
                var widths = ScaledWidths(template.Table, width);
                var heading = new ColumnHeaderBand { Name = "TableHeader", Height = Mm(template.Table.HeaderHeightMm) };
                page.ColumnHeader = heading;
                var rows = new DataBand { Name = "TableRows", Height = Mm(template.Table.RowHeightMm), DataSource = source, CanGrow = true };
                page.Bands.Add(rows);
                var offset = 0d;
                for (var column = 0; column < template.Table.Columns.Count; column++)
                {
                    var definition = template.Table.Columns[column];
                    var x = settings.RightToLeft ? width - offset - widths[column] : offset;
                    offset += widths[column];
                    heading.Objects.Add(Cell($"Head{column}", definition.Heading, x, widths[column], template.Table.HeaderHeightMm, template, definition.Align, bold: true,
                        fill: Parse(template.Table.HeaderFill, Color.Empty), expression: false));
                    rows.Objects.Add(Cell($"Cell{column}", $"[Lines.{ColumnName(column)}]", x, widths[column], template.Table.RowHeightMm, template, definition.Align, bold: false,
                        fill: Color.Empty, expression: true));
                }
            }

            if (template.DocumentFooter.HeightMm > 0)
            {
                page.ReportSummary = new ReportSummaryBand { Name = "DocumentFooter", Height = Mm(template.DocumentFooter.HeightMm) };
                AddElements(page.ReportSummary.Objects, template.DocumentFooter, template, data, ref counter);
            }

            if (template.PageFooter.HeightMm > 0)
            {
                page.PageFooter = new PageFooterBand { Name = "PageFooter", Height = Mm(template.PageFooter.HeightMm) };
                AddElements(page.PageFooter.Objects, template.PageFooter, template, data, ref counter);
            }

            if (!report.Prepare()) throw new InvalidOperationException("FastReport could not prepare the print template.");
            return report;
        }
        catch
        {
            report.Dispose();
            throw;
        }
    }

    private static void AddElements(ReportComponentCollection target, PrintBand band, PrintTemplate template, PrintData data, ref int counter)
    {
        foreach (var element in band.Elements)
        {
            counter++;
            var name = "E" + counter.ToString(CultureInfo.InvariantCulture);
            var bounds = new RectangleF(Mm(element.XMm), Mm(element.YMm), Mm(element.WidthMm), Mm(element.HeightMm));
            switch (element.Type)
            {
                case PrintElementType.Text:
                    target.Add(TextElement(name, element, bounds, template, data));
                    break;
                case PrintElementType.Image:
                    if (ImageElement(name, element, bounds) is { } picture) target.Add(picture);
                    break;
                case PrintElementType.Line:
                    var line = new LineObject { Name = name, Bounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, 0) };
                    line.Border.Color = Parse(element.Color, Color.Black);
                    line.Border.Width = 1;
                    target.Add(line);
                    break;
                default:
                    var shape = new ShapeObject { Name = name, Shape = ShapeKind.Rectangle, Bounds = bounds };
                    shape.Border.Color = Parse(element.Color, Color.Black);
                    if (Parse(element.Fill, Color.Empty) is { IsEmpty: false } fill) shape.Fill = new SolidFill(fill);
                    target.Add(shape);
                    break;
            }
        }
    }

    private static TextObject TextElement(string name, PrintElement element, RectangleF bounds, PrintTemplate template, PrintData data)
    {
        var text = Substitute(element.Text, data, out var usesPageNumbers);
        var family = string.IsNullOrWhiteSpace(element.FontFamily) ? template.Page.FontFamily : element.FontFamily;
        var style = (element.Bold ? FontStyle.Bold : FontStyle.Regular) | (element.Italic ? FontStyle.Italic : FontStyle.Regular);
        var label = new TextObject
        {
            Name = name, Text = text, AllowExpressions = usesPageNumbers, Bounds = bounds, WordWrap = true, CanGrow = false,
            RightToLeft = template.Page.RightToLeft, TextRenderType = TextRenderType.Default,
            Font = new Font(family, (float)element.FontSize, style), TextColor = Parse(element.Color, Color.Black),
            HorzAlign = Horizontal(element.Align, template.Page.RightToLeft),
        };
        if (Parse(element.Fill, Color.Empty) is { IsEmpty: false } fill) label.Fill = new SolidFill(fill);
        if (element.Border) { label.Border.Lines = BorderLines.All; label.Border.Color = Parse(element.Color, Color.Black); }
        return label;
    }

    private static PictureObject? ImageElement(string name, PrintElement element, RectangleF bounds)
    {
        if (string.IsNullOrEmpty(element.ImageBase64)) return null;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(element.ImageBase64));
            using var source = Image.FromStream(stream);
            // Fit the picture inside its box keeping the aspect ratio (scaled here: the box itself stays exactly where the designer put it).
            var scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
            var fitted = new RectangleF(bounds.X, bounds.Y, source.Width * scale, source.Height * scale);
            return new PictureObject { Name = name, Bounds = fitted, Image = new Bitmap(source) };
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or ExternalException or OutOfMemoryException)
        {
            // A damaged picture must not stop a document from printing: the element is skipped.
            return null;
        }
    }

    private static TextObject Cell(string name, string text, double x, double widthMm, double heightMm, PrintTemplate template, PrintAlign align, bool bold, Color fill, bool expression)
    {
        var cell = new TextObject
        {
            Name = name, Text = text, AllowExpressions = expression, WordWrap = true, CanGrow = expression, CanBreak = false,
            Bounds = new RectangleF(Mm(x), 0, Mm(widthMm), Mm(heightMm)), RightToLeft = template.Page.RightToLeft, TextRenderType = TextRenderType.Default,
            Font = new Font(template.Page.FontFamily, (float)template.Table.FontSize, bold ? FontStyle.Bold : FontStyle.Regular), HorzAlign = Horizontal(align, template.Page.RightToLeft),
            VertAlign = VertAlign.Center,
        };
        if (template.Table.Borders) cell.Border.Lines = BorderLines.All;
        if (!fill.IsEmpty) cell.Fill = new SolidFill(fill);
        return cell;
    }

    /// <summary>
    /// Replaces <c>[Field]</c> tokens by their values. Page numbers stay FastReport variables and make the element an expression, so any
    /// bracket inside a substituted value is neutralized there.
    /// </summary>
    public static string Substitute(string text, PrintData data, out bool usesPageNumbers)
    {
        var pageNumbers = Token.IsMatch(text) && (text.Contains("[Page#]", StringComparison.Ordinal) || text.Contains("[TotalPages#]", StringComparison.Ordinal));
        usesPageNumbers = pageNumbers;
        return Token.Replace(text, match =>
        {
            var key = match.Groups[1].Value;
            if (key is PrintFieldCatalog.PageNumber or PrintFieldCatalog.TotalPages) return match.Value;
            if (!data.Fields.TryGetValue(key, out var value)) return match.Value;
            return pageNumbers ? value.Replace('[', '(').Replace(']', ')') : value;
        });
    }

    private static double[] ScaledWidths(PrintTable table, double printableWidth)
    {
        var total = table.Columns.Sum(column => column.WidthMm);
        var scale = total > 0 ? printableWidth / total : 1;
        return [.. table.Columns.Select(column => column.WidthMm * scale)];
    }

    private static string ColumnName(int index) => "F" + (index + 1).ToString("00", CultureInfo.InvariantCulture);

    private static float Mm(double millimeters) => (float)millimeters * Units.Millimeters;

    private static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try { return ColorTranslator.FromHtml(value); }
        catch (Exception exception) when (exception is ArgumentException or FormatException) { return fallback; }
    }

    /// <summary>
    /// Visual alignment on the paper. FastReport mirrors horizontal alignment for right-to-left text ("Left" is the reading start, the right
    /// edge), so the visual sides are swapped there.
    /// </summary>
    private static HorzAlign Horizontal(PrintAlign align, bool rightToLeft) => align switch
    {
        PrintAlign.Center => HorzAlign.Center,
        PrintAlign.Left => rightToLeft ? HorzAlign.Right : HorzAlign.Left,
        _ => rightToLeft ? HorzAlign.Left : HorzAlign.Right,
    };
}
