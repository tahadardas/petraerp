using System.Windows;

namespace PetraERP.Desktop.Shell;

/// <summary>Standard widths of a modal dialog (design system: dialog-s / dialog-m / dialog-l).</summary>
public enum DialogSize
{
    /// <summary>480: a name prompt, a single choice.</summary>
    Small = 480,
    /// <summary>760: report options, wizards.</summary>
    Medium = 760,
    /// <summary>980: program options.</summary>
    Large = 980,
}

/// <summary>
/// The one way secondary windows are opened (design system "Windows and opening"). Documents open through the navigator;
/// modal dialogs through <see cref="ShowDialog"/>; non-modal tool windows (report viewer, print preview, template designer)
/// through <see cref="ShowTool"/>; confirmations through IUserConfirmation / DialogHost, or <see cref="PetraMessage"/> where
/// no shell is present. Every window gets the same style, owner, placement, flow direction and language.
/// </summary>
public static class PetraWindows
{
    /// <summary>Applies the dialog standard: Window.Dialog style, owner, CenterOwner, no taskbar entry, owner's direction and language.</summary>
    public static T PrepareDialog<T>(T window, Window? owner, DialogSize? size = null) where T : Window
    {
        ArgumentNullException.ThrowIfNull(window);
        Apply(window, owner, "Window.Dialog");
        if (size is { } width)
        {
            window.Width = (double)width;
            window.MinWidth = Math.Round((double)width * 0.8);
        }
        return window;
    }

    /// <summary>Applies the tool-window standard: Window.Tool style (taskbar entry), owner, CenterOwner, owner's direction and language.</summary>
    public static T PrepareTool<T>(T window, Window? owner) where T : Window
    {
        ArgumentNullException.ThrowIfNull(window);
        Apply(window, owner, "Window.Tool");
        return window;
    }

    /// <summary>Opens a modal dialog prepared to the standard.</summary>
    public static bool? ShowDialog(Window window, Window? owner, DialogSize? size = null) => PrepareDialog(window, owner, size).ShowDialog();

    /// <summary>Opens a non-modal tool window prepared to the standard and brings it to the front.</summary>
    public static void ShowTool(Window window, Window? owner)
    {
        PrepareTool(window, owner).Show();
        _ = window.Activate();
    }

    /// <summary>The window that should own a new dialog: the active window of the application, else its main window.</summary>
    public static Window? ActiveOwner()
    {
        var app = Application.Current;
        if (app is null) return null;
        foreach (Window candidate in app.Windows) if (candidate.IsActive) return candidate;
        return app.MainWindow;
    }

    private static void Apply(Window window, Window? owner, string styleKey)
    {
        owner ??= ActiveOwner();
        if (owner is not null && !ReferenceEquals(owner, window) && window.Owner is null) window.Owner = owner;
        // XAML windows name their style (Window.Dialog / Window.Tool); a window built in code gets it here.
        if (window.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue && window.TryFindResource(styleKey) is Style style) window.Style = style;
        window.WindowStartupLocation = window.Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        // A window without its own binding takes the direction and language of the window that opened it.
        if (window.Owner is { } parent)
        {
            if (window.ReadLocalValue(FrameworkElement.FlowDirectionProperty) == DependencyProperty.UnsetValue) window.FlowDirection = parent.FlowDirection;
            if (window.ReadLocalValue(FrameworkElement.LanguageProperty) == DependencyProperty.UnsetValue) window.Language = parent.Language;
        }
    }
}
