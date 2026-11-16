using System.ComponentModel;
using System.Globalization;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Reporting;

/// <summary>
/// UI-free construction and search of the header-filter value list. Values are grouped by their RAW canonical
/// value, never by the formatted text: raw 1.231 and 1.234 both render as "1.23" at two decimals but stay two
/// separate entries, so selecting one can never silently drop (or silently include) the other.
/// </summary>
internal static class PetraReportDistinctValues
{
    /// <summary>Upper bound of list rows materialised into the popup; search and select-all still cover every distinct value.</summary>
    internal const int VisibleLimit = 500;

    /// <summary>Canonical raw key the filter engine parses back losslessly (invariant decimal without trailing zeros, yyyy-MM-dd, or the text itself).</summary>
    internal static string RawKey(object? value, ReportColumn column)
    {
        if (value is null) return string.Empty;
        return column.Kind switch
        {
            ReportValueKind.Amount or ReportValueKind.Quantity or ReportValueKind.Integer =>
                CanonicalDecimal(ReportDocument.DecimalValue(value)),
            ReportValueKind.Date => ReportDocument.DateValue(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    internal static string CanonicalDecimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>Saved selections may carry "1.230"; they must still pre-select the entry whose key is "1.23".</summary>
    private static string NormalizeSelection(string raw, ReportColumn column) =>
        column.Kind is ReportValueKind.Amount or ReportValueKind.Quantity or ReportValueKind.Integer &&
        decimal.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out var number)
            ? CanonicalDecimal(number)
            : raw;

    internal static IReadOnlyList<DistinctFilterItem> Build(
        ReportDocument document,
        ReportColumn column,
        IReadOnlyCollection<string>? preselected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(column);
        var groups = new Dictionary<string, (string Formatted, int Count)>(StringComparer.Ordinal);
        var processed = 0;
        foreach (var row in document.Rows)
        {
            if ((++processed & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            row.TryGetValue(column.Key, out var value);
            var raw = RawKey(value, column);
            groups[raw] = groups.TryGetValue(raw, out var existing)
                ? (existing.Formatted, existing.Count + 1)
                : (ReportGridCellConverter.Format(value, column), 1);
        }
        cancellationToken.ThrowIfCancellationRequested();

        var collisions = groups.Values.GroupBy(group => group.Formatted, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var selected = preselected?.Select(item => NormalizeSelection(item, column)).ToHashSet(StringComparer.Ordinal);
        cancellationToken.ThrowIfCancellationRequested();
        return groups
            .OrderByDescending(pair => pair.Value.Count)
            .ThenBy(pair => pair.Value.Formatted, StringComparer.CurrentCulture)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DistinctFilterItem(
                pair.Key, pair.Value.Formatted, pair.Value.Count,
                selected is null || selected.Contains(pair.Key),
                showRaw: collisions.Contains(pair.Value.Formatted) && !string.Equals(pair.Key, pair.Value.Formatted, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>Searches ALL distinct values (display text and raw value, Arabic-normalised) and returns the full match set plus a bounded slice for the UI.</summary>
    internal static DistinctSearchResult Search(
        IReadOnlyList<DistinctFilterItem> all,
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(all);
        var text = query?.Trim() ?? string.Empty;
        if (text.Length == 0) return new DistinctSearchResult(all, all.Count <= limit ? all : all.Take(limit).ToArray());
        var needle = PetraReportFilterEngine.NormalizeArabic(text);
        var matches = new List<DistinctFilterItem>();
        for (var index = 0; index < all.Count; index++)
        {
            if ((index & 0x3FF) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (all[index].SearchKey.Contains(needle, StringComparison.OrdinalIgnoreCase)) matches.Add(all[index]);
        }
        return new DistinctSearchResult(matches, matches.Count <= limit ? matches : matches.Take(limit).ToArray());
    }
}

internal sealed record DistinctSearchResult(IReadOnlyList<DistinctFilterItem> Matches, IReadOnlyList<DistinctFilterItem> Shown);

/// <summary>Pure, cancellable, stable sort of immutable report rows by RAW typed values (never by display text).</summary>
internal static class PetraReportRowSorter
{
    internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> Sort(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        ReportColumn column,
        ListSortDirection direction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(column);
        var keys = new object?[rows.Count];
        for (var index = 0; index < keys.Length; index++)
        {
            if ((index & 0x3FF) == 0) cancellationToken.ThrowIfCancellationRequested();
            keys[index] = rows[index].TryGetValue(column.Key, out var value) && value is not null ? KeyOf(value, column.Kind) : null;
        }
        var comparer = new KeyComparer(cancellationToken);
        var order = Enumerable.Range(0, keys.Length);
        // OrderBy/OrderByDescending are stable, so equal keys keep their snapshot order in both directions.
        var sortedIndexes = direction == ListSortDirection.Ascending
            ? order.OrderBy(index => keys[index], comparer)
            : order.OrderByDescending(index => keys[index], comparer);
        var result = new IReadOnlyDictionary<string, object?>[keys.Length];
        var position = 0;
        foreach (var index in sortedIndexes) result[position++] = rows[index];
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static object KeyOf(object value, ReportValueKind kind) => kind switch
    {
        ReportValueKind.Amount or ReportValueKind.Quantity or ReportValueKind.Integer => ReportDocument.DecimalValue(value),
        ReportValueKind.Date => ReportDocument.DateValue(value),
        _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty,
    };

    private sealed class KeyComparer(CancellationToken cancellationToken) : IComparer<object?>
    {
        private int calls;

        public int Compare(object? x, object? y)
        {
            if ((++calls & 0xFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return x switch
            {
                decimal left => left.CompareTo((decimal)y),
                DateTime left => left.CompareTo((DateTime)y),
                _ => StringComparer.CurrentCultureIgnoreCase.Compare((string)x, (string)y),
            };
        }
    }
}
