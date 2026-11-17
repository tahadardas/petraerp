using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Controls;

/// <summary>The edge or corner a resize gesture started on; <see cref="None"/> means the title bar (move).</summary>
[Flags]
public enum MdiEdge
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

/// <summary>A move or resize gesture of one internal window, in the container's (left-to-right) coordinate space.</summary>
public sealed class MdiDragEventArgs(double horizontalChange, double verticalChange, MdiEdge edge) : EventArgs
{
    public double HorizontalChange { get; } = horizontalChange;
    public double VerticalChange { get; } = verticalChange;
    public MdiEdge Edge { get; } = edge;
}

/// <summary>
/// One internal window of the classic MDI workspace: a small title bar (icon, caption, minimize / maximize-restore / close),
/// resize edges and the document's own view. The chrome is always laid out left-to-right so drag deltas keep their sign;
/// only the content takes the application flow direction. It owns no geometry decisions (the host does).
/// </summary>
public sealed class MdiChildWindow : Border
{
    private const double EdgeThickness = 5;
    private const double CornerSize = 10;

    private readonly Grid root = new() { FlowDirection = FlowDirection.LeftToRight };
    private readonly Grid chrome = new() { FlowDirection = FlowDirection.LeftToRight, Height = MdiLayoutEngine.TitleHeight };
    private readonly Border titleBar = new();
    private readonly TextBlock glyphText = new() { Margin = new Thickness(6, 0, 4, 0), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock titleText = new() { Margin = new Thickness(4, 0, 4, 0), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Thumb dragHandle = new() { Background = System.Windows.Media.Brushes.Transparent, Opacity = 0, Focusable = false };
    private readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
    private readonly Button minimizeButton;
    private readonly Button maximizeButton;
    private readonly Button closeButton;
    private readonly Border body = new();
    private readonly ILocalizationService localization;
    private MdiWindowState state = MdiWindowState.Normal;
    private bool isActiveWindow;
    private string contextCaption = string.Empty;

    public MdiChildWindow(WorkspaceDocument document, FlowDirection contentFlow, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(localization);
        Document = document;
        this.localization = localization;
        Focusable = false;
        SnapsToDevicePixels = true;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        SetResourceReference(BackgroundProperty, "Surface");
        titleBar.CornerRadius = new CornerRadius(7, 7, 0, 0);
        body.CornerRadius = new CornerRadius(0, 0, 7, 7);
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.20, Color = System.Windows.Media.Color.FromRgb(11, 18, 32) };

        minimizeButton = CreateButton("", localization["D_MinimizeActive"], isClose: false, () => MinimizeRequested?.Invoke(this, EventArgs.Empty));
        maximizeButton = CreateButton("", localization["UX_Maximize"], isClose: false, () => MaximizeToggleRequested?.Invoke(this, EventArgs.Empty));
        closeButton = CreateButton("", localization["D_CloseTab"], isClose: true, () => CloseRequested?.Invoke(this, EventArgs.Empty));

        glyphText.SetResourceReference(StyleProperty, "Text.Icon");
        glyphText.Text = document.Glyph;
        glyphText.Visibility = document.Glyph.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        dragHandle.DragStarted += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        dragHandle.DragDelta += (_, e) => Dragged?.Invoke(this, new MdiDragEventArgs(e.HorizontalChange, e.VerticalChange, MdiEdge.None));
        dragHandle.MouseDoubleClick += (_, _) => MaximizeToggleRequested?.Invoke(this, EventArgs.Empty);

        titleBar.Child = chrome;
        chrome.Children.Add(dragHandle);
        chrome.Children.Add(glyphText);
        chrome.Children.Add(titleText);
        chrome.Children.Add(buttons);

        body.Child = new LoadingHost
        {
            Content = document.Content,
            State = (document.Content as ILoadingAware)?.Loading,
            LoadingText = localization["UI_State_Loading"],
            SavingText = localization["UI_State_Saving"],
            PostingText = localization["UI_State_Posting"],
            UnknownText = localization["UI_State_OutcomeUnknown"],
            UnknownHelpText = localization["UI_OutcomeUnknownHelp"],
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(body, 1);
        root.Children.Add(titleBar);
        root.Children.Add(body);
        AddResizeThumbs();
        Child = root;

        document.PropertyChanged += DocumentChanged;
        PreviewMouseDown += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        ApplyFlow(contentFlow);
        UpdateTitle();
        ApplyActiveVisual();
        UpdateStateVisual();
    }

    public WorkspaceDocument Document { get; }
    public MdiWindowState State => state;

    /// <summary>The restored (non-minimized, non-maximized) rectangle; kept so that minimizing or maximizing never loses it.</summary>
    public MdiRect NormalBounds { get; set; }

    /// <summary>True until the host has had a valid container size to place the window in.</summary>
    public bool NeedsPlacement { get; set; }

    public event EventHandler? Activated;
    public event EventHandler? CloseRequested;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? MaximizeToggleRequested;
    public event EventHandler<MdiDragEventArgs>? Dragged;

    public void SetState(MdiWindowState value)
    {
        state = value;
        UpdateStateVisual();
    }

    public void SetActive(bool value)
    {
        isActiveWindow = value;
        ApplyActiveVisual();
    }

    /// <summary>Content follows the application flow direction; the buttons sit on the reading-start side like a mirrored Windows caption.</summary>
    public void ApplyFlow(FlowDirection contentFlow)
    {
        body.FlowDirection = contentFlow;
        titleText.FlowDirection = contentFlow;
        var rtl = contentFlow == FlowDirection.RightToLeft;
        chrome.ColumnDefinitions.Clear();
        chrome.ColumnDefinitions.Add(new ColumnDefinition { Width = rtl ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        chrome.ColumnDefinitions.Add(new ColumnDefinition { Width = rtl ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        // RTL: [buttons][caption ... icon]; LTR: [icon caption ...][buttons]
        Grid.SetColumn(buttons, rtl ? 0 : 1);
        Grid.SetColumn(dragHandle, rtl ? 1 : 0);
        Grid.SetColumn(titleText, rtl ? 1 : 0);
        Grid.SetColumn(glyphText, rtl ? 1 : 0);
        titleText.HorizontalAlignment = rtl ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        glyphText.HorizontalAlignment = rtl ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        titleText.Margin = rtl ? new Thickness(4, 0, 30, 0) : new Thickness(30, 0, 4, 0);
        if (glyphText.Visibility != Visibility.Visible) titleText.Margin = new Thickness(8, 0, 8, 0);

        // Position buttons so Close is always at the outer window corner:
        // RTL (buttons on the left): [Close][Maximize][Minimize]
        // LTR (buttons on the right): [Minimize][Maximize][Close]
        buttons.Children.Clear();
        if (rtl)
        {
            buttons.Children.Add(closeButton);
            buttons.Children.Add(maximizeButton);
            buttons.Children.Add(minimizeButton);
        }
        else
        {
            buttons.Children.Add(minimizeButton);
            buttons.Children.Add(maximizeButton);
            buttons.Children.Add(closeButton);
        }
    }

    /// <summary>Stops observing the document and releases its view, so the content is rendered nowhere else than the next host.</summary>
    public void Release()
    {
        Document.PropertyChanged -= DocumentChanged;
        if (body.Child is LoadingHost host) { host.State = null; host.Content = null; }
        body.Child = null;
    }

    private void DocumentChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(WorkspaceDocument.Title) or nameof(WorkspaceDocument.IsDirty)) UpdateTitle();
    }

    /// <summary>The branch and fiscal year the document belongs to, shown after the title and in its tooltip so the context is never in doubt.</summary>
    public string ContextCaption
    {
        get => contextCaption;
        set
        {
            if (string.Equals(contextCaption, value, StringComparison.Ordinal)) return;
            contextCaption = value;
            UpdateTitle();
        }
    }

    private void UpdateTitle()
    {
        var title = Document.IsDirty ? Document.Title + " ●" : Document.Title;
        titleText.Text = contextCaption.Length == 0 ? title : title + "   |   " + contextCaption;
        titleText.ToolTip = contextCaption.Length == 0 ? null : contextCaption;
        AutomationProperties.SetName(this, Document.Title);
    }

    private void UpdateStateVisual()
    {
        body.Visibility = state == MdiWindowState.Minimized ? Visibility.Collapsed : Visibility.Visible;
        maximizeButton.Content = state == MdiWindowState.Maximized ? "" : "";
        var tip = state == MdiWindowState.Maximized ? localization["D_RestoreAll"] : localization["UX_Maximize"];
        maximizeButton.ToolTip = tip;
        AutomationProperties.SetName(maximizeButton, tip);
        minimizeButton.Visibility = state == MdiWindowState.Minimized ? Visibility.Collapsed : Visibility.Visible;

        if (state == MdiWindowState.Maximized)
        {
            CornerRadius = new CornerRadius(0);
            BorderThickness = new Thickness(0);
            Effect = null;
            titleBar.CornerRadius = new CornerRadius(0);
            body.CornerRadius = new CornerRadius(0);
        }
        else if (state == MdiWindowState.Minimized)
        {
            CornerRadius = new CornerRadius(6);
            BorderThickness = new Thickness(1);
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.15, Color = System.Windows.Media.Color.FromRgb(11, 18, 32) };
            titleBar.CornerRadius = new CornerRadius(5);
        }
        else
        {
            CornerRadius = new CornerRadius(8);
            BorderThickness = new Thickness(1);
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.20, Color = System.Windows.Media.Color.FromRgb(11, 18, 32) };
            titleBar.CornerRadius = new CornerRadius(7, 7, 0, 0);
            body.CornerRadius = new CornerRadius(0, 0, 7, 7);
        }
    }

    private void ApplyActiveVisual()
    {
        SetResourceReference(BorderBrushProperty, isActiveWindow ? "Accent" : "Divider");
        titleBar.SetResourceReference(BackgroundProperty, isActiveWindow ? "Accent" : "Alternate");
        var foreground = isActiveWindow ? "Text.OnAccent" : "Text";
        titleText.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        glyphText.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        foreach (var button in new[] { minimizeButton, maximizeButton, closeButton }) button.SetResourceReference(Control.ForegroundProperty, foreground);
        titleText.FontWeight = isActiveWindow ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private Button CreateButton(string glyph, string tip, bool isClose, Action click)
    {
        var button = new Button { Content = glyph, ToolTip = tip, Focusable = false };
        button.SetResourceReference(StyleProperty, "Button.Icon");
        button.MinWidth = 28;
        button.MinHeight = MdiLayoutEngine.TitleHeight;
        button.Padding = new Thickness(2);
        button.FontSize = 11;
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        if (isClose)
        {
            button.MouseEnter += (_, _) =>
            {
                button.SetResourceReference(Control.BackgroundProperty, "Error");
                button.SetResourceReference(Control.ForegroundProperty, "Text.OnAccent");
            };
            button.MouseLeave += (_, _) =>
            {
                button.Background = System.Windows.Media.Brushes.Transparent;
                button.SetResourceReference(Control.ForegroundProperty, isActiveWindow ? "Text.OnAccent" : "Text");
            };
        }
        return button;
    }

    private void AddResizeThumbs()
    {
        AddThumb(MdiEdge.Left, HorizontalAlignment.Left, VerticalAlignment.Stretch, Cursors.SizeWE, EdgeThickness, double.NaN);
        AddThumb(MdiEdge.Right, HorizontalAlignment.Right, VerticalAlignment.Stretch, Cursors.SizeWE, EdgeThickness, double.NaN);
        AddThumb(MdiEdge.Top, HorizontalAlignment.Stretch, VerticalAlignment.Top, Cursors.SizeNS, double.NaN, EdgeThickness);
        AddThumb(MdiEdge.Bottom, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Cursors.SizeNS, double.NaN, EdgeThickness);
        AddThumb(MdiEdge.Left | MdiEdge.Top, HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE, CornerSize, CornerSize);
        AddThumb(MdiEdge.Right | MdiEdge.Bottom, HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE, CornerSize, CornerSize);
        AddThumb(MdiEdge.Right | MdiEdge.Top, HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW, CornerSize, CornerSize);
        AddThumb(MdiEdge.Left | MdiEdge.Bottom, HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW, CornerSize, CornerSize);
    }

    private void AddThumb(MdiEdge edge, HorizontalAlignment horizontal, VerticalAlignment vertical, Cursor cursor, double width, double height)
    {
        var thumb = new Thumb
        {
            Background = System.Windows.Media.Brushes.Transparent, Opacity = 0, Focusable = false, Cursor = cursor,
            HorizontalAlignment = horizontal, VerticalAlignment = vertical, Width = width, Height = height,
        };
        Grid.SetRowSpan(thumb, 2);
        thumb.DragStarted += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        thumb.DragDelta += (_, e) => Dragged?.Invoke(this, new MdiDragEventArgs(e.HorizontalChange, e.VerticalChange, edge));
        root.Children.Add(thumb);
    }
}

/// <summary>
/// The classic MDI workspace: a canvas inside the main window that holds one <see cref="MdiChildWindow"/> per open, attached,
/// closable document. Move, resize, minimize (to a chip along the bottom edge), maximize inside the container, close, activate,
/// z-order, tile and cascade all use the container's own bounds (see <see cref="MdiLayoutEngine"/>), never the screen work area.
/// Decisions about closing (unsaved changes, pending posting) stay in the shell view model: the host only raises requests.
/// </summary>
public sealed class MdiWorkspaceHost : Canvas
{
    private readonly List<MdiChildWindow> children = [];
    private readonly HashSet<WorkspaceDocument> observed = [];
    private WorkspaceDocumentManager? documents;
    private ILocalizationService? localization;
    private FlowDirection contentFlow = FlowDirection.RightToLeft;
    private string contextCaption = string.Empty;
    private bool enabledMode;
    private int zCounter;

    public MdiWorkspaceHost()
    {
        ClipToBounds = true;
        // Canvas.Left/Top are absolute from the left edge in this container; each window's content gets the application flow.
        FlowDirection = FlowDirection.LeftToRight;
        Focusable = false;
        SizeChanged += (_, _) => Reflow();
    }

    /// <summary>Raised when the user clicks or drags a window: the shell activates that document.</summary>
    public event EventHandler<WorkspaceDocument>? ActivateRequested;

    /// <summary>Raised by the close button: the shell runs its normal close rules (dirty confirmation, pending posting).</summary>
    public event EventHandler<WorkspaceDocument>? CloseRequested;

    /// <summary>The container's own bounds; what every layout operation is computed from.</summary>
    public MdiRect Bounds => new(0, 0, ActualWidth, ActualHeight);

    public int WindowCount => children.Count;

    public IReadOnlyList<MdiChildWindow> Windows => children;

    /// <summary>The branch / fiscal year caption given to every window (and to windows opened later).</summary>
    public string ContextCaption
    {
        get => contextCaption;
        set
        {
            value ??= string.Empty;
            if (string.Equals(contextCaption, value, StringComparison.Ordinal)) return;
            contextCaption = value;
            foreach (var child in children) child.ContextCaption = value;
        }
    }

    public FlowDirection ContentFlow
    {
        get => contentFlow;
        set
        {
            if (contentFlow == value) return;
            contentFlow = value;
            foreach (var child in children) child.ApplyFlow(value);
            Reflow();
        }
    }

    public void Attach(WorkspaceDocumentManager manager, ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(localizationService);
        if (documents is not null) return;
        documents = manager;
        localization = localizationService;
        manager.Documents.CollectionChanged += DocumentsChanged;
        manager.ActiveChanged += (_, _) => SyncActive();
        manager.Reactivated += (_, _) => SyncActive();
        Sync();
    }

    /// <summary>
    /// Classic MDI renders its windows only while it is the presentation mode; otherwise the views are released so a document
    /// is never rendered twice (here and in the tab host).
    /// </summary>
    public void SetEnabledMode(bool value)
    {
        if (enabledMode == value) return;
        enabledMode = value;
        Sync();
    }

    public void TileHorizontally() => ArrangeWindows(MdiLayoutEngine.TileHorizontal);

    public void TileVertically() => ArrangeWindows(MdiLayoutEngine.TileVertical);

    public void Cascade() => ArrangeWindows((count, bounds) => MirrorIfRightToLeft(MdiLayoutEngine.Cascade(count, bounds), bounds));

    /// <summary>In a right-to-left application new and cascaded windows start at the right edge, the reading start.</summary>
    private IReadOnlyList<MdiRect> MirrorIfRightToLeft(IReadOnlyList<MdiRect> rects, MdiRect bounds) =>
        contentFlow == FlowDirection.RightToLeft ? rects.Select(rect => new MdiRect(bounds.X + bounds.Right - rect.Right, rect.Y, rect.Width, rect.Height)).ToArray() : rects;

    public void MinimizeActive()
    {
        var active = children.FirstOrDefault(child => child.Document.IsActive);
        if (active is not null) SetChildState(active, MdiWindowState.Minimized);
    }

    public void RestoreAll()
    {
        foreach (var child in children.Where(child => child.State != MdiWindowState.Normal).ToList()) SetChildState(child, MdiWindowState.Normal);
    }

    public void BringAllToFront()
    {
        foreach (var child in children.Where(child => !child.Document.IsActive)) Raise(child);
        var active = children.FirstOrDefault(child => child.Document.IsActive);
        if (active is not null) Raise(active);
    }

    private void ArrangeWindows(Func<int, MdiRect, IReadOnlyList<MdiRect>> layout)
    {
        var bounds = Bounds;
        if (!bounds.IsValid) return;
        var visible = children.Where(child => child.State != MdiWindowState.Minimized).ToList();
        var rects = layout(visible.Count, bounds);
        for (var index = 0; index < visible.Count && index < rects.Count; index++)
        {
            var child = visible[index];
            child.NormalBounds = rects[index];
            child.SetState(MdiWindowState.Normal);
            Remember(child);
        }

        ApplyAllGeometry();
    }

    private void DocumentsChanged(object? sender, NotifyCollectionChangedEventArgs args) => Sync();

    private void DocumentPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkspaceDocument.IsDetached)) Sync();
    }

    private void Sync()
    {
        var manager = documents;
        var loc = localization;
        if (manager is null || loc is null) return;
        List<WorkspaceDocument> wanted = enabledMode
            ? manager.Documents.Where(document => document.IsClosable && !document.IsDetached).ToList()
            : [];

        foreach (var child in children.Where(child => !wanted.Contains(child.Document)).ToList()) RemoveChild(child);
        foreach (var document in wanted.Where(document => children.All(child => !ReferenceEquals(child.Document, document))).ToList()) AddChild(document, loc);

        // Watch every document for detach/attach, so a detached document releases its internal window first.
        foreach (var document in observed.Where(document => !manager.Documents.Contains(document)).ToList())
        {
            document.PropertyChanged -= DocumentPropertyChanged;
            observed.Remove(document);
        }

        foreach (var document in manager.Documents)
            if (observed.Add(document)) document.PropertyChanged += DocumentPropertyChanged;

        SyncActive();
    }

    private void AddChild(WorkspaceDocument document, ILocalizationService loc)
    {
        var child = new MdiChildWindow(document, contentFlow, loc) { NeedsPlacement = true, ContextCaption = contextCaption };
        child.Activated += (_, _) =>
        {
            // Clicking a minimized window's chip restores it, even when its document is already the active one.
            if (child.State == MdiWindowState.Minimized) SetChildState(child, MdiWindowState.Normal);
            ActivateRequested?.Invoke(this, document);
        };
        child.CloseRequested += (_, _) => CloseRequested?.Invoke(this, document);
        child.MinimizeRequested += (_, _) => SetChildState(child, MdiWindowState.Minimized);
        child.MaximizeToggleRequested += (_, _) => SetChildState(child, child.State == MdiWindowState.Maximized ? MdiWindowState.Normal : MdiWindowState.Maximized);
        child.Dragged += OnChildDragged;

        var bounds = Bounds;
        if (document.MdiBounds is { IsValid: true } saved) child.NormalBounds = saved;
        else child.NormalBounds = InitialCenterBounds(document, bounds, children.Count);
        child.SetState(document.MdiState);
        children.Add(child);
        Children.Add(child);
        Raise(child);
        ApplyGeometry(child);
        child.NeedsPlacement = !bounds.IsValid;
    }

    /// <summary>
    /// Opens floating internal windows centered in the MDI workspace viewport (with a subtle offset for multiple windows).
    /// </summary>
    private static MdiRect InitialCenterBounds(WorkspaceDocument document, MdiRect bounds, int index)
    {
        if (!bounds.IsValid) return new MdiRect(0, 0, 780, 500);
        var size = AmeenWindowSizes.For(document) ?? (780, 500);
        var width = Math.Min(size.Width, Math.Max(400, bounds.Width - 40));
        var height = Math.Min(size.Height, Math.Max(300, bounds.Height - 40));
        var stagger = (index % 6) * 20;
        var x = Math.Max(0, (bounds.Width - width) / 2) + stagger;
        var y = Math.Max(0, (bounds.Height - height) / 2) + stagger;
        return MdiLayoutEngine.Clamp(new MdiRect(x, y, width, height), bounds);
    }

    private void RemoveChild(MdiChildWindow child)
    {
        Remember(child);
        children.Remove(child);
        Children.Remove(child);
        child.Release();
        ApplyAllGeometry();
    }

    private void SyncActive()
    {
        if (documents is null) return;
        foreach (var child in children)
        {
            var isActive = ReferenceEquals(child.Document, documents.Active);
            child.SetActive(isActive);
            if (!isActive) continue;
            if (child.State == MdiWindowState.Minimized) SetChildState(child, MdiWindowState.Normal);
            Raise(child);
        }
    }

    private void Raise(MdiChildWindow child) => SetZIndex(child, ++zCounter);

    private void SetChildState(MdiChildWindow child, MdiWindowState value)
    {
        child.SetState(value);
        Remember(child);
        ApplyAllGeometry();
    }

    private static void Remember(MdiChildWindow child)
    {
        child.Document.MdiBounds = child.NormalBounds;
        child.Document.MdiState = child.State;
    }

    private void Reflow()
    {
        var bounds = Bounds;
        if (!bounds.IsValid) return;
        var placed = 0;
        foreach (var child in children)
        {
            if (child.NeedsPlacement && child.Document.MdiBounds is not { IsValid: true })
            {
                child.NormalBounds = InitialCenterBounds(child.Document, bounds, placed);
            }

            child.NeedsPlacement = false;
            placed++;
        }

        ApplyAllGeometry();
    }

    private void ApplyAllGeometry()
    {
        foreach (var child in children) ApplyGeometry(child);
    }

    private void ApplyGeometry(MdiChildWindow child)
    {
        var bounds = Bounds;
        if (!bounds.IsValid)
        {
            child.Width = child.NormalBounds.Width;
            child.Height = child.NormalBounds.Height;
            SetLeft(child, child.NormalBounds.X);
            SetTop(child, child.NormalBounds.Y);
            return;
        }

        MdiRect rect;
        switch (child.State)
        {
            case MdiWindowState.Maximized:
                rect = bounds;
                break;
            case MdiWindowState.Minimized:
                var index = children.Where(other => other.State == MdiWindowState.Minimized).ToList().IndexOf(child);
                rect = MdiLayoutEngine.MinimizedSlot(index, bounds, contentFlow == FlowDirection.RightToLeft);
                break;
            default:
                rect = MdiLayoutEngine.Clamp(child.NormalBounds, bounds);
                child.NormalBounds = rect;
                break;
        }

        child.Width = rect.Width;
        child.Height = rect.Height;
        SetLeft(child, rect.X);
        SetTop(child, rect.Y);
    }

    private void OnChildDragged(object? sender, MdiDragEventArgs args)
    {
        if (sender is not MdiChildWindow child || child.State != MdiWindowState.Normal) return;
        var bounds = Bounds;
        if (!bounds.IsValid) return;
        var rect = child.NormalBounds;
        double x = rect.X, y = rect.Y, right = rect.Right, bottom = rect.Bottom;
        if (args.Edge == MdiEdge.None)
        {
            x += args.HorizontalChange;
            y += args.VerticalChange;
            right = x + rect.Width;
            bottom = y + rect.Height;
        }
        else
        {
            if (args.Edge.HasFlag(MdiEdge.Left)) x = Math.Min(x + args.HorizontalChange, right - MdiLayoutEngine.MinimumWidth);
            if (args.Edge.HasFlag(MdiEdge.Right)) right = Math.Max(right + args.HorizontalChange, x + MdiLayoutEngine.MinimumWidth);
            if (args.Edge.HasFlag(MdiEdge.Top)) y = Math.Min(y + args.VerticalChange, bottom - MdiLayoutEngine.MinimumHeight);
            if (args.Edge.HasFlag(MdiEdge.Bottom)) bottom = Math.Max(bottom + args.VerticalChange, y + MdiLayoutEngine.MinimumHeight);
        }

        child.NormalBounds = MdiLayoutEngine.Clamp(new MdiRect(x, y, right - x, bottom - y), bounds);
        Remember(child);
        ApplyGeometry(child);
    }
}
