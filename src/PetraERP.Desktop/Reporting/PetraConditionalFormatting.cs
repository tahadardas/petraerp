using System.Globalization;
using System.Windows.Media;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Reporting;

/// <summary>Comparison operators for report conditional formatting.</summary>
public enum PetraFormatOperator
{
    GreaterThan = 0,
    LessThan = 1,
    Equals = 2,
    NotEquals = 3,
    Contains = 4,
    Between = 5,
}

/// <summary>High-contrast, eye-friendly soft highlight palettes inspired by Al-Ameen 10.</summary>
public enum PetraFormatColor
{
    SoftRed = 0,    // Negative / Overdue / Warning
    SoftGreen = 1,  // Profit / Credit / Approved
    SoftYellow = 2, // Pending / Attention
    SoftBlue = 3,   // Informational / Settled
}

/// <summary>
/// A single conditional formatting rule applied to report rows.
/// </summary>
public sealed record PetraConditionalFormatRule(
    string ColumnKey,
    string ColumnHeading,
    PetraFormatOperator Operator,
    string Value1,
    string Value2,
    PetraFormatColor Style)
{
    public string DisplayText => $"{ColumnHeading} {OperatorSymbol(Operator)} {Value1}{(Operator == PetraFormatOperator.Between ? " .. " + Value2 : "")} ({ColorName(Style)})";

    public Brush BackgroundBrush => Style switch
    {
        PetraFormatColor.SoftRed => SoftRedBg,
        PetraFormatColor.SoftGreen => SoftGreenBg,
        PetraFormatColor.SoftYellow => SoftYellowBg,
        _ => SoftBlueBg,
    };

    public Brush ForegroundBrush => Style switch
    {
        PetraFormatColor.SoftRed => SoftRedFg,
        PetraFormatColor.SoftGreen => SoftGreenFg,
        PetraFormatColor.SoftYellow => SoftYellowFg,
        _ => SoftBlueFg,
    };

    private static readonly SolidColorBrush SoftRedBg = Freeze(new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)));
    private static readonly SolidColorBrush SoftRedFg = Freeze(new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B)));
    private static readonly SolidColorBrush SoftGreenBg = Freeze(new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7)));
    private static readonly SolidColorBrush SoftGreenFg = Freeze(new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34)));
    private static readonly SolidColorBrush SoftYellowBg = Freeze(new SolidColorBrush(Color.FromRgb(0xFE, 0xF0, 0x8A)));
    private static readonly SolidColorBrush SoftYellowFg = Freeze(new SolidColorBrush(Color.FromRgb(0x85, 0x4D, 0x0E)));
    private static readonly SolidColorBrush SoftBlueBg = Freeze(new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFE)));
    private static readonly SolidColorBrush SoftBlueFg = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF)));

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    public bool Matches(IReadOnlyDictionary<string, object?> row, ReportColumn column)
    {
        if (!row.TryGetValue(ColumnKey, out var raw) || raw is null) return false;

        if (column.Kind is ReportValueKind.Amount or ReportValueKind.Quantity or ReportValueKind.Integer)
        {
            var cellValue = ReportDocument.DecimalValue(raw);
            if (!decimal.TryParse(Value1.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v1)) return false;

            return Operator switch
            {
                PetraFormatOperator.GreaterThan => cellValue > v1,
                PetraFormatOperator.LessThan => cellValue < v1,
                PetraFormatOperator.Equals => cellValue == v1,
                PetraFormatOperator.NotEquals => cellValue != v1,
                PetraFormatOperator.Between => decimal.TryParse(Value2.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v2) &&
                                               cellValue >= Math.Min(v1, v2) && cellValue <= Math.Max(v1, v2),
                _ => false,
            };
        }

        if (column.Kind is ReportValueKind.Date)
        {
            var cellDate = ReportDocument.DateValue(raw);
            if (!DateTime.TryParse(Value1.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1)) return false;

            return Operator switch
            {
                PetraFormatOperator.GreaterThan => cellDate > d1,
                PetraFormatOperator.LessThan => cellDate < d1,
                PetraFormatOperator.Equals => cellDate.Date == d1.Date,
                PetraFormatOperator.NotEquals => cellDate.Date != d1.Date,
                PetraFormatOperator.Between => DateTime.TryParse(Value2.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2) &&
                                               cellDate >= (d1 < d2 ? d1 : d2) && cellDate <= (d1 > d2 ? d1 : d2),
                _ => false,
            };
        }

        var text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
        var searchVal = Value1.Trim();

        return Operator switch
        {
            PetraFormatOperator.Contains => text.Contains(searchVal, StringComparison.OrdinalIgnoreCase),
            PetraFormatOperator.Equals => string.Equals(text, searchVal, StringComparison.OrdinalIgnoreCase),
            PetraFormatOperator.NotEquals => !string.Equals(text, searchVal, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static string OperatorSymbol(PetraFormatOperator op) => op switch
    {
        PetraFormatOperator.GreaterThan => ">",
        PetraFormatOperator.LessThan => "<",
        PetraFormatOperator.Equals => "=",
        PetraFormatOperator.NotEquals => "!=",
        PetraFormatOperator.Contains => "يحتوي",
        PetraFormatOperator.Between => "بين",
        _ => "=",
    };

    private static string ColorName(PetraFormatColor color) => color switch
    {
        PetraFormatColor.SoftRed => "أحمر",
        PetraFormatColor.SoftGreen => "أخضر",
        PetraFormatColor.SoftYellow => "أصفر",
        _ => "أزرق",
    };
}
