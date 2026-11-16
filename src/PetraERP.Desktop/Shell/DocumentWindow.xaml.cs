using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// Top-level window for one detached workspace document. It holds no business logic: closing goes through the shell's
/// close command (dirty confirmation, blocked while a posting outcome is unknown), and "return to tabs" goes through the
/// shell's attach command. Only <see cref="DetachedDocumentWindows"/> closes it for real, after releasing the view.
/// </summary>
public partial class DocumentWindow : Window
{
    private bool released;

    internal DocumentWindow(DesktopViewModel shell, WorkspaceDocument document)
    {
        InitializeComponent();
        Shell = shell; Document = document; DataContext = shell;
        Host.Content = document.Content;
        document.PropertyChanged += DocumentChanged;
        shell.Localization.LanguageChanged += LanguageChanged;
        UpdateHeader();
    }

    public DesktopViewModel Shell { get; }
    public WorkspaceDocument Document { get; }
    /// <summary>The element that renders the document's view model (exposed for tests).</summary>
    public FrameworkElement ContentHost => Host;

    /// <summary>Releases the view (so it is in no tree) and closes without asking: the shell already decided.</summary>
    internal void Release()
    {
        if (released) return;
        released = true;
        Document.PropertyChanged -= DocumentChanged;
        Shell.Localization.LanguageChanged -= LanguageChanged;
        Host.Content = null;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (released) return;
        // The window's close box is a document close request: the shell applies the tab-close rules. It runs after this
        // Closing event, because a confirmed close closes this window again through the host.
        e.Cancel = true;
        Dispatcher.BeginInvoke(new Action(RequestClose));
    }

    private void RequestClose() => _ = Shell.CloseDocumentCommand.ExecuteAsync(Document);

    private void CloseClicked(object sender, RoutedEventArgs e) => RequestClose();

    private void AttachClicked(object sender, RoutedEventArgs e) => Shell.AttachDocumentCommand.Execute(Document);

    private void OnShortcut(object sender, KeyEventArgs args)
    {
        if (HandleShortcut(args.Key == Key.System ? args.SystemKey : args.Key, Keyboard.Modifiers)) args.Handled = true;
    }

    /// <summary>
    /// The shell shortcuts that make sense for one document: Ctrl+W/Ctrl+F4 close it (tab rules), Ctrl+F focuses its search,
    /// Alt+1…3 select its panes, Ctrl+K opens the palette in the main window. Ignored while a confirmation is open.
    /// </summary>
    internal bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (DesktopShortcutRouter.HandleKey(this, Shell, Document, key, modifiers)) return true;
        if (!Shell.IsAuthenticated || Shell.Dialogs?.Current is not null) return false;
        if (modifiers == ModifierKeys.Control && key is Key.W or Key.F4) { RequestClose(); return true; }
        if (modifiers == ModifierKeys.Control && key == Key.K)
        {
            Owner?.Activate(); MainShell?.Activate();
            Shell.Palette.Open();
            return true;
        }

        if (modifiers == ModifierKeys.Alt && key >= Key.D1 && key <= Key.D3)
            return PetraERP.Desktop.DesktopWindow.FindPaneHost(Host)?.SelectPane(key - Key.D1, true) == true;
        if (modifiers == ModifierKeys.Control && key == Key.F && PetraERP.Desktop.DesktopWindow.FindSearchTarget(Host) is { } target)
        {
            PetraERP.Desktop.DesktopWindow.FindPaneHost(Host)?.Reveal(target); UpdateLayout();
            target.BringIntoView(); target.Focus(); (target as System.Windows.Controls.TextBox)?.SelectAll();
            return true;
        }

        return false;
    }

    /// <summary>The main shell window (palette and tab host), set by the detached-window host.</summary>
    internal Window? MainShell { get; set; }

    private void DocumentChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(WorkspaceDocument.Title) or nameof(WorkspaceDocument.IsDirty) or null) UpdateHeader();
    }

    private void LanguageChanged(object? sender, EventArgs args) => UpdateHeader();

    private void UpdateHeader()
    {
        Title = Document.Title + " — PetraERP";
        DocumentTitle.Text = Document.Title;
        DirtyMarker.Visibility = Document.IsDirty ? Visibility.Visible : Visibility.Collapsed;
        // A report window is a window by design, not a tab that happens to be loose: "return to the tabs" and
        // "close the tab" describe a place the operator never put it, so they are replaced by window wording.
        var report = DesktopViewModel.OpensAsWindow(Document.Route ?? string.Empty);
        AttachButton.Visibility = report ? Visibility.Collapsed : Visibility.Visible;
        CloseButton.Content = Shell.Localization[report ? "D_CloseWindow" : "D_CloseTab"];
    }
}
