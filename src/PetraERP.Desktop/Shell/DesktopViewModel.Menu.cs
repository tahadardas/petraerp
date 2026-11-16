using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Printing;

namespace PetraERP.Desktop.Shell;

/// <summary>Bridges the single menu description (<see cref="MenuRegistry"/>) to the shell's existing commands.</summary>
public sealed partial class DesktopViewModel
{
    /// <summary>Opens the report options window; set by the WPF shell window.</summary>
    public Action? ReportOptionsRequested { get; set; }

    private RelayCommand? reportOptionsCommand;

    public RelayCommand OpenReportOptionsCommand => reportOptionsCommand ??= new RelayCommand(() => ReportOptionsRequested?.Invoke(), () => Preferences is not null);

    /// <summary>Opens the print and invoice template designer; set by the WPF shell window.</summary>
    public Action? TemplateDesignerRequested { get; set; }

    private RelayCommand? templateDesignerCommand;

    public RelayCommand OpenTemplateDesignerCommand => templateDesignerCommand ??= new RelayCommand(() => TemplateDesignerRequested?.Invoke(), () => IsAuthenticated);

    private AsyncRelayCommand<Management.ReportEntryRow>? openCatalogReportCommand;

    /// <summary>Opens a catalogue report from a menu row through the same dispatcher the reports center uses (nothing unopenable is routed).</summary>
    public AsyncRelayCommand<Management.ReportEntryRow> OpenCatalogReportCommand => openCatalogReportCommand ??= new AsyncRelayCommand<Management.ReportEntryRow>(
        row => row is { IsOpenable: true } && Management.ReportRouteDispatcher.Resolve(row.Entry.Route, row.Entry.Id) is { } destination
            ? NavigateToAsync(destination.Route, destination.Parameter) : Task.CompletedTask);

    private string fiscalYearLabel = string.Empty;

    /// <summary>The fiscal year of the open workspace (empty when none is selected); shown beside the branch on documents and printouts.</summary>
    public string FiscalYearLabel => reportingFiscalYearId is null ? string.Empty : fiscalYearLabel;

    /// <summary>Opens the print preview of a document; set by the WPF shell window.</summary>
    public Action<PrintJob>? PrintPreviewRequested { get; set; }

    /// <summary>A document asked to be previewed or printed: the shell adds the company, branch, fiscal year, user and date, then shows the preview.</summary>
    private void OnPrintRequested(PrintJob job)
    {
        var data = PrintDocumentBuilders.WithContext(job.Data, CompanyLabel, BranchLabel, FiscalYearLabel, UserLabel, time.GetLocalNow());
        PrintPreviewRequested?.Invoke(job with { Data = data });
    }

    /// <summary>True when documents are internal windows of the classic MDI workspace (the preference, not a tab or detached mode).</summary>
    public bool IsClassicMdi => Preferences?.WindowMode == WindowPresentationMode.ClassicMdi;

    /// <summary>
    /// The command a shell-command menu node runs, or null for an unknown id. The command's own <c>CanExecute</c> decides whether
    /// the menu item is enabled, exactly as when the items were declared in XAML.
    /// </summary>
    public ICommand? MenuCommand(string id) => id switch
    {
        "workspace" => OpenWorkspaceDialogCommand,
        "new-journal" => NewJournalCommand,
        "palette" => Palette.OpenCommand,
        "reconnect" => RefreshCommand,
        "logout" => LogoutCommand,
        "logout-all" => LogoutAllCommand,
        "preferences" => OpenPreferencesCommand,
        "report-options" => OpenReportOptionsCommand,
        "template-designer" => OpenTemplateDesignerCommand,
        "language" => ToggleLanguageCommand,
        "theme" => ToggleThemeCommand,
        "window-next" => NextDocumentCommand,
        "window-previous" => PreviousDocumentCommand,
        "window-close" => CloseActiveDocumentCommand,
        "window-close-others" => CloseOtherDocumentsCommand,
        "window-close-all" => CloseAllDocumentsCommand,
        "window-detach" => DetachActiveDocumentCommand,
        "window-tile-horizontal" => TileHorizontallyCommand,
        "window-tile-vertical" => TileVerticallyCommand,
        "window-cascade" => CascadeWindowsCommand,
        "window-minimize" => MinimizeActiveDocumentCommand,
        "window-restore-all" => RestoreMinimizedDocumentsCommand,
        "window-bring-front" => BringAllWindowsToFrontCommand,
        "window-reattach" => ReattachAllDocumentsCommand,
        _ => null,
    };
}
