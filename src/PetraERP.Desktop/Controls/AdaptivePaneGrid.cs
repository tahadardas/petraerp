using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace PetraERP.Desktop.Controls;

/// <summary>Retains the same live panes and bindings while switching split columns to a compact pane selector.</summary>
public sealed class AdaptivePaneGrid : Grid
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.RegisterAttached("Label", typeof(string), typeof(AdaptivePaneGrid), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ActivateWhenProperty = DependencyProperty.RegisterAttached("ActivateWhen", typeof(bool), typeof(AdaptivePaneGrid), new PropertyMetadata(false, ActivateChanged));
    private readonly List<FrameworkElement> panes = [];
    private readonly List<ToggleButtonEntry> selectors = [];
    private (GridLength Width, double Minimum)[] columns = [];
    private WrapPanel? header;
    public bool IsCompact { get; private set; }
    public int SelectedIndex { get; private set; }

    public static string GetLabel(DependencyObject element) => (string)element.GetValue(LabelProperty);
    public static void SetLabel(DependencyObject element, string value) => element.SetValue(LabelProperty, value);
    public static bool GetActivateWhen(DependencyObject element) => (bool)element.GetValue(ActivateWhenProperty);
    public static void SetActivateWhen(DependencyObject element, bool value) => element.SetValue(ActivateWhenProperty, value);

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        columns = ColumnDefinitions.Select(column => (column.Width, column.MinWidth)).ToArray();
        panes.AddRange(Children.OfType<FrameworkElement>().Where(child => child.ReadLocalValue(LabelProperty) != DependencyProperty.UnsetValue));
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        foreach (UIElement child in Children) SetRow(child, 1);
        header = new WrapPanel { Margin = new(0, 0, 0, 6), Visibility = Visibility.Collapsed };
        SetColumnSpan(header, Math.Max(1, columns.Length)); Children.Add(header);
        for (var index = 0; index < panes.Count; index++)
        {
            var paneIndex = index;
            var button = new System.Windows.Controls.Primitives.ToggleButton { Margin = new(2), Padding = new(10, 3, 10, 3), ToolTip = $"Alt+{index + 1}" };
            button.SetBinding(ContentControl.ContentProperty, new Binding { Source = panes[index], Path = new PropertyPath(LabelProperty) });
            button.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding { Source = panes[index], Path = new PropertyPath(LabelProperty) });
            button.Click += (_, _) => SelectPane(paneIndex, true);
            selectors.Add(new(button, GetColumn(panes[index]))); header.Children.Add(button);
            if (GetActivateWhen(panes[index])) SelectedIndex = index;
        }
        ApplyLayout();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var preferred = IsCompact ? columns : ColumnDefinitions.Select(column => (Width: column.Width, Minimum: column.MinWidth)).ToArray();
        var compact = double.IsFinite(constraint.Width) && constraint.Width < preferred.Sum(column => column.Width.IsStar ? column.Minimum : Math.Max(column.Width.Value, column.Minimum));
        if (header is not null && compact != IsCompact)
        {
            if (compact) columns = preferred;
            IsCompact = compact; ApplyLayout(true);
        }
        return base.MeasureOverride(constraint);
    }

    public bool SelectPane(int index, bool focus = false)
    {
        if (index < 0 || index >= panes.Count) return false;
        SelectedIndex = index; ApplyLayout();
        if (focus) { UpdateLayout(); panes[index].BringIntoView(); panes[index].MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); }
        return true;
    }

    public void Reveal(DependencyObject target)
    {
        for (var index = 0; index < panes.Count; index++)
            if (ReferenceEquals(panes[index], target) || panes[index].IsAncestorOf(target)) { SelectPane(index); break; }
    }

    private void ApplyLayout(bool changeColumns = false)
    {
        if (header is null) return;
        header.Visibility = IsCompact ? Visibility.Visible : Visibility.Collapsed;
        for (var index = 0; changeColumns && index < columns.Length; index++)
        {
            ColumnDefinitions[index].MinWidth = IsCompact ? 0 : columns[index].Minimum;
            ColumnDefinitions[index].Width = IsCompact ? (index == 0 ? new(1, GridUnitType.Star) : new(0)) : columns[index].Width;
        }
        foreach (var splitter in Children.OfType<GridSplitter>()) splitter.Visibility = IsCompact ? Visibility.Collapsed : Visibility.Visible;
        for (var index = 0; index < panes.Count; index++)
        {
            SetColumn(panes[index], IsCompact ? 0 : selectors[index].Column);
            panes[index].Visibility = !IsCompact || SelectedIndex == index ? Visibility.Visible : Visibility.Collapsed;
            selectors[index].Button.IsChecked = SelectedIndex == index;
        }
    }

    private static void ActivateChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true) return;
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is AdaptivePaneGrid host) { host.Reveal(element); break; }
    }

    private sealed record ToggleButtonEntry(System.Windows.Controls.Primitives.ToggleButton Button, int Column);
}
