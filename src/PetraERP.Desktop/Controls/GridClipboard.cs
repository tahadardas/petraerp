using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Excel-style clipboard for accounting grids.
/// <list type="bullet">
/// <item>Ctrl+C copies the selected cells as tab-separated text. Amount columns are copied as invariant decimal strings
/// (the wire format, never formatted or rounded), so pasting into Excel or back into PetraERP is exact.</item>
/// <item>Ctrl+V pastes tab-separated text into the editable text cells from the current cell onward. Each value goes
/// through the row's normal property setter; a value the row reports as invalid (INotifyDataErrorInfo/IDataErrorInfo)
/// is reverted and counted as rejected. Paste never adds or removes rows, and never touches read-only or posted rows.</item>
/// <item>Ctrl+Z undoes the last paste or fill in this grid (local draft input only; it never calls the server).</item>
/// </list>
/// While a cell editor has focus, the text box's own copy/paste/undo apply instead.
/// </summary>
public static class GridClipboard
{
    public const int MaximumUndo = 20;
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(GridClipboard), new PropertyMetadata(false, Attach));
    /// <summary>Marks an amount/quantity column explicitly (otherwise detected from the Text.Numeric cell style).</summary>
    public static readonly DependencyProperty NumericProperty = DependencyProperty.RegisterAttached("Numeric", typeof(bool?), typeof(GridClipboard));
    private static readonly DependencyProperty UndoProperty = DependencyProperty.RegisterAttached("Undo", typeof(UndoStack), typeof(GridClipboard));

    /// <summary>Reports a paste result (applied, rejected) to the shell's notifications; set by the composition root.</summary>
    public static Action<int, int>? PasteCompleted { get; set; }

    public static bool? GetNumeric(DependencyObject element) { ArgumentNullException.ThrowIfNull(element); return (bool?)element.GetValue(NumericProperty); }
    public static void SetNumeric(DependencyObject element, bool? value) { ArgumentNullException.ThrowIfNull(element); element.SetValue(NumericProperty, value); }

    public static bool GetEnabled(DependencyObject element) { ArgumentNullException.ThrowIfNull(element); return (bool)element.GetValue(EnabledProperty); }
    public static void SetEnabled(DependencyObject element, bool value) { ArgumentNullException.ThrowIfNull(element); element.SetValue(EnabledProperty, value); }

    /// <summary>One reversible cell change.</summary>
    public sealed record CellChange(object Row, PropertyDescriptor Property, object? OldValue);

    private sealed class UndoStack
    {
        public IEnumerable? Source { get; set; }
        public Stack<IReadOnlyList<CellChange>> Batches { get; } = new();
    }

    private static void Attach(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not DataGrid grid) return;
        grid.PreviewKeyDown -= OnKeyDown;
        if ((bool)args.NewValue) grid.PreviewKeyDown += OnKeyDown;
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        var grid = (DataGrid)sender;
        if (Keyboard.Modifiers != ModifierKeys.Control || !grid.IsEnabled) return;
        // A focused cell editor keeps the text box's own clipboard and undo.
        if (e.OriginalSource is TextBox { IsReadOnly: false }) return;
        switch (e.Key)
        {
            case Key.C:
                e.Handled = Copy(grid);
                break;
            case Key.V when !grid.IsReadOnly:
                e.Handled = PasteFromClipboard(grid);
                break;
            case Key.Z when !grid.IsReadOnly:
                e.Handled = Undo(grid);
                break;
        }
    }

    /// <summary>Records a batch of changes made outside paste (for example F4/Ctrl+D fill) so Ctrl+Z can revert it.</summary>
    public static void Record(DataGrid grid, IReadOnlyList<CellChange> changes)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0) return;
        var stack = grid.GetValue(UndoProperty) as UndoStack;
        if (stack is null || !ReferenceEquals(stack.Source, grid.ItemsSource))
        {
            stack = new UndoStack { Source = grid.ItemsSource };
            grid.SetValue(UndoProperty, stack);
        }

        stack.Batches.Push(changes);
        if (stack.Batches.Count > MaximumUndo)
        {
            var kept = stack.Batches.Take(MaximumUndo).Reverse().ToArray();
            stack.Batches.Clear();
            foreach (var batch in kept) stack.Batches.Push(batch);
        }
    }

    /// <summary>Reverts the last batch. Rows that left the grid (reloaded, removed or saved) are skipped.</summary>
    public static bool Undo(DataGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.GetValue(UndoProperty) is not UndoStack stack || !ReferenceEquals(stack.Source, grid.ItemsSource) || stack.Batches.Count == 0) return false;
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
        foreach (var change in stack.Batches.Pop().Reverse())
        {
            if (grid.Items.Contains(change.Row) && !change.Property.IsReadOnly) change.Property.SetValue(change.Row, change.OldValue);
        }

        return true;
    }

    /// <summary>Tab-separated text of the selected cells in view order; amounts as invariant decimal strings.</summary>
    public static string BuildText(DataGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var cells = grid.SelectedCells.Where(cell => cell.Column is { Visibility: Visibility.Visible } && cell.Item != CollectionView.NewItemPlaceholder).ToArray();
        if (cells.Length == 0 && grid.CurrentCell.Column is not null && grid.CurrentCell.Item is not null) cells = [grid.CurrentCell];
        if (cells.Length == 0) return string.Empty;
        var columns = cells.Select(cell => cell.Column).Distinct().OrderBy(column => column.DisplayIndex).ToArray();
        var rows = cells.Select(cell => cell.Item).Distinct().OrderBy(item => grid.Items.IndexOf(item)).ToArray();
        var builder = new StringBuilder();
        if (grid.ClipboardCopyMode == DataGridClipboardCopyMode.IncludeHeader && rows.Length > 1)
            builder.AppendJoin('\t', columns.Select(column => Clean(HeaderText(column)))).Append("\r\n");
        foreach (var row in rows)
        {
            builder.AppendJoin('\t', columns.Select(column => cells.Any(cell => ReferenceEquals(cell.Item, row) && ReferenceEquals(cell.Column, column)) ? Clean(CellText(column, row)) : string.Empty));
            builder.Append("\r\n");
        }

        return builder.ToString();
    }

    /// <summary>Applies tab-separated text from the current cell onward; returns (applied, rejected).</summary>
    public static (int Applied, int Rejected) Paste(DataGrid grid, string text)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.IsReadOnly || string.IsNullOrEmpty(text) || grid.CurrentCell.Column is not { } startColumn || grid.CurrentCell.Item is not { } startRow) return (0, 0);
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return (0, 0);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length > 1 && lines[^1].Length == 0) lines = lines[..^1];
        var columns = grid.Columns.Where(column => column.Visibility == Visibility.Visible).OrderBy(column => column.DisplayIndex).ToArray();
        var firstColumn = Array.IndexOf(columns, startColumn);
        var firstRow = grid.Items.IndexOf(startRow);
        if (firstColumn < 0 || firstRow < 0) return (0, 0);
        var changes = new List<CellChange>();
        var rejected = 0;
        for (var r = 0; r < lines.Length && firstRow + r < grid.Items.Count; r++)
        {
            var row = grid.Items[firstRow + r];
            if (row is null || row == CollectionView.NewItemPlaceholder) continue;
            var values = lines[r].Split('\t');
            for (var c = 0; c < values.Length && firstColumn + c < columns.Length; c++)
            {
                var column = columns[firstColumn + c];
                if (column.IsReadOnly || PropertyFor(column, row) is not { IsReadOnly: false, PropertyType: var type } property || type != typeof(string)) { rejected++; continue; }
                var value = values[c].Trim();
                if (IsNumeric(column)) value = PetraNumberFormat.TryParseInput(value, out var number) ? PetraNumberFormat.ToWire(number) : value;
                var old = property.GetValue(row);
                if (Equals(old, value)) continue;
                property.SetValue(row, value);
                if (HasError(row, property.Name)) { property.SetValue(row, old); rejected++; continue; }
                changes.Add(new CellChange(row, property, old));
            }
        }

        Record(grid, changes);
        return (changes.Count, rejected);
    }

    private static bool Copy(DataGrid grid)
    {
        var text = BuildText(grid);
        if (text.Length == 0) return false;
        try { Clipboard.SetText(text); }
        catch (COMException) { return false; } // The clipboard is held by another process; the framework copy still runs.
        return true;
    }

    private static bool PasteFromClipboard(DataGrid grid)
    {
        string text;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty; }
        catch (COMException) { return false; }
        if (text.Length == 0) return false;
        var (applied, rejected) = Paste(grid, text);
        PasteCompleted?.Invoke(applied, rejected);
        return applied > 0 || rejected > 0;
    }

    private static PropertyDescriptor? PropertyFor(DataGridColumn column, object row)
    {
        var path = GridEntry.GetCopyPath(column);
        if (string.IsNullOrEmpty(path) && column is DataGridBoundColumn { Binding: Binding { Path.Path: { Length: > 0 } bound } } && !bound.Contains('.', StringComparison.Ordinal)
            && !bound.Contains('[', StringComparison.Ordinal)) path = bound;
        return string.IsNullOrEmpty(path) ? null : TypeDescriptor.GetProperties(row)[path];
    }

    private static string CellText(DataGridColumn column, object row)
    {
        if (PropertyFor(column, row) is { } property && property.GetValue(row) is { } raw)
            return raw switch
            {
                string text => text,
                decimal amount => amount.ToString(CultureInfo.InvariantCulture),
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTime time => time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => raw.ToString() ?? string.Empty,
            };
        return column.OnCopyingCellClipboardContent(row)?.ToString() ?? string.Empty;
    }

    private static string HeaderText(DataGridColumn column) => column.Header switch
    {
        TextBlock block => block.Text,
        null => column.SortMemberPath,
        var header => header.ToString() ?? string.Empty,
    };

    private static string Clean(string text) => text.Replace('\t', ' ').Replace("\r", " ", StringComparison.Ordinal).Replace('\n', ' ');

    /// <summary>Amount/quantity columns are recognized by the design system's numeric cell style.</summary>
    private static bool IsNumeric(DataGridColumn column)
    {
        if (GetNumeric(column) is { } marked) return marked;
        if (column is not DataGridBoundColumn { ElementStyle: { } style }) return false;
        var numeric = Application.Current?.TryFindResource("Text.Numeric") as Style;
        for (var current = style; current is not null; current = current.BasedOn) if (ReferenceEquals(current, numeric)) return true;
        return false;
    }

    private static bool HasError(object row, string property) => row switch
    {
        INotifyDataErrorInfo errors => errors.GetErrors(property)?.Cast<object>().Any() == true,
        IDataErrorInfo info => !string.IsNullOrEmpty(info[property]),
        _ => false,
    };
}
