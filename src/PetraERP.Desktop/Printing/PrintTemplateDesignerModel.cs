using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.Reporting.OpenSource.Printing;

namespace PetraERP.Desktop.Printing;

/// <summary>The editable twin of <see cref="PrintElement"/>: every property notifies, so the canvas and the property panel stay in step.</summary>
public sealed partial class EditableElement : ObservableObject
{
    [ObservableProperty] private PrintElementType type;
    [ObservableProperty] private double x;
    [ObservableProperty] private double y;
    [ObservableProperty] private double width = 40;
    [ObservableProperty] private double height = 7;
    [ObservableProperty] private string text = string.Empty;
    [ObservableProperty] private string fontFamily = string.Empty;
    [ObservableProperty] private double fontSize = 10;
    [ObservableProperty] private bool bold;
    [ObservableProperty] private bool italic;
    [ObservableProperty] private string color = "#000000";
    [ObservableProperty] private string fill = string.Empty;
    [ObservableProperty] private PrintAlign align = PrintAlign.Right;
    [ObservableProperty] private bool border;
    [ObservableProperty] private string? imageBase64;

    public string Id { get; private init; } = Guid.NewGuid().ToString("N");

    public static EditableElement From(PrintElement element) => new()
    {
        Id = element.Id, Type = element.Type, X = element.XMm, Y = element.YMm, Width = element.WidthMm, Height = element.HeightMm, Text = element.Text,
        FontFamily = element.FontFamily, FontSize = element.FontSize, Bold = element.Bold, Italic = element.Italic, Color = element.Color, Fill = element.Fill,
        Align = element.Align, Border = element.Border, ImageBase64 = element.ImageBase64,
    };

    public PrintElement ToElement() => new()
    {
        Id = Id, Type = Type, XMm = X, YMm = Y, WidthMm = Width, HeightMm = Height, Text = Text, FontFamily = FontFamily, FontSize = FontSize, Bold = Bold,
        Italic = Italic, Color = Color, Fill = Fill, Align = Align, Border = Border, ImageBase64 = ImageBase64,
    };
}

/// <summary>An editable band: its height and the elements placed in it.</summary>
public sealed partial class EditableBand(PrintBandKind kind) : ObservableObject
{
    [ObservableProperty] private double heightMm = 20;

    public PrintBandKind Kind { get; } = kind;
    public ObservableCollection<EditableElement> Elements { get; } = [];
}

/// <summary>An editable lines-table column.</summary>
public sealed partial class EditableColumn : ObservableObject
{
    [ObservableProperty] private string field = string.Empty;
    [ObservableProperty] private string heading = string.Empty;
    [ObservableProperty] private double widthMm = 20;
    [ObservableProperty] private PrintAlign align = PrintAlign.Right;
}

/// <summary>The four bands of a template.</summary>
public enum PrintBandKind
{
    PageHeader = 0,
    DocumentHeader = 1,
    DocumentFooter = 2,
    PageFooter = 3,
}

/// <summary>
/// The designer's working copy of a template. All edits happen here; <see cref="ToTemplate"/> produces the immutable template that is validated
/// and saved. It holds no UI types, so the editing rules are unit-testable.
/// </summary>
public sealed partial class PrintTemplateDesignerModel : ObservableObject
{
    public const double Grid = 0.5;
    private bool watching;
    private static readonly Regex Token = new(@"\[([A-Za-z]+(?:\.[A-Za-z]+)?#?)\]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [ObservableProperty] private string id = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private EditableElement? selectedElement;
    [ObservableProperty] private EditableBand? selectedBand;
    [ObservableProperty] private EditableColumn? selectedColumn;
    [ObservableProperty] private double paperWidthMm = 210;
    [ObservableProperty] private double paperHeightMm = 297;
    [ObservableProperty] private bool landscape;
    [ObservableProperty] private double marginLeftMm = 10;
    [ObservableProperty] private double marginRightMm = 10;
    [ObservableProperty] private double marginTopMm = 10;
    [ObservableProperty] private double marginBottomMm = 10;
    [ObservableProperty] private bool rightToLeft = true;
    [ObservableProperty] private string pageFontFamily = "Tahoma";
    [ObservableProperty] private double tableHeaderHeightMm = 8;
    [ObservableProperty] private double tableRowHeightMm = 7;
    [ObservableProperty] private double tableFontSize = 9;
    [ObservableProperty] private string tableHeaderFill = "#E8EEF0";
    [ObservableProperty] private bool tableBorders = true;

    public PrintTemplateDesignerModel(PrintTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        Kind = template.Kind;
        foreach (var kind in Enum.GetValues<PrintBandKind>()) Bands.Add(new EditableBand(kind));
        Load(template);
    }

    /// <summary>Raised after any edit (element, band, column, page or table), so the canvas can redraw.</summary>
    public event EventHandler? Changed;

    public PrintDocumentKind Kind { get; }
    public ObservableCollection<EditableBand> Bands { get; } = [];
    public ObservableCollection<EditableColumn> Columns { get; } = [];

    public double PrintableWidthMm => (Landscape ? Math.Max(PaperWidthMm, PaperHeightMm) : Math.Min(PaperWidthMm, PaperHeightMm)) - MarginLeftMm - MarginRightMm;

    public EditableBand Band(PrintBandKind kind) => Bands[(int)kind];

    public IReadOnlyList<PrintField> DocumentFields => PrintFieldCatalog.DocumentFields(Kind);
    public IReadOnlyList<PrintField> LineFields => PrintFieldCatalog.LineFields(Kind);

    private void Load(PrintTemplate template)
    {
        Id = template.Id; Name = template.Name;
        var page = template.Page;
        PaperWidthMm = page.PaperWidthMm; PaperHeightMm = page.PaperHeightMm; Landscape = page.Landscape;
        MarginLeftMm = page.MarginLeftMm; MarginRightMm = page.MarginRightMm; MarginTopMm = page.MarginTopMm; MarginBottomMm = page.MarginBottomMm;
        RightToLeft = page.RightToLeft; PageFontFamily = page.FontFamily;
        TableHeaderHeightMm = template.Table.HeaderHeightMm; TableRowHeightMm = template.Table.RowHeightMm; TableFontSize = template.Table.FontSize;
        TableHeaderFill = template.Table.HeaderFill; TableBorders = template.Table.Borders;
        Fill(Band(PrintBandKind.PageHeader), template.PageHeader);
        Fill(Band(PrintBandKind.DocumentHeader), template.DocumentHeader);
        Fill(Band(PrintBandKind.DocumentFooter), template.DocumentFooter);
        Fill(Band(PrintBandKind.PageFooter), template.PageFooter);
        Columns.Clear();
        foreach (var column in template.Table.Columns) Columns.Add(new EditableColumn { Field = column.Field, Heading = column.Heading, WidthMm = column.WidthMm, Align = column.Align });
        SelectedBand = Band(PrintBandKind.DocumentHeader);
        Watch();
        IsDirty = false;
    }

    private static void Fill(EditableBand band, PrintBand source)
    {
        band.HeightMm = source.HeightMm;
        band.Elements.Clear();
        foreach (var element in source.Elements) band.Elements.Add(EditableElement.From(element));
    }

    /// <summary>Marks the working copy as changed whenever anything in it changes.</summary>
    private void Watch()
    {
        if (watching) return;
        watching = true;
        PropertyChanged += (_, args) => { if (args.PropertyName is not (nameof(IsDirty) or nameof(SelectedElement) or nameof(SelectedBand) or nameof(SelectedColumn))) Touch(); };
        foreach (var band in Bands)
        {
            band.PropertyChanged += (_, _) => Touch();
            band.Elements.CollectionChanged += OnElementsChanged;
            foreach (var element in band.Elements) element.PropertyChanged += OnElementChanged;
        }

        Columns.CollectionChanged += OnColumnsChanged;
        foreach (var column in Columns) column.PropertyChanged += OnElementChanged;
    }

    private void Touch()
    {
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnElementsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        Touch();
        foreach (var element in args.NewItems?.OfType<EditableElement>() ?? []) element.PropertyChanged += OnElementChanged;
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        Touch();
        foreach (var column in args.NewItems?.OfType<EditableColumn>() ?? []) column.PropertyChanged += OnElementChanged;
    }

    private void OnElementChanged(object? sender, PropertyChangedEventArgs args) => Touch();

    /// <summary>The template as currently edited (not yet validated against the field catalogue).</summary>
    public PrintTemplate ToTemplate() => new()
    {
        Id = Id, Name = Name, Kind = Kind,
        Page = new PrintPageSettings
        {
            PaperWidthMm = PaperWidthMm, PaperHeightMm = PaperHeightMm, Landscape = Landscape, MarginLeftMm = MarginLeftMm, MarginRightMm = MarginRightMm,
            MarginTopMm = MarginTopMm, MarginBottomMm = MarginBottomMm, RightToLeft = RightToLeft, FontFamily = PageFontFamily,
        },
        PageHeader = ToBand(PrintBandKind.PageHeader), DocumentHeader = ToBand(PrintBandKind.DocumentHeader),
        DocumentFooter = ToBand(PrintBandKind.DocumentFooter), PageFooter = ToBand(PrintBandKind.PageFooter),
        Table = new PrintTable
        {
            HeaderHeightMm = TableHeaderHeightMm, RowHeightMm = TableRowHeightMm, FontSize = TableFontSize, HeaderFill = TableHeaderFill, Borders = TableBorders,
            Columns = [.. Columns.Select(column => new PrintColumn(column.Field, column.Heading, column.WidthMm, column.Align))],
        },
    };

    private PrintBand ToBand(PrintBandKind kind) => new() { HeightMm = Band(kind).HeightMm, Elements = [.. Band(kind).Elements.Select(element => element.ToElement())] };

    /// <summary>Replaces the working copy with a template (for example after an import) and clears the changed flag.</summary>
    public void Replace(PrintTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.Kind != Kind) throw new InvalidOperationException("The template is for another document kind.");
        Load(template);
        IsDirty = true;
    }

    // ───── editing ─────

    /// <summary>Adds an element to a band, selects it and returns it. New text starts with a useful caption so it is visible on the canvas.</summary>
    public EditableElement AddElement(PrintElementType type, PrintBandKind bandKind, string? text = null)
    {
        var band = Band(bandKind);
        var element = new EditableElement
        {
            Type = type, X = 10, Y = Math.Min(2, Math.Max(0, band.HeightMm - 8)),
            Width = type == PrintElementType.Line ? 60 : type == PrintElementType.Image ? 30 : 50,
            Height = type == PrintElementType.Line ? 0 : type == PrintElementType.Image ? 20 : 7,
            Text = type == PrintElementType.Text ? text ?? "نص" : string.Empty,
            Align = RightToLeft ? PrintAlign.Right : PrintAlign.Left,
            Color = type is PrintElementType.Line or PrintElementType.Rectangle ? "#7A8A93" : "#000000",
        };
        band.Elements.Add(element);
        SelectedBand = band;
        SelectedElement = element;
        return element;
    }

    public void RemoveSelected()
    {
        if (SelectedElement is not { } element) return;
        foreach (var band in Bands) band.Elements.Remove(element);
        SelectedElement = null;
    }

    public EditableElement? DuplicateSelected()
    {
        if (SelectedElement is not { } source || BandOf(source) is not { } band) return null;
        var copy = EditableElement.From(source.ToElement() with { Id = Guid.NewGuid().ToString("N"), XMm = source.X + 5, YMm = source.Y + 5 });
        band.Elements.Add(copy);
        SelectedElement = copy;
        return copy;
    }

    public EditableBand? BandOf(EditableElement element) => Bands.FirstOrDefault(band => band.Elements.Contains(element));

    /// <summary>Moves the selected element by a distance in millimeters, snapped to the grid and kept inside its band and the printable width.</summary>
    public void MoveSelected(double dx, double dy)
    {
        if (SelectedElement is { } element) MoveTo(element, element.X + dx, element.Y + dy);
    }

    /// <summary>Places an element at an absolute position (millimeters), snapped to the grid and kept inside its band and the printable width.</summary>
    public void MoveTo(EditableElement element, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (BandOf(element) is not { } band) return;
        element.X = Math.Clamp(Snap(x), 0, Math.Max(0, PrintableWidthMm - element.Width));
        element.Y = Math.Clamp(Snap(y), 0, Math.Max(0, band.HeightMm - element.Height));
    }

    /// <summary>Resizes the selected element (minimum 3 mm wide; a line keeps zero height), kept inside its band and the printable width.</summary>
    public void ResizeSelected(double dx, double dy)
    {
        if (SelectedElement is { } element) ResizeTo(element, element.Width + dx, element.Height + dy);
    }

    /// <summary>Gives an element an absolute size (millimeters), snapped and kept inside its band and the printable width.</summary>
    public void ResizeTo(EditableElement element, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (BandOf(element) is not { } band) return;
        element.Width = Math.Clamp(Snap(width), 3, Math.Max(3, PrintableWidthMm - element.X));
        if (element.Type != PrintElementType.Line) element.Height = Math.Clamp(Snap(height), 2, Math.Max(2, band.HeightMm - element.Y));
    }

    private static double Snap(double value) => Math.Round(value / Grid, MidpointRounding.AwayFromZero) * Grid;

    /// <summary>Inserts <c>[Field]</c> into the selected text element, or adds a new text element showing it.</summary>
    public EditableElement InsertField(string key, PrintBandKind bandKind)
    {
        if (SelectedElement is { Type: PrintElementType.Text } element)
        {
            element.Text += "[" + key + "]";
            return element;
        }

        return AddElement(PrintElementType.Text, bandKind, "[" + key + "]");
    }

    public EditableColumn AddColumn(PrintField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var column = new EditableColumn { Field = field.Key, Heading = field.Arabic, WidthMm = 20, Align = PrintAlign.Center };
        Columns.Add(column);
        SelectedColumn = column;
        return column;
    }

    public void RemoveSelectedColumn()
    {
        if (SelectedColumn is { } column) Columns.Remove(column);
        SelectedColumn = null;
    }

    public void MoveSelectedColumn(int direction)
    {
        if (SelectedColumn is not { } column) return;
        var index = Columns.IndexOf(column);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= Columns.Count) return;
        Columns.Move(index, target);
    }

    /// <summary>Scales all column widths so that they exactly fill the printable width.</summary>
    public void FitColumnsToWidth()
    {
        var total = Columns.Sum(column => column.WidthMm);
        if (total <= 0) return;
        var scale = PrintableWidthMm / total;
        foreach (var column in Columns) column.WidthMm = Math.Round(column.WidthMm * scale, 1);
    }

    /// <summary>Problems that would make the printout wrong or empty; an empty list means the template can be saved.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) problems.Add("اسم النمط مطلوب.");
        if (Columns.Count == 0) problems.Add("جدول الأسطر بلا أعمدة.");
        var known = PrintFieldCatalog.AllKeys(Kind);
        foreach (var column in Columns.Where(column => !known.Contains(column.Field))) problems.Add("عمود غير معروف: " + column.Field);
        foreach (var band in Bands)
            foreach (var element in band.Elements.Where(element => element.Type == PrintElementType.Text))
                foreach (Match match in Token.Matches(element.Text))
                    if (!known.Contains(match.Groups[1].Value)) problems.Add("حقل غير معروف: [" + match.Groups[1].Value + "]");
        if (PrintableWidthMm < 50) problems.Add("الهوامش تترك عرض طباعة صغيرًا جدًا.");
        return problems.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Formats a millimeter value for display (invariant, one decimal).</summary>
    public static string Mm(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
