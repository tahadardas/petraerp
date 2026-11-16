using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using PetraERP.Desktop.Platform;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// WPF host for detached documents. It follows <see cref="WorkspaceDocument.IsDetached"/> and the open-document list: a
/// detached document gets one <see cref="DocumentWindow"/>; attaching, closing, revalidation, a context switch or logout
/// (anything that removes the document) releases the view and closes that window. Decisions stay in the shell view model.
/// </summary>
public sealed class DetachedDocumentWindows : IDisposable
{
    private readonly Window owner;
    private readonly DesktopViewModel shell;
    private readonly Dictionary<WorkspaceDocument, DocumentWindow> windows = [];
    private readonly DispatcherTimer placementTimer;

    public DetachedDocumentWindows(Window owner, DesktopViewModel shell)
    {
        this.owner = owner; this.shell = shell;
        placementTimer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        placementTimer.Tick += PlacementTick;
        foreach (var document in shell.Documents.Documents) document.PropertyChanged += DocumentChanged;
        shell.Documents.Documents.CollectionChanged += DocumentsChanged;
        shell.Documents.DetachedActivationRequested += ActivationRequested;
        owner.Closed += OwnerClosed;
    }

    /// <summary>The open detached windows (for tests and diagnostics).</summary>
    public IReadOnlyCollection<DocumentWindow> Windows => windows.Values;

    public DocumentWindow? WindowOf(WorkspaceDocument document) => windows.GetValueOrDefault(document);

    public void TileHorizontally()
    {
        var openWindows = windows.Values.Where(w => w.IsVisible).ToList();
        if (openWindows.Count == 0) return;

        var workArea = SystemParameters.WorkArea;
        var height = workArea.Height / openWindows.Count;
        for (var i = 0; i < openWindows.Count; i++)
        {
            var win = openWindows[i];
            if (win.WindowState == WindowState.Minimized || win.WindowState == WindowState.Maximized)
                win.WindowState = WindowState.Normal;
            win.Left = workArea.Left;
            win.Top = workArea.Top + (i * height);
            win.Width = workArea.Width;
            win.Height = Math.Max(200, height);
            EnsureSafeBounds(win);
        }
    }

    public void TileVertically()
    {
        var openWindows = windows.Values.Where(w => w.IsVisible).ToList();
        if (openWindows.Count == 0) return;

        var workArea = SystemParameters.WorkArea;
        var width = workArea.Width / openWindows.Count;
        for (var i = 0; i < openWindows.Count; i++)
        {
            var win = openWindows[i];
            if (win.WindowState == WindowState.Minimized || win.WindowState == WindowState.Maximized)
                win.WindowState = WindowState.Normal;
            win.Left = workArea.Left + (i * width);
            win.Top = workArea.Top;
            win.Width = Math.Max(300, width);
            win.Height = workArea.Height;
            EnsureSafeBounds(win);
        }
    }

    public void Cascade()
    {
        var openWindows = windows.Values.Where(w => w.IsVisible).ToList();
        if (openWindows.Count == 0) return;

        var workArea = SystemParameters.WorkArea;
        var width = Math.Min(1100, Math.Max(800, workArea.Width * 0.75));
        var height = Math.Min(750, Math.Max(500, workArea.Height * 0.75));
        const double step = 30.0;

        for (var i = 0; i < openWindows.Count; i++)
        {
            var win = openWindows[i];
            if (win.WindowState == WindowState.Minimized || win.WindowState == WindowState.Maximized)
                win.WindowState = WindowState.Normal;
            var offset = (i % 10) * step;
            win.Left = workArea.Left + offset;
            win.Top = workArea.Top + offset;
            win.Width = width;
            win.Height = height;
            EnsureSafeBounds(win);
            win.Activate();
        }
    }

    public void MinimizeActive()
    {
        var active = windows.Values.FirstOrDefault(w => w.IsActive);
        if (active is not null)
        {
            active.WindowState = WindowState.Minimized;
        }
        else if (owner.IsActive)
        {
            owner.WindowState = WindowState.Minimized;
        }
    }

    public void RestoreAll()
    {
        owner.WindowState = WindowState.Normal;
        owner.Activate();
        foreach (var win in windows.Values)
        {
            if (win.WindowState == WindowState.Minimized)
                win.WindowState = WindowState.Normal;
            EnsureSafeBounds(win);
            win.Activate();
        }
    }

    public void BringAllWindowsToFront()
    {
        owner.Activate();
        foreach (var win in windows.Values)
        {
            EnsureSafeBounds(win);
            win.Activate();
        }
    }

    public static void EnsureSafeBounds(Window window)
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;

        if (window.Left + 50 < virtualLeft || window.Left > virtualLeft + virtualWidth - 50 ||
            window.Top + 30 < virtualTop || window.Top > virtualTop + virtualHeight - 30)
        {
            window.Left = SystemParameters.WorkArea.Left + 40;
            window.Top = SystemParameters.WorkArea.Top + 40;
        }
    }


    private void DocumentsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        foreach (var document in args.OldItems?.OfType<WorkspaceDocument>() ?? []) { document.PropertyChanged -= DocumentChanged; Release(document); }
        foreach (var document in args.NewItems?.OfType<WorkspaceDocument>() ?? []) { document.PropertyChanged += DocumentChanged; Sync(document); }
        if (args.Action == NotifyCollectionChangedAction.Reset)
            foreach (var document in windows.Keys.Where(document => !shell.Documents.Documents.Contains(document)).ToArray()) Release(document);
    }

    private void DocumentChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is WorkspaceDocument document && args.PropertyName == nameof(WorkspaceDocument.IsDetached)) Sync(document);
    }

    private void Sync(WorkspaceDocument document)
    {
        if (!document.IsDetached || !shell.Documents.Documents.Contains(document)) { Release(document); return; }
        if (windows.ContainsKey(document)) return;
        // The manager has already moved the main host to another document, so this view model is rendered nowhere else.
        var offset = 32 * (windows.Count + 1);
        var window = new DocumentWindow(shell, document)
        {
            // Cascaded from the main window, on the same monitor; the user can then move it anywhere.
            WindowStartupLocation = WindowStartupLocation.Manual, Left = owner.Left + offset, Top = owner.Top + offset,
            ShowActivated = owner.ShowActivated,
            MainShell = owner,
        };
        var saved = document.Placement;
        window.SourceInitialized += (_, _) => WindowsDocumentPlacement.Apply(window, owner, saved, windows.Count);
        windows[document] = window;
        window.Show();
        if (saved is { IsValid: true, Maximized: true }) window.WindowState = WindowState.Maximized;
        window.LocationChanged += WindowGeometryChanged;
        window.SizeChanged += WindowSizeChanged;
        window.StateChanged += WindowGeometryChanged;
        Capture(window);
        if (owner.ShowActivated) window.Activate();
    }

    private void Release(WorkspaceDocument document)
    {
        if (windows.Remove(document, out var window))
        {
            Capture(window);
            window.LocationChanged -= WindowGeometryChanged;
            window.SizeChanged -= WindowSizeChanged;
            window.StateChanged -= WindowGeometryChanged;
            window.Release();
        }
    }

    private void Capture(DocumentWindow window)
    {
        if (WindowsDocumentPlacement.Capture(window) is { } placement) shell.RememberWindowPlacement(window.Document, placement);
    }

    private void WindowSizeChanged(object sender, SizeChangedEventArgs args) => WindowGeometryChanged(sender, args);
    private void WindowGeometryChanged(object? sender, EventArgs args)
    {
        if (sender is DocumentWindow window) Capture(window);
        placementTimer.Stop(); placementTimer.Start();
    }

    private void PlacementTick(object? sender, EventArgs args) => FlushPlacements();

    /// <summary>Coalesces disk writes during move/resize; called explicitly before releasing the window host.</summary>
    internal void FlushPlacements()
    {
        placementTimer.Stop();
        foreach (var window in windows.Values) Capture(window);
        shell.PersistWindowPlacements();
    }

    private void ActivationRequested(object? sender, WorkspaceDocument document)
    {
        if (!windows.TryGetValue(document, out var window)) return;
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = document.Placement?.Maximized == true ? WindowState.Maximized : WindowState.Normal;
        window.Activate();
    }

    private void OwnerClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        FlushPlacements();
        placementTimer.Tick -= PlacementTick;
        owner.Closed -= OwnerClosed;
        shell.Documents.Documents.CollectionChanged -= DocumentsChanged;
        shell.Documents.DetachedActivationRequested -= ActivationRequested;
        foreach (var document in shell.Documents.Documents) document.PropertyChanged -= DocumentChanged;
        foreach (var document in windows.Keys.ToArray()) Release(document);
    }
}
