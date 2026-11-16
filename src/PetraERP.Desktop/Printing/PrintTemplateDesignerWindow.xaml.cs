using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PetraERP.Desktop.Shell;
using PetraERP.Reporting.OpenSource.Printing;

namespace PetraERP.Desktop.Printing;

/// <summary>
/// The template designer. The surface shows the page with its bands; elements are dragged and resized in millimeters (snapped to 0.5 mm) and
/// the preview tab renders the very same template through FastReport with the real document values. All rules live in
/// <see cref="PrintTemplateDesignerModel"/>; this window only draws and routes input.
/// </summary>
public partial class PrintTemplateDesignerWindow : Window
{
    private const double BasePixelsPerMillimeter = 3.2;
    private static readonly (string Label, double Width, double Height)[] Papers =
    [
        ("A4  210 × 297", 210, 297), ("A5  148 × 210", 148, 210), ("نصف A4  210 × 148", 210, 148), ("Letter  215.9 × 279.4", 215.9, 279.4), ("A3  297 × 420", 297, 420),
    ];

    private readonly PrintTemplateDesignerModel model;
    private readonly PrintTemplateStore store;
    private readonly Guid companyId;
    private readonly PrintData previewData;
    private bool dragging;
    private bool closingConfirmed;
    private bool updatingPaper;

    public PrintTemplateDesignerWindow(PrintTemplate template, PrintTemplateStore store, Guid companyId, PrintData previewData)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(template);
        this.store = store; this.companyId = companyId; this.previewData = previewData;
        model = new PrintTemplateDesignerModel(template);
        DataContext = model;

        var fonts = Fonts.SystemFontFamilies.Select(family => family.Source).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        FontBox.ItemsSource = fonts; PageFontBox.ItemsSource = fonts;
        AlignBox.ItemsSource = new[] { new Option<PrintAlign>(PrintAlign.Left, "يسار"), new Option<PrintAlign>(PrintAlign.Center, "وسط"), new Option<PrintAlign>(PrintAlign.Right, "يمين") };
        PaperBox.ItemsSource = Papers.Select(paper => new Option<(double, double)>((paper.Width, paper.Height), paper.Label)).ToArray();
        ColumnFieldBox.ItemsSource = model.LineFields; ColumnFieldBox.SelectedIndex = 0;
        var fields = new ListCollectionView(model.DocumentFields.ToList());
        fields.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PrintField.Group)));
        FieldList.ItemsSource = fields;
        SyncPaperSelection();

        model.Changed += (_, _) => { if (!dragging) Rebuild(); };
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(PrintTemplateDesignerModel.SelectedElement) or nameof(PrintTemplateDesignerModel.SelectedBand) && !dragging) Rebuild();
            if (args.PropertyName == nameof(PrintTemplateDesignerModel.IsDirty)) UpdateTitle();
        };
        UpdateTitle();
        Rebuild();
    }

    /// <summary>The identifier of the template saved last, or null when nothing was saved.</summary>
    public string? SavedTemplateId { get; private set; }

    private sealed record Option<T>(T Value, string Label);

    private double Scale => BasePixelsPerMillimeter * (ZoomSlider?.Value ?? 1);

    private void UpdateTitle() => Title = "مصمم قوالب الطباعة — " + model.Name + (model.IsDirty ? " *" : string.Empty);

    private void SyncPaperSelection()
    {
        updatingPaper = true;
        PaperBox.SelectedIndex = Array.FindIndex(Papers, paper => Math.Abs(paper.Width - model.PaperWidthMm) < 0.1 && Math.Abs(paper.Height - model.PaperHeightMm) < 0.1);
        updatingPaper = false;
    }

    private void OnPaperChanged(object sender, SelectionChangedEventArgs args)
    {
        if (updatingPaper || PaperBox.SelectedItem is not Option<(double Width, double Height)> choice) return;
        model.PaperWidthMm = choice.Value.Width;
        model.PaperHeightMm = choice.Value.Height;
    }

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (model is not null && IsLoaded) Rebuild();
    }

    // ───── drawing ─────

    private void Rebuild()
    {
        var scale = Scale;
        var paperWidth = (model.Landscape ? Math.Max(model.PaperWidthMm, model.PaperHeightMm) : Math.Min(model.PaperWidthMm, model.PaperHeightMm));
        var paperHeight = (model.Landscape ? Math.Min(model.PaperWidthMm, model.PaperHeightMm) : Math.Max(model.PaperWidthMm, model.PaperHeightMm));
        PageBorder.Width = paperWidth * scale;
        PageBorder.MinHeight = paperHeight * scale;
        BandsPanel.Margin = new Thickness(model.MarginLeftMm * scale, model.MarginTopMm * scale, model.MarginRightMm * scale, model.MarginBottomMm * scale);
        BandsPanel.Children.Clear();
        BandsPanel.Children.Add(BandSection(PrintBandKind.PageHeader, "ترويسة الصفحة (تتكرر في كل صفحة)", scale));
        BandsPanel.Children.Add(BandSection(PrintBandKind.DocumentHeader, "ترويسة المستند (مرة واحدة قبل الأسطر)", scale));
        BandsPanel.Children.Add(TableSection(scale));
        BandsPanel.Children.Add(BandSection(PrintBandKind.DocumentFooter, "تذييل المستند (بعد الأسطر)", scale));
        BandsPanel.Children.Add(BandSection(PrintBandKind.PageFooter, "تذييل الصفحة (يتكرر في كل صفحة)", scale));
        StatusText.Text = model.Validate() is { Count: > 0 } problems ? "⚠ " + problems[0] : string.Empty;
    }

    private StackPanel BandSection(PrintBandKind kind, string caption, double scale)
    {
        var band = model.Band(kind);
        var selected = ReferenceEquals(model.SelectedBand, band);
        var header = new Border
        {
            Background = selected ? (Brush)FindResource("Accent") : (Brush)FindResource("Alternate"), Height = 16, Cursor = Cursors.Hand, Tag = band,
            Child = new TextBlock { Text = caption + "  —  " + PrintTemplateDesignerModel.Mm(band.HeightMm) + " مم", FontSize = 10, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = selected ? (Brush)FindResource("Text.OnAccent") : (Brush)FindResource("Text"), FlowDirection = FlowDirection.RightToLeft },
        };
        header.MouseLeftButtonDown += (_, _) => { model.SelectedElement = null; model.SelectedBand = band; };

        var canvas = new Canvas { Width = model.PrintableWidthMm * scale, Height = band.HeightMm * scale, Background = new SolidColorBrush(Color.FromArgb(selected ? (byte)40 : (byte)14, 90, 140, 160)), ClipToBounds = true };
        canvas.MouseLeftButtonDown += (_, args) => { if (ReferenceEquals(args.OriginalSource, canvas)) { model.SelectedElement = null; model.SelectedBand = band; } };
        foreach (var element in band.Elements) canvas.Children.Add(ElementVisual(element, band, scale));

        var grip = new Thumb { Height = 5, Cursor = Cursors.SizeNS, Background = Brushes.Transparent, Opacity = 0.0, ToolTip = "اسحب لتغيير ارتفاع القسم" };
        var resizeStart = 0d;
        grip.DragStarted += (_, _) => { dragging = true; resizeStart = band.HeightMm; };
        grip.DragDelta += (_, args) =>
        {
            var height = Math.Clamp(Math.Round((resizeStart * scale + args.VerticalChange) / scale / PrintTemplateDesignerModel.Grid) * PrintTemplateDesignerModel.Grid, 3, 400);
            canvas.Height = height * scale;
        };
        grip.DragCompleted += (_, args) =>
        {
            dragging = false;
            band.HeightMm = Math.Clamp(Math.Round((resizeStart * scale + args.VerticalChange) / scale / PrintTemplateDesignerModel.Grid) * PrintTemplateDesignerModel.Grid, 3, 400);
        };
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        stack.Children.Add(header);
        stack.Children.Add(canvas);
        stack.Children.Add(new Border { Height = 5, Background = new SolidColorBrush(Color.FromArgb(70, 90, 140, 160)), Child = grip, Cursor = Cursors.SizeNS });
        return stack;
    }

    private StackPanel TableSection(double scale)
    {
        var header = new Border { Background = (Brush)FindResource("Alternate"), Height = 16, Child = new TextBlock { Text = "جدول الأسطر (الأعمدة من تبويب «أعمدة الأسطر»)", FontSize = 10, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.RightToLeft, Foreground = (Brush)FindResource("Text") } };
        header.MouseLeftButtonDown += (_, _) => SideTabs.SelectedIndex = 2;
        var grid = new Grid { Width = model.PrintableWidthMm * scale, Margin = new Thickness(0, 0, 0, 4) };
        var total = Math.Max(1, model.Columns.Sum(column => column.WidthMm));
        var factor = model.PrintableWidthMm / total;
        var rows = new[] { true, false, false };
        for (var row = 0; row < rows.Length; row++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength((rows[row] ? model.TableHeaderHeightMm : model.TableRowHeightMm) * scale) });
        var position = 0d;
        for (var index = 0; index < model.Columns.Count; index++)
        {
            var column = model.Columns[index];
            var width = column.WidthMm * factor * scale;
            var x = model.RightToLeft ? model.PrintableWidthMm * scale - position - width : position;
            position += width;
            for (var row = 0; row < rows.Length; row++)
            {
                var sample = rows[row] ? column.Heading : (model.LineFields.FirstOrDefault(field => field.Key == column.Field)?.Sample ?? string.Empty);
                var cell = new Border
                {
                    BorderBrush = Brushes.Gray, BorderThickness = new Thickness(model.TableBorders ? 0.5 : 0), Width = width, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(x, 0, 0, 0),
                    Background = rows[row] ? new SolidColorBrush(Parse(model.TableHeaderFill, Colors.Transparent)) : Brushes.Transparent,
                    Child = new TextBlock { Text = sample, Foreground = Brushes.Black, FontSize = model.TableFontSize * 96d / 72d * scale / 3.7795, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 2, 0), FontWeight = rows[row] ? FontWeights.Bold : FontWeights.Normal,
                        TextAlignment = column.Align switch { PrintAlign.Left => TextAlignment.Left, PrintAlign.Right => TextAlignment.Right, _ => TextAlignment.Center }, VerticalAlignment = VerticalAlignment.Center, FlowDirection = model.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight },
                };
                Grid.SetRow(cell, row);
                grid.Children.Add(cell);
            }
        }

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(grid);
        return stack;
    }

    private Grid ElementVisual(EditableElement element, EditableBand band, double scale)
    {
        var selected = ReferenceEquals(model.SelectedElement, element);
        FrameworkElement content = element.Type switch
        {
            PrintElementType.Image => ImageContent(element),
            PrintElementType.Line => new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Parse(element.Color, Colors.Gray)) },
            PrintElementType.Rectangle => new Border { BorderBrush = new SolidColorBrush(Parse(element.Color, Colors.Gray)), BorderThickness = new Thickness(1), Background = FillBrush(element.Fill) },
            _ => TextContent(element, scale),
        };
        var box = new Grid { Width = Math.Max(4, element.Width * scale), Height = element.Type == PrintElementType.Line ? 6 : Math.Max(4, element.Height * scale), Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Tag = element };
        box.Children.Add(content);
        box.Children.Add(new Border { BorderBrush = selected ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(70, 120, 120, 120)), BorderThickness = new Thickness(selected ? 2 : 0.5), IsHitTestVisible = false });
        Canvas.SetLeft(box, element.X * scale);
        Canvas.SetTop(box, element.Y * scale);

        Point start = default; double originX = 0, originY = 0; Canvas? parent = null;
        box.MouseLeftButtonDown += (_, args) =>
        {
            model.SelectedBand = band;
            model.SelectedElement = element;
            if (VisualTreeHelper.GetParent(box) is not Canvas canvas) return;
            parent = canvas; start = args.GetPosition(canvas); originX = element.X; originY = element.Y;
            dragging = true;
            box.CaptureMouse();
            args.Handled = true;
        };
        box.MouseMove += (_, args) =>
        {
            if (!box.IsMouseCaptured || parent is null) return;
            var now = args.GetPosition(parent);
            model.MoveTo(element, originX + (now.X - start.X) / scale, originY + (now.Y - start.Y) / scale);
            Canvas.SetLeft(box, element.X * scale);
            Canvas.SetTop(box, element.Y * scale);
        };
        box.MouseLeftButtonUp += (_, _) =>
        {
            if (!box.IsMouseCaptured) return;
            box.ReleaseMouseCapture();
            dragging = false;
            Rebuild();
        };

        if (selected)
        {
            var grip = new Thumb { Width = 9, Height = 9, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Background = (Brush)FindResource("Accent") };
            double startWidth = 0, startHeight = 0;
            grip.DragStarted += (_, _) => { dragging = true; startWidth = element.Width; startHeight = element.Height; };
            grip.DragDelta += (_, args) =>
            {
                model.ResizeTo(element, element.Width + args.HorizontalChange / scale, element.Height + args.VerticalChange / scale);
                box.Width = Math.Max(4, element.Width * scale);
                if (element.Type != PrintElementType.Line) box.Height = Math.Max(4, element.Height * scale);
            };
            grip.DragCompleted += (_, _) => { dragging = false; _ = startWidth + startHeight; Rebuild(); };
            box.Children.Add(grip);
        }

        return box;
    }

    private TextBlock TextContent(EditableElement element, double scale)
    {
        var shown = PrintTemplateRenderer.Substitute(element.Text, previewData, out _);
        var family = string.IsNullOrWhiteSpace(element.FontFamily) ? model.PageFontFamily : element.FontFamily;
        return new TextBlock
        {
            Text = shown, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily(family), FontSize = Math.Max(3, element.FontSize * 96d / 72d * scale / 3.7795),
            FontWeight = element.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = element.Italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = new SolidColorBrush(Parse(element.Color, Colors.Black)), Background = FillBrush(element.Fill),
            TextAlignment = element.Align switch { PrintAlign.Left => TextAlignment.Left, PrintAlign.Right => TextAlignment.Right, _ => TextAlignment.Center },
            FlowDirection = model.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
    }

    private static FrameworkElement ImageContent(EditableElement element)
    {
        if (string.IsNullOrEmpty(element.ImageBase64)) return new Border { Background = new SolidColorBrush(Color.FromArgb(40, 120, 120, 120)), Child = new TextBlock { Text = "صورة", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray } };
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(element.ImageBase64));
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            return new Image { Source = frame, Stretch = Stretch.Uniform };
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or IOException)
        {
            return new Border { Background = Brushes.MistyRose, Child = new TextBlock { Text = "صورة تالفة", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        }
    }

    private static SolidColorBrush? FillBrush(string fill) => string.IsNullOrWhiteSpace(fill) ? null : new SolidColorBrush(Parse(fill, Colors.Transparent));

    private static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try { return (Color)ColorConverter.ConvertFromString(value.Trim()); }
        catch (FormatException) { return fallback; }
    }

    // ───── commands ─────

    private PrintBandKind TargetBand() => model.SelectedBand is { } band ? band.Kind : PrintBandKind.DocumentHeader;

    private void OnAddText(object sender, RoutedEventArgs args) => model.AddElement(PrintElementType.Text, TargetBand());

    private void OnAddLine(object sender, RoutedEventArgs args) => model.AddElement(PrintElementType.Line, TargetBand());

    private void OnAddRectangle(object sender, RoutedEventArgs args) => model.AddElement(PrintElementType.Rectangle, TargetBand());

    private void OnAddImage(object sender, RoutedEventArgs args)
    {
        model.AddElement(PrintElementType.Image, TargetBand());
        OnPickImage(sender, args);
    }

    private void OnDuplicate(object sender, RoutedEventArgs args) => model.DuplicateSelected();

    private void OnDeleteElement(object sender, RoutedEventArgs args) => model.RemoveSelected();

    private void OnPickImage(object sender, RoutedEventArgs args)
    {
        if (model.SelectedElement is not { } element) return;
        if (element.Type != PrintElementType.Image) { PetraMessage.Show(this, "اختر عنصر صورة أولًا (زر «صورة / شعار»).", Title); return; }
        var dialog = new OpenFileDialog { Filter = "صور (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > PrintTemplate.MaximumImageBytes) { PetraMessage.Show(this, "حجم الصورة أكبر من 1.5 ميغابايت. صغّرها ثم أعد المحاولة.", Title); return; }
            var bytes = File.ReadAllBytes(dialog.FileName);
            using (var stream = new MemoryStream(bytes)) _ = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            element.ImageBase64 = Convert.ToBase64String(bytes);
            if (element.Height < 2 || element.Width < 3) { element.Width = 30; element.Height = 20; }
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            PetraMessage.Show(this, "تعذّر قراءة الصورة: " + exception.Message, Title);
        }
    }

    private void OnFieldDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (FieldList.SelectedItem is PrintField field) model.InsertField(field.Key, TargetBand());
    }

    private void OnAddColumn(object sender, RoutedEventArgs args)
    {
        if (ColumnFieldBox.SelectedItem is PrintField field) model.AddColumn(field);
    }

    private void OnRemoveColumn(object sender, RoutedEventArgs args) => model.RemoveSelectedColumn();

    private void OnColumnUp(object sender, RoutedEventArgs args) => model.MoveSelectedColumn(-1);

    private void OnColumnDown(object sender, RoutedEventArgs args) => model.MoveSelectedColumn(1);

    private void OnFitColumns(object sender, RoutedEventArgs args) => model.FitColumnsToWidth();

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.S) { OnSave(this, new RoutedEventArgs()); args.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBox or ComboBox or DataGridCell) return;
        var step = Keyboard.Modifiers == ModifierKeys.Shift ? 5d : PrintTemplateDesignerModel.Grid;
        switch (args.Key)
        {
            case Key.Delete: model.RemoveSelected(); args.Handled = true; break;
            case Key.Left: model.MoveSelected(-step, 0); args.Handled = true; break;
            case Key.Right: model.MoveSelected(step, 0); args.Handled = true; break;
            case Key.Up: model.MoveSelected(0, -step); args.Handled = true; break;
            case Key.Down: model.MoveSelected(0, step); args.Handled = true; break;
        }
    }

    private async void OnCenterTabChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!ReferenceEquals(args.OriginalSource, CenterTabs) || CenterTabs.SelectedIndex != 1) return;
        PreviewNote.Text = "المعاينة بقيم المستند نفسه عبر FastReport، كما ستُطبع.";
        try
        {
            var template = model.ToTemplate().Validated();
            var images = await Task.Run(() => PrintTemplateRenderer.RenderPages(template, previewData, 110)).ConfigureAwait(true);
            PreviewPages.ItemsSource = images.Select(png => DocumentPrintViewModel.ToPage(png, template)).ToList();
        }
        catch (Exception exception)
        {
            PreviewNote.Text = "تعذّرت المعاينة: " + exception.Message;
        }
    }

    // ───── save / load ─────

    private bool Save()
    {
        if (model.Validate() is { Count: > 0 } problems)
        {
            PetraMessage.Show(this, string.Join(Environment.NewLine, problems), "لا يمكن حفظ النمط", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (model.Id == DefaultPrintTemplates.IdFor(model.Kind))
        {
            // The built-in template is never overwritten: the first save makes a company copy.
            model.Id = Guid.NewGuid().ToString("N");
            if (model.Name == DefaultPrintTemplates.For(model.Kind).Name) model.Name += " (نسخة الشركة)";
        }

        try
        {
            var saved = store.Save(companyId, model.ToTemplate());
            SavedTemplateId = saved.Id;
            model.IsDirty = false;
            StatusText.Text = "تم حفظ النمط.";
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException)
        {
            PetraMessage.Show(this, exception.Message, "تعذّر الحفظ", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void OnSave(object sender, RoutedEventArgs args) => Save();

    private void OnSaveAs(object sender, RoutedEventArgs args)
    {
        var name = PromptName(model.Name + " (نسخة)");
        if (string.IsNullOrWhiteSpace(name)) return;
        model.Id = Guid.NewGuid().ToString("N");
        model.Name = name.Trim();
        Save();
    }

    private void OnSetDefault(object sender, RoutedEventArgs args)
    {
        if (model.IsDirty && !Save()) return;
        store.SetDefault(companyId, model.Kind, model.Id);
        StatusText.Text = "صار هذا النمط الافتراضي للشركة.";
    }

    private void OnDelete(object sender, RoutedEventArgs args)
    {
        if (model.Id == DefaultPrintTemplates.IdFor(model.Kind)) { PetraMessage.Show(this, "النمط المدمج لا يُحذف. احفظ نسخة منه لتعديلها.", Title); return; }
        if (PetraMessage.Show(this, "حذف هذا النمط نهائيًا؟", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        store.Delete(companyId, model.Kind, model.Id);
        SavedTemplateId = DefaultPrintTemplates.IdFor(model.Kind);
        model.IsDirty = false;
        closingConfirmed = true;
        Close();
    }

    private void OnImport(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog { Filter = "قالب طباعة (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            model.Replace(PrintTemplateStore.Import(dialog.FileName, model.Kind));
            SyncPaperSelection();
            Rebuild();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            PetraMessage.Show(this, exception.Message, "تعذّر الاستيراد", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnExport(object sender, RoutedEventArgs args)
    {
        var dialog = new SaveFileDialog { Filter = "قالب طباعة (*.json)|*.json", FileName = model.Name + ".json", AddExtension = true, DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try { PrintTemplateStore.Export(model.ToTemplate().Validated(), dialog.FileName); StatusText.Text = "تم التصدير."; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            PetraMessage.Show(this, exception.Message, "تعذّر التصدير", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs args)
    {
        if (closingConfirmed || !model.IsDirty) return;
        switch (PetraMessage.Show(this, "في النمط تغييرات غير محفوظة. هل تريد حفظها؟", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
        {
            case MessageBoxResult.Yes: if (!Save()) args.Cancel = true; break;
            case MessageBoxResult.Cancel: args.Cancel = true; break;
        }
    }

    /// <summary>A small modal prompt for a template name.</summary>
    private string? PromptName(string initial)
    {
        var box = new TextBox { Text = initial, MinWidth = 280, Margin = new Thickness(0, 0, 0, 10) };
        var ok = new Button { Content = "موافق", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "إلغاء", IsCancel = true, MinWidth = 90 };
        ok.SetResourceReference(StyleProperty, "Button.Primary");
        cancel.SetResourceReference(StyleProperty, "Button.Command");
        var label = new TextBlock { Text = "اسم النمط" };
        label.SetResourceReference(StyleProperty, "Text.Label");
        var panel = new StackPanel();
        panel.SetResourceReference(MarginProperty, "Pad.Dialog");
        panel.Children.Add(label);
        panel.Children.Add(box);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } });
        var prompt = new Window { Title = "حفظ باسم", Content = panel, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize };
        ok.Click += (_, _) => prompt.DialogResult = true;
        return PetraWindows.ShowDialog(prompt, this, DialogSize.Small) == true ? box.Text : null;
    }
}
