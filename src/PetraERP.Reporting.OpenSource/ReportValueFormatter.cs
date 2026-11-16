using System.Globalization;

namespace PetraERP.Reporting.OpenSource;

/// <summary>Presentation only; no aggregation, conversion or financial balance calculation.</summary>
internal static class ReportValueFormatter
{
    public static string Display(object? value, ReportColumn column) => value is null ? string.Empty : column.Kind switch
    {
        ReportValueKind.Amount => ReportDocument.DecimalValue(value).ToString("N" + column.DecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
        ReportValueKind.Quantity => ReportDocument.DecimalValue(value).ToString("0.########", CultureInfo.InvariantCulture),
        ReportValueKind.Integer => ReportDocument.DecimalValue(value).ToString("0", CultureInfo.InvariantCulture),
        ReportValueKind.Date => ReportDocument.DateValue(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    public static string Lossless(object? value, ReportColumn column) => value is null ? string.Empty : column.Kind switch
    {
        ReportValueKind.Amount or ReportValueKind.Quantity or ReportValueKind.Integer => ReportDocument.DecimalValue(value).ToString(CultureInfo.InvariantCulture),
        _ => Display(value, column),
    };
}
