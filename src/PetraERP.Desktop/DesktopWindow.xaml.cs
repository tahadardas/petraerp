using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop;

/// <summary>The single application shell window. Code-behind only routes global shortcuts to view-model commands and focus.</summary>
public partial class DesktopWindow : Window
{
    public DesktopWindow(DesktopViewModel model)
    {
        InitializeComponent(); Model = model; DataContext = model;
        SizeChanged += (_, _) => UpdateWorkArea();
        DetachedWindows = new DetachedDocumentWindows(this, model);
        // Window menu: in the classic MDI mode the commands lay out the internal windows inside the MDI container
        // (its own bounds, never the screen work area); in the other modes they keep arranging detached windows.
        model.TileHorizontallyRequested = () => { if (Model.IsClassicMdi) MdiHost.TileHorizontally(); else DetachedWindows.TileHorizontally(); };
        model.TileVerticallyRequested = () => { if (Model.IsClassicMdi) MdiHost.TileVertically(); else DetachedWindows.TileVertically(); };
        model.CascadeRequested = () => { if (Model.IsClassicMdi) MdiHost.Cascade(); else DetachedWindows.Cascade(); };
        model.MinimizeActiveRequested = () => { if (Model.IsClassicMdi) MdiHost.MinimizeActive(); else DetachedWindows.MinimizeActive(); };
        model.RestoreAllRequested = () => { if (Model.IsClassicMdi) MdiHost.RestoreAll(); else DetachedWindows.RestoreAll(); };
        model.BringAllWindowsToFrontRequested = () => { if (Model.IsClassicMdi) MdiHost.BringAllToFront(); else DetachedWindows.BringAllWindowsToFront(); };

        model.ProgramOptionsRequested = ShowProgramOptions;
        model.ReportOptionsRequested = ShowReportOptions;
        model.TemplateDesignerRequested = ShowTemplateDesigner;
        model.PrintPreviewRequested = ShowPrintPreview;
        MdiHost.Attach(model.Documents, model.Localization);
        MdiHost.ContentFlow = model.Localization.FlowDirection;
        MdiHost.ActivateRequested += (_, document) => { if (!document.IsActive) Model.ActivateDocumentCommand.Execute(document); };
        MdiHost.CloseRequested += (_, document) => _ = Model.CloseDocumentCommand.ExecuteAsync(document);

        // The menu bar is built from the single menu description and rebuilt whenever it can change.
        model.Navigation.PropertyChanged += OnNavigationChanged;
        model.PropertyChanged += OnModelChanged;
        model.Themes.ThemeChanged += (_, _) => RebuildMenu();
        model.Localization.LanguageChanged += (_, _) => { MdiHost.ContentFlow = Model.Localization.FlowDirection; RebuildMenu(); };
        if (model.Preferences is { } preferences) preferences.PropertyChanged += OnPreferencesChanged;
        model.Documents.Documents.CollectionChanged += (_, _) => ApplyPresentation();
        model.Documents.ActiveChanged += (_, _) => ApplyPresentation();
        RebuildMenu();
        ApplyPresentation();
    }

    private bool exitConfirmed;

    /// <summary>The program-options "confirm before closing": the first close is held back, asked about, and repeated when answered yes.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || exitConfirmed || !EntryOptionsHub.Current.ConfirmOnExit) return;
        e.Cancel = true;
        _ = ConfirmThenCloseAsync();
    }

    private async Task ConfirmThenCloseAsync()
    {
        if (!await Model.ConfirmExitAsync().ConfigureAwait(true)) return;
        exitConfirmed = true;
        Close();
    }

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(NavigationService.TopMenu)) RebuildMenu();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DesktopViewModel.BranchLabel) or nameof(DesktopViewModel.PeriodLabel) or nameof(DesktopViewModel.CompanyLabel)) UpdateContextCaption();
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(UserPreferencesService.WindowMode)) ApplyPresentation();
    }

    private void ShowProgramOptions()
    {
        if (Model.PreferencesPage is not { } page) return;
        _ = PetraWindows.ShowDialog(new ProgramOptionsWindow(Model, page), this);
    }

    private void ShowPrintPreview(Printing.PrintJob job)
    {
        if (Model.Company is not { } company) return;
        var store = Printing.DocumentPrinting.Store;
        var preview = new Printing.DocumentPrintWindow(new Printing.DocumentPrintViewModel(job, store, company.Id, Model.Localization));
        preview.DesignRequested = viewModel =>
        {
            if (viewModel.SelectedTemplate is not { } template) return;
            var designer = new Printing.PrintTemplateDesignerWindow(template, store, company.Id, job.Data);
            _ = PetraWindows.ShowDialog(designer, preview);
            viewModel.ReloadTemplates(designer.SavedTemplateId ?? template.Id);
        };
        PetraWindows.ShowTool(preview, this);
    }

    private void ShowReportOptions()
    {
        if (Model.Preferences is not { } preferences) return;
        _ = PetraWindows.ShowDialog(new Reporting.ReportOptionsWindow(preferences, Model.Localization), this);
    }

    private void ShowTemplateDesigner()
    {
        if (Model.Company is not { } company) return;
        var store = Printing.DocumentPrinting.Store;
        var template = store.Resolve(company.Id, PetraERP.Reporting.OpenSource.Printing.PrintDocumentKind.SalesInvoice);
        var sampleData = PetraERP.Reporting.OpenSource.Printing.PrintFieldCatalog.Sample(PetraERP.Reporting.OpenSource.Printing.PrintDocumentKind.SalesInvoice);
        _ = PetraWindows.ShowDialog(new Printing.PrintTemplateDesignerWindow(template, store, company.Id, sampleData), this);
    }

    /// <summary>Every document window carries the branch and fiscal year it works in.</summary>
    private void UpdateContextCaption()
    {
        var parts = new[] { Model.BranchLabel, Model.FiscalYearLabel }.Where(part => !string.IsNullOrWhiteSpace(part));
        MdiHost.ContextCaption = string.Join(" · ", parts);
    }

    private void RebuildMenu() => TopMenuBuilder.Rebuild(MainMenu, Model);

    /// <summary>
    /// Chooses what renders the documents. Classic MDI: the internal windows; the page host is emptied (a document is never
    /// rendered by two hosts) and Home is only the free background while no window is open. Tabbed / separate windows: as before.
    /// </summary>
    private void ApplyPresentation()
    {
        UpdateContextCaption();
        var mdi = Model.IsClassicMdi;
        // Al-Ameen's classic workspace does not mix tabbed documents with internal MDI windows.
        // Hiding the strip also gives its entire row back to the work area.
        DocumentTabsStrip.Visibility = mdi ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceArea.Margin = mdi ? new Thickness(0) : (Thickness)FindResource("Space.Page");
        // A nested scrolling viewport must never scroll the MDI canvas while windows are being dragged.
        WorkspaceViewport.MinimumContentHeight = mdi ? 0 : (double)FindResource("Workspace.MinimumHeight");
        WorkspaceViewport.VerticalScrollBarVisibility = mdi ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        // Release the previous visual host *before* constructing the other one. This prevents
        // duplicate editor visuals, lost keyboard focus and conflicting controls during a mode switch.
        if (mdi)
        {
            if (BindingOperations.IsDataBound(PageHost, ContentControl.ContentProperty))
            {
                BindingOperations.ClearBinding(PageHost, ContentControl.ContentProperty);
                PageHost.Content = null;
            }
            MdiHost.SetEnabledMode(true);
        }
        else
        {
            MdiHost.SetEnabledMode(false);
            if (!BindingOperations.IsDataBound(PageHost, ContentControl.ContentProperty))
                PageHost.SetBinding(ContentControl.ContentProperty, new Binding(nameof(DesktopViewModel.CurrentView)));
        }
        MdiHost.Visibility = mdi ? Visibility.Visible : Visibility.Collapsed;
        HomeSurface.Visibility = Model.IsHome && (!mdi || Model.Documents.Count <= 1) ? Visibility.Visible : Visibility.Collapsed;
        PageSurface.Visibility = !mdi && !Model.IsHome ? Visibility.Visible : Visibility.Collapsed;
    }

    public DesktopViewModel Model { get; }
    /// <summary>Top-level windows of detached documents (Window menu "Open in separate window").</summary>
    public DetachedDocumentWindows DetachedWindows { get; }

    private void OnShortcut(object sender, KeyEventArgs args)
    {
        if (HandleShortcut(args.Key == Key.System ? args.SystemKey : args.Key, Keyboard.Modifiers)) args.Handled = true;
    }

    internal bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (DesktopShortcutRouter.HandleKey(this, Model, null, key, modifiers)) return true;
        if (!Model.IsAuthenticated || Model.Dialogs?.Current is not null) return false;
        if (modifiers == ModifierKeys.Control && key == Key.K)
        {
            if (Model.Palette.IsOpen) Model.Palette.IsOpen = false; else Model.Palette.Open();
            return true;
        }
        if (Model.Palette.IsOpen) return false;
        if (key == Key.Escape && modifiers == ModifierKeys.None && Model.Notifications.IsPanelOpen) { Model.Notifications.IsPanelOpen = false; return true; }
        if (key == Key.F12 && modifiers == ModifierKeys.None) { Model.ToggleToolbarCommand.Execute(null); return true; }
        // Workspace tabs: Ctrl+Tab / Ctrl+Shift+Tab cycle, Ctrl+W or Ctrl+F4 closes the active document (with confirmation when dirty).
        if (key == Key.Tab && modifiers == ModifierKeys.Control) { Model.NextDocumentCommand.Execute(null); return true; }
        if (key == Key.Tab && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { Model.PreviousDocumentCommand.Execute(null); return true; }
        if ((modifiers == ModifierKeys.Control && key is Key.W or Key.F4))
        {
            if (Model.Documents.Active.IsClosable) _ = Model.CloseActiveDocumentCommand.ExecuteAsync(null);
            return true;
        }
        // In classic MDI the active view is hosted by the internal window, not PageHost.
        // The old shortcuts searched an empty PageHost and appeared broken to the operator.
        var visualRoot = ActiveDocumentVisualRoot;
        if (modifiers == ModifierKeys.Alt && key >= Key.D1 && key <= Key.D3)
            return FindPaneHost(visualRoot)?.SelectPane(key - Key.D1, true) == true;
        if (modifiers == ModifierKeys.Control && key == Key.F)
        {
            // Ctrl+F focuses the current page's list search; on pages without one it opens the palette.
            if (FindSearchTarget(visualRoot) is { } target)
            {
                FindPaneHost(visualRoot)?.Reveal(target); UpdateLayout();
                target.BringIntoView(); target.Focus(); (target as TextBox)?.SelectAll();
            }
            else Model.Palette.Open();
            return true;
        }

        return false;
    }

    /// <summary>Returns the active view's actual visual host in both classic MDI and tabbed presentation.</summary>
    private DependencyObject ActiveDocumentVisualRoot => Model.IsClassicMdi
        ? (DependencyObject?)MdiHost.Windows.FirstOrDefault(child => child.Document.IsActive) ?? MdiHost
        : PageHost;

    /// <summary>Middle-click closes a tab (with the usual unsaved-changes confirmation).</summary>
    private void TabMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement { DataContext: WorkspaceDocument { IsClosable: true } document })
        {
            _ = Model.CloseDocumentCommand.ExecuteAsync(document);
            e.Handled = true;
        }
    }

    /// <summary>Builds the tab's context menu for the tab under the pointer (close, close others, close all, detach).</summary>
    private void TabContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkspaceDocument document } tab) return;
        var localization = Model.Localization;
        var menu = new ContextMenu { FlowDirection = FlowDirection, PlacementTarget = tab };
        menu.Items.Add(new MenuItem { Header = localization["D_CloseTab"], InputGestureText = "Ctrl+W", Command = Model.CloseDocumentCommand, CommandParameter = document, IsEnabled = document.IsClosable });
        menu.Items.Add(new MenuItem { Header = localization["D_CloseOtherTabs"], Command = Model.CloseOtherTabsCommand, CommandParameter = document });
        menu.Items.Add(new MenuItem { Header = localization["D_CloseAllTabs"], Command = Model.CloseAllDocumentsCommand });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = localization["D_DetachTab"], Command = Model.DetachDocumentCommand, CommandParameter = document });
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void UpdateWorkArea()
    {
        _ = ActualWidth;
    }

    internal static AdaptivePaneGrid? FindPaneHost(DependencyObject root)
    {
        if (root is AdaptivePaneGrid host) return host;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindPaneHost(VisualTreeHelper.GetChild(root, index)) is { } found) return found;
        return null;
    }

    internal static FrameworkElement? FindSearchTarget(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element && SearchTarget.GetIsTarget(element)) return element;
            if (FindSearchTarget(child) is { } found) return found;
        }

        return null;
    }
}
