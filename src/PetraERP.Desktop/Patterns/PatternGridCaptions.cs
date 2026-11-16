using System.Windows;
using System.Windows.Controls;

namespace PetraERP.Desktop.Patterns;

/// <summary>
/// Applies the column captions defined in a pattern's line fields to a document grid.
/// A column without a pattern caption keeps (or gets back) its standard header.
/// </summary>
public static class PatternGridCaptions
{
    private static readonly DependencyProperty StandardHeaderProperty =
        DependencyProperty.RegisterAttached("StandardHeader", typeof(object), typeof(PatternGridCaptions));

    /// <param name="fieldsByColumn">Pattern field name for each grid column by position; null leaves that column untouched.</param>
    public static void Apply(DataGrid grid, IReadOnlyDictionary<string, string>? captions, IReadOnlyList<string?> fieldsByColumn)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(fieldsByColumn);
        for (var i = 0; i < fieldsByColumn.Count && i < grid.Columns.Count; i++)
        {
            if (fieldsByColumn[i] is not { } field) continue;
            var column = grid.Columns[i];
            if (column.ReadLocalValue(StandardHeaderProperty) == DependencyProperty.UnsetValue)
                column.SetValue(StandardHeaderProperty, column.Header);
            column.Header = captions is not null && captions.TryGetValue(field, out var caption) && !string.IsNullOrWhiteSpace(caption)
                ? caption
                : column.GetValue(StandardHeaderProperty);
        }
    }
}
