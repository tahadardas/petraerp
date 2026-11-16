using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PetraERP.Desktop.Controls;

/// <summary>Presentation row numbers follow the current view, including sorting, deletion and recycled rows.</summary>
public static class GridRowNumbers
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(GridRowNumbers), new PropertyMetadata(false, Attach));
    private static readonly DependencyProperty ChangeHandlerProperty = DependencyProperty.RegisterAttached("ChangeHandler", typeof(NotifyCollectionChangedEventHandler), typeof(GridRowNumbers));
    private static readonly DependencyProperty PendingProperty = DependencyProperty.RegisterAttached("Pending", typeof(bool), typeof(GridRowNumbers));
    public static bool GetEnabled(DependencyObject element) { ArgumentNullException.ThrowIfNull(element); return (bool)element.GetValue(EnabledProperty); }
    public static void SetEnabled(DependencyObject element, bool value) { ArgumentNullException.ThrowIfNull(element); element.SetValue(EnabledProperty, value); }

    private static void Attach(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not DataGrid grid) return;
        grid.LoadingRow -= OnLoadingRow;
        if (grid.GetValue(ChangeHandlerProperty) is NotifyCollectionChangedEventHandler previous) ((INotifyCollectionChanged)grid.Items).CollectionChanged -= previous;
        if (!(bool)args.NewValue) return;
        // Local values keep the opt-in visible when the implicit dense style is applied after XAML initialization.
        grid.SetValue(DataGrid.HeadersVisibilityProperty, grid.HeadersVisibility | DataGridHeadersVisibility.Row);
        grid.SetValue(DataGrid.RowHeaderWidthProperty, 42d);
        grid.LoadingRow += OnLoadingRow;
        // Renumber realized rows only when the view changes (insert, delete, move, sort, filter, reset) — once per
        // dispatcher pass — instead of walking the visual tree on every layout pass. Recycled rows renumber in LoadingRow.
        NotifyCollectionChangedEventHandler handler = (_, _) => Schedule(grid);
        grid.SetValue(ChangeHandlerProperty, handler); ((INotifyCollectionChanged)grid.Items).CollectionChanged += handler;
        Schedule(grid);
    }

    private static void OnLoadingRow(object? sender, DataGridRowEventArgs args) => Number(args.Row);

    private static void Schedule(DataGrid grid)
    {
        if ((bool)grid.GetValue(PendingProperty)) return;
        grid.SetValue(PendingProperty, true);
        grid.Dispatcher.BeginInvoke(() => { grid.SetValue(PendingProperty, false); Refresh(grid); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void Refresh(DependencyObject root)
    {
        if (root is DataGridRow row) { Number(row); return; }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) Refresh(VisualTreeHelper.GetChild(root, index));
    }

    private static void Number(DataGridRow row)
    {
        var number = (row.GetIndex() + 1).ToString(CultureInfo.InvariantCulture);
        if (!Equals(row.Header, number)) row.Header = number;
    }
}
