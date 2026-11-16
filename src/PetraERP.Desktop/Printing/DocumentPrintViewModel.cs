using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.Desktop.Localization;
using PetraERP.Reporting.OpenSource.Printing;

namespace PetraERP.Desktop.Printing;

/// <summary>One rendered page with its natural size in device-independent pixels.</summary>
public sealed record PreviewPage(BitmapSource Source, double WidthDip, double HeightDip);

/// <summary>
/// Preview of one document through a print template. It renders on a worker thread, keeps only the latest render, and never changes the
/// document: choosing another template only changes how the same values are laid out.
/// </summary>
public sealed partial class DocumentPrintViewModel : ObservableObject
{
    private const int PreviewResolution = 110;
    private const int PrintResolution = 200;
    private readonly PrintTemplateStore store;
    private readonly Guid companyId;
    private int renderVersion;

    [ObservableProperty] private PrintTemplate? selectedTemplate;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string error = string.Empty;
    [ObservableProperty] private double zoomPercent = 100;
    [ObservableProperty] private string pageSummary = string.Empty;

    public DocumentPrintViewModel(PrintJob job, PrintTemplateStore store, Guid companyId, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(localization);
        Job = job; this.store = store; this.companyId = companyId; Localization = localization;
        ReloadTemplates(null);
    }

    public PrintJob Job { get; }
    public LocalizationService Localization { get; }
    public ObservableCollection<PrintTemplate> Templates { get; } = [];
    public ObservableCollection<PreviewPage> Pages { get; } = [];
    public string Title => Job.Title;
    public double ZoomFactor => ZoomPercent / 100d;
    public bool IsDefaultTemplate => SelectedTemplate is { } template && store.DefaultId(companyId, Job.Kind) == template.Id;
    public Guid CompanyId => companyId;

    partial void OnZoomPercentChanged(double value) => OnPropertyChanged(nameof(ZoomFactor));

    partial void OnSelectedTemplateChanged(PrintTemplate? value)
    {
        OnPropertyChanged(nameof(IsDefaultTemplate));
        _ = RenderAsync();
    }

    /// <summary>Lists the company's templates and selects <paramref name="selectId"/>, else the company default.</summary>
    public void ReloadTemplates(string? selectId)
    {
        var chosen = store.Resolve(companyId, Job.Kind);
        Templates.Clear();
        foreach (var template in store.List(companyId, Job.Kind)) Templates.Add(template);
        SelectedTemplate = Templates.FirstOrDefault(template => template.Id == (selectId ?? chosen.Id)) ?? Templates[0];
    }

    /// <summary>Makes the selected template the company's choice for this document kind.</summary>
    public void SetSelectedAsDefault()
    {
        if (SelectedTemplate is not { } template) return;
        store.SetDefault(companyId, Job.Kind, template.Id);
        OnPropertyChanged(nameof(IsDefaultTemplate));
    }

    public void ChangeZoom(double delta) => ZoomPercent = Math.Clamp(ZoomPercent + delta, 40, 250);

    /// <summary>Renders the selected template; a newer request makes an older one discard its result.</summary>
    public async Task RenderAsync()
    {
        if (SelectedTemplate is not { } template) return;
        var version = Interlocked.Increment(ref renderVersion);
        IsBusy = true; Error = string.Empty;
        try
        {
            var images = await Task.Run(() => PrintTemplateRenderer.RenderPages(template, Job.Data, PreviewResolution)).ConfigureAwait(true);
            if (version != renderVersion) return;
            Pages.Clear();
            foreach (var png in images) Pages.Add(ToPage(png, template));
            PageSummary = string.Format(Localization.Culture, Localization["PRN_Pages"], Pages.Count);
        }
        catch (Exception exception)
        {
            if (version == renderVersion) { Pages.Clear(); Error = Localization["PRN_RenderFailed"] + " " + exception.Message; PageSummary = string.Empty; }
        }
        finally
        {
            if (version == renderVersion) IsBusy = false;
        }
    }

    /// <summary>Renders every page for the printer at print resolution (off the UI thread).</summary>
    public Task<IReadOnlyList<byte[]>> RenderForPrintAsync() =>
        SelectedTemplate is { } template ? Task.Run(() => PrintTemplateRenderer.RenderPages(template, Job.Data, PrintResolution)) : Task.FromResult<IReadOnlyList<byte[]>>([]);

    public Task<byte[]> RenderPdfAsync() =>
        SelectedTemplate is { } template ? Task.Run(() => PrintTemplateRenderer.RenderPdf(template, Job.Data)) : Task.FromResult(Array.Empty<byte>());

    internal static PreviewPage ToPage(byte[] png, PrintTemplate template)
    {
        using var stream = new MemoryStream(png);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        var settings = template.Page;
        return new PreviewPage(frame, settings.EffectivePaperWidthMm * 96d / 25.4d, settings.EffectivePaperHeightMm * 96d / 25.4d);
    }
}
