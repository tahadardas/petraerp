using System.Collections.ObjectModel;
using System.Globalization;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Reporting;

public enum PetraSummaryKind
{
    Sum,
    Min,
    Max,
    Average,
    Count,
    DistinctCount,
}

public sealed record PetraReportCustomSummary(
    string ColumnKey,
    string ColumnHeading,
    PetraSummaryKind Kind,
    string? CustomLabel = null);

public sealed record PetraReportSummaryResult(
    string Label,
    string Value,
    string? Currency = null);

/// <summary>
/// Configurable presentation summaries over client visible rows or selection.
/// Crucial invariant: never modifies or replaces the server-authoritative accounting summaries held in ReportDocument.Summaries.
/// </summary>
public sealed class PetraReportSummaryManager
{
    public ObservableCollection<PetraReportCustomSummary> ConfiguredSummaries { get; } = [];

    public void AddSummary(string columnKey, string columnHeading, PetraSummaryKind kind, string? customLabel = null)
    {
        var existing = ConfiguredSummaries.FirstOrDefault(s =>
            string.Equals(s.ColumnKey, columnKey, StringComparison.Ordinal) && s.Kind == kind);
        if (existing is null)
        {
            ConfiguredSummaries.Add(new PetraReportCustomSummary(columnKey, columnHeading, kind, customLabel));
        }
    }

    public void RemoveSummary(PetraReportCustomSummary summary) => ConfiguredSummaries.Remove(summary);

    public void Clear() => ConfiguredSummaries.Clear();

    public IReadOnlyList<PetraReportSummaryResult> Calculate(
        ReportDocument? document,
        IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        if (document is null) return [];
        var rowsList = rows.ToList();
        var results = new List<PetraReportSummaryResult>();
        var columns = document.Columns.ToDictionary(c => c.Key, StringComparer.Ordinal);

        foreach (var config in ConfiguredSummaries)
        {
            if (!columns.TryGetValue(config.ColumnKey, out var column)) continue;

            var values = new List<object>();
            foreach (var row in rowsList)
            {
                if (row.TryGetValue(config.ColumnKey, out var val) && val is not null)
                {
                    values.Add(val);
                }
            }

            var label = !string.IsNullOrWhiteSpace(config.CustomLabel)
                ? config.CustomLabel
                : DefaultLabel(config.Kind, column.Heading);

            string formattedValue;
            string? currency = column.Kind == ReportValueKind.Amount ? document.CurrencyCode : null;

            switch (config.Kind)
            {
                case PetraSummaryKind.Count:
                    formattedValue = values.Count.ToString("N0", CultureInfo.InvariantCulture);
                    currency = null;
                    break;

                case PetraSummaryKind.DistinctCount:
                    var distinctCount = values.Select(v => v.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    formattedValue = distinctCount.ToString("N0", CultureInfo.InvariantCulture);
                    currency = null;
                    break;

                case PetraSummaryKind.Sum:
                    decimal sum = 0;
                    foreach (var v in values) sum += ReportDocument.DecimalValue(v);
                    formattedValue = sum.ToString("N" + column.DecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    break;

                case PetraSummaryKind.Average:
                    if (values.Count == 0)
                    {
                        formattedValue = "0.00";
                    }
                    else
                    {
                        decimal total = 0;
                        foreach (var v in values) total += ReportDocument.DecimalValue(v);
                        var avg = total / values.Count;
                        formattedValue = avg.ToString("N" + column.DecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    }
                    break;

                case PetraSummaryKind.Min:
                    if (values.Count == 0)
                    {
                        formattedValue = "-";
                    }
                    else if (column.Kind == ReportValueKind.Date)
                    {
                        var minDate = values.Select(ReportDocument.DateValue).Min();
                        formattedValue = minDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        currency = null;
                    }
                    else
                    {
                        var minVal = values.Select(ReportDocument.DecimalValue).Min();
                        formattedValue = minVal.ToString("N" + column.DecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    }
                    break;

                case PetraSummaryKind.Max:
                    if (values.Count == 0)
                    {
                        formattedValue = "-";
                    }
                    else if (column.Kind == ReportValueKind.Date)
                    {
                        var maxDate = values.Select(ReportDocument.DateValue).Max();
                        formattedValue = maxDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        currency = null;
                    }
                    else
                    {
                        var maxVal = values.Select(ReportDocument.DecimalValue).Max();
                        formattedValue = maxVal.ToString("N" + column.DecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    }
                    break;

                default:
                    formattedValue = "-";
                    break;
            }

            results.Add(new PetraReportSummaryResult(label, formattedValue, currency));
        }

        return results.AsReadOnly();
    }

    private static string DefaultLabel(PetraSummaryKind kind, string heading) => kind switch
    {
        PetraSummaryKind.Sum => $"مجموع {heading} / Sum",
        PetraSummaryKind.Average => $"متوسط {heading} / Avg",
        PetraSummaryKind.Min => $"أدنى {heading} / Min",
        PetraSummaryKind.Max => $"أعلى {heading} / Max",
        PetraSummaryKind.Count => $"عدد {heading} / Count",
        PetraSummaryKind.DistinctCount => $"قيم فريدة {heading} / Distinct",
        _ => heading,
    };
}
