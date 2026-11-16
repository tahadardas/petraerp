using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PetraERP.Desktop.Printing;

/// <summary>
/// Preview, print and PDF of one document. Printing renders the same template at print resolution and sends the pages to the chosen
/// printer; nothing about the document changes. The designer is opened through <see cref="DesignRequested"/> so this window stays a viewer.
/// </summary>
public partial class DocumentPrintWindow : Window
{
    private readonly DocumentPrintViewModel model;

    public DocumentPrintWindow(DocumentPrintViewModel model)
    {
        InitializeComponent();
        this.model = model;
        DataContext = model;
        DesignButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>Asked to open the template designer for the selected template; when unset the Design button stays hidden.</summary>
    public Action<DocumentPrintViewModel>? DesignRequested
    {
        get => designRequested;
        set { designRequested = value; DesignButton.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible; }
    }

    private Action<DocumentPrintViewModel>? designRequested;

    private void OnDesign(object sender, RoutedEventArgs args) => designRequested?.Invoke(model);

    private void OnSetDefault(object sender, RoutedEventArgs args) => model.SetSelectedAsDefault();

    private void OnZoomIn(object sender, RoutedEventArgs args) => model.ChangeZoom(10);

    private void OnZoomOut(object sender, RoutedEventArgs args) => model.ChangeZoom(-10);

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.P) { OnPrint(this, new RoutedEventArgs()); args.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && args.Key is Key.OemPlus or Key.Add) { model.ChangeZoom(10); args.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && args.Key is Key.OemMinus or Key.Subtract) { model.ChangeZoom(-10); args.Handled = true; }
        else if (args.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { Close(); args.Handled = true; }
    }

    private async void OnPrint(object sender, RoutedEventArgs args)
    {
        if (model.SelectedTemplate is not { } template) return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        try
        {
            model.IsBusy = true;
            var images = await model.RenderForPrintAsync().ConfigureAwait(true);
            var document = BuildDocument(images, template.Page.EffectivePaperWidthMm, template.Page.EffectivePaperHeightMm);
            dialog.PrintDocument(document.DocumentPaginator, model.Title);
        }
        catch (Exception exception) when (exception is PrintQueueException or PrintSystemException or InvalidOperationException or IOException)
        {
            model.Error = model.Localization["PRN_PrintFailed"] + " " + exception.Message;
        }
        finally
        {
            model.IsBusy = false;
        }
    }

    private async void OnSavePdf(object sender, RoutedEventArgs args)
    {
        var dialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = SafeFileName(model.Title) + ".pdf", AddExtension = true, DefaultExt = ".pdf" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            model.IsBusy = true;
            var pdf = await model.RenderPdfAsync().ConfigureAwait(true);
            await File.WriteAllBytesAsync(dialog.FileName, pdf).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            model.Error = model.Localization["PRN_PrintFailed"] + " " + exception.Message;
        }
        finally
        {
            model.IsBusy = false;
        }
    }

    /// <summary>One fixed page per rendered image, sized to the paper so the printer driver does not rescale it.</summary>
    private static FixedDocument BuildDocument(IReadOnlyList<byte[]> pages, double paperWidthMm, double paperHeightMm)
    {
        var width = paperWidthMm * 96d / 25.4d;
        var height = paperHeightMm * 96d / 25.4d;
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(width, height);
        foreach (var png in pages)
        {
            using var stream = new MemoryStream(png);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            var page = new FixedPage { Width = width, Height = height, FlowDirection = FlowDirection.LeftToRight };
            page.Children.Add(new Image { Source = frame, Width = width, Height = height, Stretch = System.Windows.Media.Stretch.Fill });
            var content = new PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(page);
            document.Pages.Add(content);
        }

        return document;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string([.. name.Select(ch => invalid.Contains(ch) ? '_' : ch)]).Trim();
        return clean.Length == 0 ? "document" : clean[..Math.Min(80, clean.Length)];
    }
}
