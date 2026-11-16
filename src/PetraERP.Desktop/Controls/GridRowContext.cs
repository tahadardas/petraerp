using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PetraERP.Desktop.Controls;

/// <summary>Context actions use the row and cell under the pointer, never an earlier selection.</summary>
public static class GridRowContext
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(GridRowContext), new PropertyMetadata(false, Attach));
    public static bool GetEnabled(DependencyObject element) { ArgumentNullException.ThrowIfNull(element); return (bool)element.GetValue(EnabledProperty); }
    public static void SetEnabled(DependencyObject element, bool value) { ArgumentNullException.ThrowIfNull(element); element.SetValue(EnabledProperty, value); }

    private static void Attach(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not DataGrid grid) return;
        grid.PreviewMouseRightButtonDown -= OnRightButton;
        grid.ContextMenuOpening -= OnMenuOpening;
        if (!(bool)args.NewValue) return;
        grid.PreviewMouseRightButtonDown += OnRightButton;
        grid.ContextMenuOpening += OnMenuOpening;
    }

    private static void OnRightButton(object sender, MouseButtonEventArgs e)
    {
        if (!SelectTarget((DataGrid)sender, e.OriginalSource as DependencyObject)) e.Handled = true;
    }

    /// <summary>False means the current edit could not be committed and the menu must not open.</summary>
    public static bool SelectTarget(DataGrid grid, DependencyObject? source)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
        var row = GridEntry.FindParent<DataGridRow>(source);
        if (row is null || !ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(row), grid))
        {
            grid.UnselectAll(); grid.CurrentCell = default;
            return true;
        }
        grid.SelectedItem = row.Item;
        grid.CurrentCell = GridEntry.FindParent<DataGridCell>(source) is { } cell ? new(row.Item, cell.Column) : default;
        row.Focus();
        return true;
    }

    private static void OnMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var grid = (DataGrid)sender;
        if (grid.ContextMenu is not { } menu) return;
        // ContextMenu is a separate visual tree. Keep bindings current after scope/language/VM changes.
        menu.SetBinding(FrameworkElement.DataContextProperty, new Binding("PlacementTarget.DataContext") { Source = menu });
        menu.SetBinding(FrameworkElement.FlowDirectionProperty, new Binding("PlacementTarget.FlowDirection") { Source = menu });
    }
}
