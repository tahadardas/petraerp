using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Opt-in cell entry shortcuts providing Al-Ameen high-speed accounting keyboard navigation:
/// - Enter: commits cell and advances horizontally to the next editable column, wrapping to the next row at the end.
/// - F2: activates fast lookup or expands dropdown on cell editors.
/// - Shift+Enter: commits and moves to the previous editable cell (wrapping to the previous row).
/// - F4 (and Ctrl+D, the Excel "fill down" alias): copies the same column from the previous visible row (when CopyPath is
///   configured). The F4 contract is unchanged; the fill is recorded so Ctrl+Z (see <see cref="GridClipboard"/>) can undo it.
/// - Esc: cancels the current cell edit without losing committed record state.
/// </summary>
public static class GridEntry
{
    public static readonly RoutedUICommand CopyPreviousCell = new("Copy previous cell", nameof(CopyPreviousCell), typeof(GridEntry));
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(GridEntry), new PropertyMetadata(false, Attach));
    public static readonly DependencyProperty CopyPathProperty = DependencyProperty.RegisterAttached("CopyPath", typeof(string), typeof(GridEntry));
    public static readonly DependencyProperty EnterMovesNextProperty = DependencyProperty.RegisterAttached("EnterMovesNext", typeof(bool), typeof(GridEntry), new PropertyMetadata(true));
    public static readonly DependencyProperty SkipOnEnterProperty = DependencyProperty.RegisterAttached("SkipOnEnter", typeof(bool), typeof(GridEntry), new PropertyMetadata(false));

    public static bool GetEnabled(DependencyObject element) { ArgumentNullException.ThrowIfNull(element); return (bool)element.GetValue(EnabledProperty); }
    public static bool GetSkipOnEnter(DependencyObject element) { ArgumentNullException.ThrowIfNull(element); return (bool)element.GetValue(SkipOnEnterProperty); }
    public static void SetSkipOnEnter(DependencyObject element, bool value) { ArgumentNullException.ThrowIfNull(element); element.SetValue(SkipOnEnterProperty, value); }
    public static void SetEnabled(DependencyObject element, bool value) { ArgumentNullException.ThrowIfNull(element); element.SetValue(EnabledProperty, value); }
    public static string? GetCopyPath(DependencyObject element) { ArgumentNullException.ThrowIfNull(element); return (string?)element.GetValue(CopyPathProperty); }
    public static void SetCopyPath(DependencyObject element, string value) { ArgumentNullException.ThrowIfNull(element); element.SetValue(CopyPathProperty, value); }
    public static bool GetEnterMovesNext(DependencyObject element) { ArgumentNullException.ThrowIfNull(element); return (bool)element.GetValue(EnterMovesNextProperty); }
    public static void SetEnterMovesNext(DependencyObject element, bool value) { ArgumentNullException.ThrowIfNull(element); element.SetValue(EnterMovesNextProperty, value); }

    private static void Attach(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not DataGrid grid) return;
        grid.PreviewKeyDown -= OnKeyDown;
        foreach (var binding in grid.CommandBindings.Cast<CommandBinding>().Where(b => b.Command == CopyPreviousCell).ToArray()) grid.CommandBindings.Remove(binding);
        if (!(bool)args.NewValue) return;
        grid.PreviewKeyDown += OnKeyDown;
        grid.CommandBindings.Add(new(CopyPreviousCell, (_, e) => { CopyFromPrevious(grid); e.Handled = true; },
            (_, e) => { e.CanExecute = Resolve(grid) is not null; e.Handled = true; }));
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid || !GetEnabled(grid) || !grid.IsEnabled) return;

        // Ensure grid.CurrentCell reflects the focused cell
        if (e.OriginalSource is DependencyObject source && FindParent<DataGridCell>(source) is { } cell)
        {
            if (grid.CurrentCell.Item != cell.DataContext || grid.CurrentCell.Column != cell.Column)
            {
                grid.CurrentCell = new(cell.DataContext, cell.Column);
            }
        }

        // F2, F3, Alt+Down and Space: open the lookup list (ComboBox and LookupPicker). Space opens only when the picker is
        // empty or fully selected, and never on an editable ComboBox, so typing text and numbers keeps working.
        var openKey = (e.Key is Key.F2 or Key.F3 && Keyboard.Modifiers == ModifierKeys.None)
            || (e.Key == Key.Multiply && Keyboard.Modifiers == ModifierKeys.None)
            || (e.Key == Key.Down || e.Key == Key.System && e.SystemKey == Key.Down) && Keyboard.Modifiers == ModifierKeys.Alt;
        var spaceKey = e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None;
        if (openKey || spaceKey)
        {
            if (e.OriginalSource is DependencyObject orig)
            {
                var combo = orig as ComboBox ?? FindParent<ComboBox>(orig);
                if (combo is not null)
                {
                    if (spaceKey && combo.IsEditable) return;
                    combo.IsDropDownOpen = !combo.IsDropDownOpen;
                    e.Handled = true;
                    return;
                }

                var picker = orig as LookupPicker ?? FindParent<LookupPicker>(orig);
                if (picker is null && FindParent<DataGridCell>(orig) is { } parentCell)
                {
                    picker = FindChild<LookupPicker>(parentCell);
                }

                if (picker is not null && (!spaceKey || picker.CanOpenWithSpace))
                {
                    picker.ToggleLookup();
                    e.Handled = true;
                    return;
                }
            }
        }

        // F4: Copy from previous row (if configured)
        if ((e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.None) || (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control))
        {
            if (Resolve(grid) is not null)
            {
                e.Handled = true;
                CopyFromPrevious(grid);
                return;
            }
        }

        // Enter and Tab: High-speed horizontal navigation across columns and rows
        if ((e.Key == Key.Enter || e.Key == Key.Tab) && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift && GetEnterMovesNext(grid))
        {
            if (!grid.IsReadOnly && grid.CurrentCell.Column is not null)
            {
                var focused = Keyboard.FocusedElement as DependencyObject ?? e.OriginalSource as DependencyObject;
                var cellTextBox = focused as TextBox ?? FindParent<TextBox>(focused);
                if (cellTextBox is null && FindParent<DataGridCell>(focused) is { } editingCell)
                {
                    cellTextBox = FindChild<TextBox>(editingCell);
                }

                if (cellTextBox is not null && !cellTextBox.IsReadOnly && PetraMathEvaluator.TryEvaluate(cellTextBox.Text, out var calculated))
                {
                    cellTextBox.Text = PetraNumberFormat.ToWire(calculated);
                    var binding = BindingOperations.GetBindingExpression(cellTextBox, TextBox.TextProperty);
                    binding?.UpdateSource();
                }

                var picker = focused as LookupPicker ?? FindParent<LookupPicker>(focused);
                if (picker is null && FindParent<DataGridCell>(focused) is { } targetCell)
                {
                    picker = FindChild<LookupPicker>(targetCell);
                }

                if (picker is not null && e.Key == Key.Enter)
                {
                    e.Handled = true;
                    picker.CommitHighlighted();
                    AdvanceToNextCell(grid, Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
                    return;
                }

                var combo = focused as ComboBox ?? FindParent<ComboBox>(focused);
                if (combo is not null && combo.IsDropDownOpen && e.Key == Key.Enter)
                {
                    return;
                }

                e.Handled = true;
                AdvanceToNextCell(grid, Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
                return;
            }
        }

        // Arrow keys: Al-Ameen style movement between cells while entering data. Up/Down always change row; Left/Right change
        // column only when the editor has nothing left to move through (empty or fully selected text, or a non-text editor),
        // so editing inside a value with the caret keeps working. Open lookup lists keep the arrows for their own list.
        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right && Keyboard.Modifiers == ModifierKeys.None && !grid.IsReadOnly && grid.CurrentCell.Column is not null)
        {
            var focusedElement = Keyboard.FocusedElement as DependencyObject ?? e.OriginalSource as DependencyObject;
            var openPicker = focusedElement as LookupPicker ?? FindParent<LookupPicker>(focusedElement);
            var openCombo = focusedElement as ComboBox ?? FindParent<ComboBox>(focusedElement);
            var popupOpen = openPicker is { IsPopupOpen: true } || openCombo is { IsDropDownOpen: true };
            if (!popupOpen)
            {
                var vertical = e.Key is Key.Up or Key.Down;
                var text = focusedElement as TextBox ?? FindParent<TextBox>(focusedElement);
                var canMove = vertical || text is null || text.Text.Length == 0 || text.SelectionLength == text.Text.Length;
                if (canMove)
                {
                    var visualRight = grid.FlowDirection == FlowDirection.RightToLeft ? -1 : 1;
                    var columnStep = e.Key == Key.Right ? visualRight : e.Key == Key.Left ? -visualRight : 0;
                    var rowStep = e.Key == Key.Down ? 1 : e.Key == Key.Up ? -1 : 0;
                    if (MoveBy(grid, rowStep, columnStep)) e.Handled = true;
                }
            }
        }

        // Escape: Cancel cell edit cleanly
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            grid.CancelEdit(DataGridEditingUnit.Cell);
            e.Handled = true;
        }
    }

    /// <summary>Commits the current cell and moves the entry cell by whole rows and/or editable columns; false when there is nowhere to go.</summary>
    private static bool MoveBy(DataGrid grid, int rowStep, int columnStep)
    {
        var column = grid.CurrentCell.Column;
        var rowItem = grid.CurrentCell.Item;
        if (column is null || rowItem is null || rowItem == CollectionView.NewItemPlaceholder) return false;
        var columns = grid.Columns.Where(c => c.Visibility == Visibility.Visible && !c.IsReadOnly && !GetSkipOnEnter(c)).OrderBy(c => c.DisplayIndex).ToList();
        var columnIndex = columns.IndexOf(column);
        if (columnIndex < 0) return false;
        var targetColumnIndex = columnIndex + columnStep;
        var targetRow = grid.Items.IndexOf(rowItem) + rowStep;
        if (targetColumnIndex < 0 || targetColumnIndex >= columns.Count || targetRow < 0 || targetRow >= grid.Items.Count) return false;
        var targetItem = grid.Items[targetRow];
        if (targetItem == CollectionView.NewItemPlaceholder) return false;
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true)) return true; // invalid value: stay put, keep the validation visible
        var target = new DataGridCellInfo(targetItem, columns[targetColumnIndex]);
        NavigateToCell(grid, target, targetItem, columns[targetColumnIndex]);
        return true;
    }

    private static void NavigateToCell(DataGrid grid, DataGridCellInfo target, object? focusItem, DataGridColumn? focusColumn)
    {
        grid.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            try
            {
                grid.CurrentCell = target;
                grid.SelectedCells.Clear();
                grid.SelectedCells.Add(target);
                grid.BeginEdit();
                if (focusItem is not null && focusColumn is not null)
                {
                    FocusInnerInput(grid, focusItem, focusColumn);
                }
            }
            catch (InvalidOperationException)
            {
                // Defensive guard: ignore re-entrant layout/virtualization collisions
            }
        });
    }

    private static void AdvanceToNextCell(DataGrid grid, int direction = 1)
    {
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true)) return;

        var currentColumn = grid.CurrentCell.Column;
        var currentRowItem = grid.CurrentCell.Item;
        if (currentColumn is null || currentRowItem is null) return;

        var editableColumns = grid.Columns
            .Where(c => c.Visibility == Visibility.Visible && !c.IsReadOnly && !GetSkipOnEnter(c))
            .OrderBy(c => c.DisplayIndex)
            .ToList();

        if (editableColumns.Count == 0) return;

        var currentIndex = editableColumns.IndexOf(currentColumn);
        if (direction < 0)
        {
            MoveBackward(grid, editableColumns, currentIndex, currentRowItem);
            return;
        }

        if (currentIndex >= 0 && currentIndex < editableColumns.Count - 1)
        {
            // Move to next editable column in the same row
            var nextColumn = editableColumns[currentIndex + 1];
            var nextTarget = new DataGridCellInfo(currentRowItem, nextColumn);
            NavigateToCell(grid, nextTarget, currentRowItem, nextColumn);
        }
        else
        {
            // At the end of the row, move to the first column of the next row
            var rowIndex = grid.Items.IndexOf(currentRowItem);
            if (rowIndex >= 0 && rowIndex < grid.Items.Count - 1)
            {
                var nextRowItem = grid.Items[rowIndex + 1];
                if (nextRowItem != CollectionView.NewItemPlaceholder)
                {
                    var firstColumn = editableColumns[0];
                    var firstTarget = new DataGridCellInfo(nextRowItem, firstColumn);
                    NavigateToCell(grid, firstTarget, nextRowItem, firstColumn);
                }
            }
            else if (rowIndex >= grid.Items.Count - 1 && grid is PetraAccountingDataGrid accountingGrid)
            {
                accountingGrid.RequestAutoGrow();
                if (rowIndex < grid.Items.Count - 1)
                {
                    var nextRowItem = grid.Items[rowIndex + 1];
                    if (nextRowItem != CollectionView.NewItemPlaceholder)
                    {
                        var firstColumn = editableColumns[0];
                        var firstTarget = new DataGridCellInfo(nextRowItem, firstColumn);
                        NavigateToCell(grid, firstTarget, nextRowItem, firstColumn);
                    }
                }
            }
        }
    }

    private static void MoveBackward(DataGrid grid, List<DataGridColumn> editableColumns, int currentIndex, object currentRowItem)
    {
        DataGridCellInfo target;
        object targetItem;
        DataGridColumn targetColumn;
        if (currentIndex > 0)
        {
            targetColumn = editableColumns[currentIndex - 1];
            targetItem = currentRowItem;
            target = new DataGridCellInfo(currentRowItem, targetColumn);
        }
        else
        {
            var rowIndex = grid.Items.IndexOf(currentRowItem);
            if (rowIndex <= 0) return;
            targetItem = grid.Items[rowIndex - 1];
            targetColumn = editableColumns[^1];
            target = new DataGridCellInfo(targetItem, targetColumn);
        }

        NavigateToCell(grid, target, targetItem, targetColumn);
    }

    /// <summary>Copies the same column from the previous visible row, retaining validation and normal draft setters.</summary>
    public static bool CopyFromPrevious(DataGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (Resolve(grid) is not { } initial) return false;
        // Do not replace an invalid editor value or leave an uncommitted editor showing stale text.
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
        if (Resolve(grid) is not { } target || !ReferenceEquals(initial.Row, target.Row)) return false;
        var old = target.Property.GetValue(target.Row);
        target.Property.SetValue(target.Row, target.Property.GetValue(target.Previous));
        GridClipboard.Record(grid, [new GridClipboard.CellChange(target.Row, target.Property, old)]);
        grid.BeginEdit();
        return true;
    }

    private sealed record CopyTarget(object Row, object Previous, PropertyDescriptor Property);

    private static CopyTarget? Resolve(DataGrid grid)
    {
        if (!GetEnabled(grid) || !grid.IsEnabled || grid.IsReadOnly || grid.CurrentCell.Column is not { IsReadOnly: false } column) return null;
        var path = GetCopyPath(column);
        var row = grid.CurrentCell.Item;
        var index = grid.Items.IndexOf(row);
        if (string.IsNullOrEmpty(path) || index < 1 || row == CollectionView.NewItemPlaceholder) return null;
        var previous = grid.Items[index - 1];
        if (previous is null || previous.GetType() != row.GetType()) return null;
        var property = TypeDescriptor.GetProperties(row)[path];
        return property is { IsReadOnly: false, PropertyType: var type } && type == typeof(string) ? new(row, previous, property) : null;
    }

    internal static T? FindParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    internal static T? FindChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) return null;
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var nested = FindChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
    private static void FocusInnerInput(DataGrid grid, object rowItem, DataGridColumn column)
    {
        grid.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            var cell = FindCell(grid, rowItem, column);
            if (cell is not null)
            {
                var innerInput = FindChild<TextBox>(cell) ?? FindChild<LookupPicker>(cell) as FrameworkElement ?? cell;
                innerInput.Focus();
                if (innerInput is TextBox tb) tb.SelectAll();
                else if (innerInput is LookupPicker lp) lp.FocusSearch();
            }
        });
    }

    private static DataGridCell? FindCell(DataGrid grid, object item, DataGridColumn column)
    {
        if (grid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row) return null;
        var presenter = FindChild<System.Windows.Controls.Primitives.DataGridCellsPresenter>(row);
        if (presenter is null) return null;
        var cell = presenter.ItemContainerGenerator.ContainerFromIndex(column.DisplayIndex) as DataGridCell;
        return cell ?? FindChild<DataGridCell>(row);
    }
}
