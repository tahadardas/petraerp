using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// The one command bar for daily documents. Bind <see cref="Screen"/> to the page view model
/// (<c>Screen="{Binding}"</c>); it shows only the commands that apply to the current lifecycle state
/// (hidden, never disabled), puts Reverse / Delete / screen-specific actions under "More", and shows the
/// document status as a single badge. Works in both text directions because it only uses a horizontal StackPanel.
/// </summary>
public sealed class DocumentCommandBar : ContentControl
{
    public static readonly DependencyProperty ScreenProperty = DependencyProperty.Register(
        nameof(Screen), typeof(IDocumentScreen), typeof(DocumentCommandBar), new PropertyMetadata(null, (d, e) => ((DocumentCommandBar)d).Attach((IDocumentScreen?)e.OldValue, (IDocumentScreen?)e.NewValue)));

    private readonly StackPanel panel = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<DocumentCommandId, Button> buttons = [];
    private readonly StackPanel extraPanel = new() { Orientation = Orientation.Horizontal };
    private readonly Button moreButton = new();
    private readonly ContextMenu moreMenu = new();
    private IDocumentScreen? screen;
    private DocumentExtraCommand[] shownExtras = [];

    public DocumentCommandBar()
    {
        Content = panel;
        Focusable = false;
        IsTabStop = false;
        foreach (var id in Enum.GetValues<DocumentCommandId>().Where(DocumentCommandLayout.IsPrimary))
        {
            var button = new Button { Margin = new Thickness(2, 0, 2, 0), Visibility = Visibility.Collapsed };
            button.SetResourceReference(FrameworkElement.StyleProperty, id == DocumentCommandId.Save ? "Am.PrimaryActionButton" : "Am.ActionButton");
            buttons[id] = button;
            panel.Children.Add(button);
        }
        panel.Children.Insert(0, extraPanel);
        moreButton.ContextMenu = moreMenu;
        MenuButton.SetOpensMenu(moreButton, true);
        moreButton.SetResourceReference(FrameworkElement.StyleProperty, "Am.ToolDropdown");
        moreButton.Visibility = Visibility.Collapsed;
        panel.Children.Add(moreButton);
    }

    public IDocumentScreen? Screen { get => (IDocumentScreen?)GetValue(ScreenProperty); set => SetValue(ScreenProperty, value); }

    private void Attach(IDocumentScreen? oldScreen, IDocumentScreen? newScreen)
    {
        if (oldScreen is not null) oldScreen.PropertyChanged -= OnScreenChanged;
        screen = newScreen;
        if (newScreen is not null) newScreen.PropertyChanged += OnScreenChanged;
        Refresh();
    }

    private void OnScreenChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) Refresh(); else Dispatcher.BeginInvoke(Refresh);
    }

    /// <summary>Re-evaluates visibility and captions. Cheap: no controls are created or removed.</summary>
    public void Refresh()
    {
        if (screen is null)
        {
            foreach (var b in buttons.Values) { b.Command = null; b.Visibility = Visibility.Collapsed; }
            moreMenu.Items.Clear();
            extraPanel.Children.Clear();
            shownExtras = [];
            moreButton.Visibility = Visibility.Collapsed;
            return;
        }

        var visible = DocumentCommandLayout.Visible(screen).ToHashSet();
        foreach (var (id, button) in buttons)
        {
            button.Command = DocumentCommandLayout.CommandOf(screen, id);
            button.Content = screen.Localization[DocumentCommandLayout.CaptionKey(id)];
            button.Visibility = visible.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
        }

        moreMenu.Items.Clear();
        foreach (var id in Enum.GetValues<DocumentCommandId>().Where(i => !DocumentCommandLayout.IsPrimary(i) && visible.Contains(i)))
            moreMenu.Items.Add(new MenuItem { Header = screen.Localization[DocumentCommandLayout.CaptionKey(id)], Command = DocumentCommandLayout.CommandOf(screen, id) });
        var applicable = screen.MoreCommands.Where(e => (!screen.IsEditing || e.AllowWhileEditing) && (e.IsApplicable?.Invoke() ?? true)).ToArray();
        foreach (var extra in applicable.Where(e => !e.IsPrimary))
            moreMenu.Items.Add(new MenuItem { Header = extra.Caption, Command = extra.Command });

        // Primary extras (e.g. "New receipt" / "New payment") sit before the standard buttons; rebuilt only when they change.
        var primary = applicable.Where(e => e.IsPrimary).ToArray();
        if (!primary.SequenceEqual(shownExtras, ExtraComparer.Instance))
        {
            shownExtras = primary;
            extraPanel.Children.Clear();
            foreach (var extra in primary)
            {
                var button = new Button { Content = extra.Caption, Command = extra.Command, Margin = new Thickness(2, 0, 2, 0) };
                button.SetResourceReference(FrameworkElement.StyleProperty, "Am.ActionButton");
                extraPanel.Children.Add(button);
            }
        }
        moreButton.Content = screen.Localization["D_More"] + " ▾";
        moreButton.Visibility = moreMenu.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed class ExtraComparer : IEqualityComparer<DocumentExtraCommand>
    {
        public static readonly ExtraComparer Instance = new();
        public bool Equals(DocumentExtraCommand? x, DocumentExtraCommand? y) => x is not null && y is not null && x.Caption == y.Caption && ReferenceEquals(x.Command, y.Command);
        public int GetHashCode(DocumentExtraCommand obj) => obj.Caption.GetHashCode(StringComparison.Ordinal);
    }
}
