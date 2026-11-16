using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetraERP.Reporting.OpenSource.Printing;

/// <summary>The business documents that have print templates (an Al-Ameen "نمط طباعة" per document kind).</summary>
public enum PrintDocumentKind
{
    SalesInvoice = 0,
    PurchaseInvoice = 1,
    ReceiptVoucher = 2,
    PaymentVoucher = 3,
}

/// <summary>What a template element draws.</summary>
public enum PrintElementType
{
    /// <summary>Static text and/or <c>[Field]</c> tokens such as <c>[Doc.Number]</c> or <c>[Page#]</c>.</summary>
    Text = 0,
    /// <summary>A picture (logo, stamp, header image) stored inside the template.</summary>
    Image = 1,
    /// <summary>A horizontal rule.</summary>
    Line = 2,
    /// <summary>A rectangle (frame or filled box).</summary>
    Rectangle = 3,
}

/// <summary>Visual alignment on the paper (left / right edge), independent of the text direction.</summary>
public enum PrintAlign
{
    Left = 0,
    Center = 1,
    Right = 2,
}

/// <summary>Paper and text settings of a template.</summary>
public sealed record PrintPageSettings
{
    public double PaperWidthMm { get; init; } = 210;
    public double PaperHeightMm { get; init; } = 297;
    public bool Landscape { get; init; }
    public double MarginLeftMm { get; init; } = 10;
    public double MarginRightMm { get; init; } = 10;
    public double MarginTopMm { get; init; } = 10;
    public double MarginBottomMm { get; init; } = 10;
    /// <summary>Right-to-left text (Arabic) for every element.</summary>
    public bool RightToLeft { get; init; } = true;
    public string FontFamily { get; init; } = "Tahoma";

    /// <summary>Printable width in millimeters (after margins), honoring the orientation.</summary>
    public double PrintableWidthMm => EffectivePaperWidthMm - MarginLeftMm - MarginRightMm;

    public double EffectivePaperWidthMm => Landscape ? Math.Max(PaperWidthMm, PaperHeightMm) : Math.Min(PaperWidthMm, PaperHeightMm);

    public double EffectivePaperHeightMm => Landscape ? Math.Min(PaperWidthMm, PaperHeightMm) : Math.Max(PaperWidthMm, PaperHeightMm);
}

/// <summary>
/// One item placed in a band. Positions are millimeters from the left edge of the printable area and from the top of the band, so what the
/// designer shows is what is printed. Text may mix literal words with <c>[Field]</c> tokens (see <see cref="PrintFieldCatalog"/>).
/// </summary>
public sealed record PrintElement
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public PrintElementType Type { get; init; }
    public double XMm { get; init; }
    public double YMm { get; init; }
    public double WidthMm { get; init; } = 40;
    public double HeightMm { get; init; } = 7;
    public string Text { get; init; } = string.Empty;
    /// <summary>Empty uses the page font.</summary>
    public string FontFamily { get; init; } = string.Empty;
    public double FontSize { get; init; } = 10;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    /// <summary>Text, line or border color as #RRGGBB.</summary>
    public string Color { get; init; } = "#000000";
    /// <summary>Background color as #RRGGBB; empty for transparent.</summary>
    public string Fill { get; init; } = string.Empty;
    public PrintAlign Align { get; init; } = PrintAlign.Right;
    public bool Border { get; init; }
    /// <summary>PNG/JPEG bytes of an <see cref="PrintElementType.Image"/>, as Base64.</summary>
    public string? ImageBase64 { get; init; }
}

/// <summary>A horizontal region (page header, document header, document footer, page footer) with its elements.</summary>
public sealed record PrintBand
{
    public double HeightMm { get; init; } = 20;
    public IReadOnlyList<PrintElement> Elements { get; init; } = [];
}

/// <summary>A column of the lines table; <see cref="Field"/> is a key such as <c>Line.ItemName</c>.</summary>
public sealed record PrintColumn(string Field, string Heading, double WidthMm, PrintAlign Align = PrintAlign.Right);

/// <summary>The lines table of the document.</summary>
public sealed record PrintTable
{
    public double HeaderHeightMm { get; init; } = 8;
    public double RowHeightMm { get; init; } = 7;
    public double FontSize { get; init; } = 9;
    /// <summary>Heading background (#RRGGBB).</summary>
    public string HeaderFill { get; init; } = "#E8EEF0";
    public bool Borders { get; init; } = true;
    public IReadOnlyList<PrintColumn> Columns { get; init; } = [];
}

/// <summary>
/// A complete print template: paper, four bands and the lines table. Stored as JSON (one file per template, importable and exportable),
/// rendered by FastReport. It holds no data, only layout.
/// </summary>
public sealed record PrintTemplate
{
    public const int CurrentVersion = 1;
    public const int MaximumElementsPerBand = 100;
    public const int MaximumImageBytes = 1_500_000;

    public int Version { get; init; } = CurrentVersion;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public PrintDocumentKind Kind { get; init; }
    public PrintPageSettings Page { get; init; } = new();
    /// <summary>Repeats at the top of every page (logo, company).</summary>
    public PrintBand PageHeader { get; init; } = new();
    /// <summary>Printed once, before the lines (title, number, date, party).</summary>
    public PrintBand DocumentHeader { get; init; } = new();
    public PrintTable Table { get; init; } = new();
    /// <summary>Printed once, after the lines (totals, notes, signatures).</summary>
    public PrintBand DocumentFooter { get; init; } = new();
    /// <summary>Repeats at the bottom of every page (footer text, page number).</summary>
    public PrintBand PageFooter { get; init; } = new();

    /// <summary>Returns the template with every number and size forced into a printable range; throws when it is structurally unusable.</summary>
    public PrintTemplate Validated()
    {
        if (Version != CurrentVersion) throw new InvalidDataException("This print template version is not supported.");
        var page = Page with
        {
            PaperWidthMm = Math.Clamp(Finite(Page.PaperWidthMm, 210), 50, 1000),
            PaperHeightMm = Math.Clamp(Finite(Page.PaperHeightMm, 297), 50, 1500),
            MarginLeftMm = Math.Clamp(Finite(Page.MarginLeftMm, 10), 0, 100),
            MarginRightMm = Math.Clamp(Finite(Page.MarginRightMm, 10), 0, 100),
            MarginTopMm = Math.Clamp(Finite(Page.MarginTopMm, 10), 0, 100),
            MarginBottomMm = Math.Clamp(Finite(Page.MarginBottomMm, 10), 0, 100),
            FontFamily = string.IsNullOrWhiteSpace(Page.FontFamily) || Page.FontFamily.Length > 64 ? "Tahoma" : Page.FontFamily,
        };
        if (page.PrintableWidthMm < 20) throw new InvalidDataException("The margins leave no printable width.");
        var table = Table with
        {
            HeaderHeightMm = Math.Clamp(Finite(Table.HeaderHeightMm, 8), 3, 60),
            RowHeightMm = Math.Clamp(Finite(Table.RowHeightMm, 7), 3, 60),
            FontSize = Math.Clamp(Finite(Table.FontSize, 9), 5, 40),
            Columns = [.. Table.Columns.Take(24).Select(column => column with { WidthMm = Math.Clamp(Finite(column.WidthMm, 20), 5, 400) })],
        };
        return this with
        {
            Id = string.IsNullOrWhiteSpace(Id) || Id.Length > 64 || Id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-' && ch != '_') ? Guid.NewGuid().ToString("N") : Id,
            Name = string.IsNullOrWhiteSpace(Name) ? "نمط" : Name.Trim()[..Math.Min(80, Name.Trim().Length)],
            Page = page,
            Table = table,
            PageHeader = Clean(PageHeader),
            DocumentHeader = Clean(DocumentHeader),
            DocumentFooter = Clean(DocumentFooter),
            PageFooter = Clean(PageFooter),
        };
    }

    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    private static PrintBand Clean(PrintBand band) => band with
    {
        HeightMm = Math.Clamp(Finite(band.HeightMm, 20), 0, 400),
        Elements = [.. band.Elements.Take(MaximumElementsPerBand).Select(Clean)],
    };

    private static PrintElement Clean(PrintElement element)
    {
        var image = element.ImageBase64;
        if (image is not null && (image.Length > MaximumImageBytes * 4 / 3 + 8 || !IsBase64(image))) image = null;
        return element with
        {
            XMm = Math.Clamp(Finite(element.XMm, 0), -50, 1000),
            YMm = Math.Clamp(Finite(element.YMm, 0), -50, 1000),
            WidthMm = Math.Clamp(Finite(element.WidthMm, 40), 0, 1000),
            HeightMm = Math.Clamp(Finite(element.HeightMm, 7), 0, 1000),
            FontSize = Math.Clamp(Finite(element.FontSize, 10), 4, 120),
            Text = element.Text.Length > 2000 ? element.Text[..2000] : element.Text,
            ImageBase64 = image,
        };
    }

    private static bool IsBase64(string value)
    {
        Span<byte> buffer = new byte[value.Length * 3 / 4 + 4];
        return Convert.TryFromBase64String(value, buffer, out _);
    }
}

/// <summary>JSON persistence of templates (the same format is used for import and export).</summary>
public static class PrintTemplateJson
{
    public const long MaximumFileBytes = 8L * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(PrintTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return JsonSerializer.Serialize(template.Validated(), Options);
    }

    /// <summary>Reads and validates a template; throws <see cref="InvalidDataException"/> for anything that is not a usable template.</summary>
    public static PrintTemplate Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaximumFileBytes) throw new InvalidDataException("The print template file is too large.");
        try
        {
            return (JsonSerializer.Deserialize<PrintTemplate>(json, Options) ?? throw new InvalidDataException("The print template is empty.")).Validated();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The print template file is not valid.", exception);
        }
    }
}
