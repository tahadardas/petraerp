using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace PetraERP.Desktop.Controls;

/// <summary>Kinds of financial number display. Each has its own scale; none goes through binary floating point.</summary>
public enum NumberKind
{
    /// <summary>Amounts: 2 to 4 decimals ("#,##0.00##").</summary>
    Money,
    /// <summary>Quantities: 0 to 4 decimals.</summary>
    Quantity,
    /// <summary>Exchange rates and factors: 0 to 8 decimals, no grouping.</summary>
    Rate,
    /// <summary>Percentages: 0 to 4 decimals.</summary>
    Percent,
}

/// <summary>How grouping and decimal separators are shown. Digits are always ASCII and laid out left to right.</summary>
public enum NumberDisplayMode
{
    /// <summary>1,234.56 in every language (the accounting default and the clipboard format).</summary>
    Invariant = 0,
    /// <summary>The UI culture's separators (for example ٬ and ٫ in Arabic) with ASCII digits.</summary>
    Culture = 1,
    GroupThree = 2,
    GroupTwo = 3,
    NoGrouping = 4,
    OperatingSystem = 5,
}

public enum DecimalDisplayMode { Automatic=0, Dot=1, Comma=2, Arabic=3, OperatingSystem=4 }

/// <summary>
/// Central formatter and parser for financial numbers. Server values arrive as invariant decimal strings (ADR-0002) and
/// are parsed with <see cref="decimal"/> only. User input accepts Arabic-Indic and Eastern Arabic-Indic digits and the
/// Arabic separators, and is normalized to the invariant wire format. Nothing here rounds: a value with more decimals than
/// its kind allows is reported invalid instead of silently changed.
/// </summary>
public static class PetraNumberFormat
{
    private static NumberDisplayMode mode = NumberDisplayMode.Invariant;
    private static CultureInfo culture = CultureInfo.InvariantCulture;
    private static CultureInfo operatingSystemCulture = CultureInfo.CurrentCulture;
    private static DecimalDisplayMode decimals;
    public static void CaptureOperatingSystemCulture(CultureInfo value)
    {
        ArgumentNullException.ThrowIfNull(value);
        operatingSystemCulture = CultureInfo.ReadOnly((CultureInfo)value.Clone());
    }

    /// <summary>Raised when the display mode or culture changes so open views can re-format.</summary>
    public static event EventHandler? Changed;

    public static NumberDisplayMode Mode => mode;

    /// <summary>Sets the display mode and UI culture (called by the shell on language/preference change).</summary>
    public static void Configure(NumberDisplayMode displayMode, CultureInfo uiCulture, DecimalDisplayMode decimalMode = DecimalDisplayMode.Automatic)
    {
        ArgumentNullException.ThrowIfNull(uiCulture);
        if (!Enum.IsDefined(displayMode) || !Enum.IsDefined(decimalMode)) throw new ArgumentOutOfRangeException(nameof(displayMode));
        if (mode == displayMode && Equals(culture, uiCulture) && decimals == decimalMode) return;
        mode = displayMode; culture = uiCulture; decimals = decimalMode;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static int MaximumScale(NumberKind kind) => kind switch
    {
        NumberKind.Money => 4,
        NumberKind.Quantity => 4,
        NumberKind.Rate => 8,
        _ => 4,
    };

    private static string Pattern(NumberKind kind) => kind switch
    {
        NumberKind.Money => "#,##0.00##",
        NumberKind.Quantity => "#,##0.####",
        NumberKind.Rate => "0.########",
        _ => "#,##0.####",
    };

    /// <summary>Formats a decimal for display. Negative values use a leading minus or, when requested, parentheses.</summary>
    public static string Format(decimal value, NumberKind kind = NumberKind.Money, bool parenthesesForNegative = false) =>
        FormatWith(value, kind, parenthesesForNegative, mode, mode == NumberDisplayMode.OperatingSystem ? operatingSystemCulture : culture, decimals);

    /// <summary>Formats with an explicit mode and culture (no shared state); used by previews and tests.</summary>
    internal static string FormatWith(decimal value, NumberKind kind, bool parenthesesForNegative, NumberDisplayMode displayMode, CultureInfo uiCulture, DecimalDisplayMode decimalMode = DecimalDisplayMode.Automatic)
    {
        var text = Math.Abs(value).ToString(Pattern(kind), DisplayFormat(displayMode, uiCulture, decimalMode));
        if (kind == NumberKind.Percent) text += "%";
        return value < 0 ? (parenthesesForNegative ? "(" + text + ")" : "-" + text) : text;
    }

    /// <summary>Formats an invariant wire string. Text that is not a decimal is returned unchanged (never invented).</summary>
    public static string Format(string? wire, NumberKind kind = NumberKind.Money, bool parenthesesForNegative = false) =>
        TryParseWire(wire, out var value) ? Format(value, kind, parenthesesForNegative) : wire ?? string.Empty;

    public static bool TryParseWire(string? wire, out decimal value)
    {
        value = 0m;
        return !string.IsNullOrEmpty(wire) && decimal.TryParse(wire, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    public static bool IsZero(string? wire) => TryParseWire(wire, out var value) && value == 0m;

    /// <summary>True for "-1.00", "(1.00)" and negative wire values (used for the negative tone, never for meaning alone).</summary>
    public static bool IsNegative(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')')) return true;
        return TryParseInput(trimmed, out var value) && value < 0m;
    }

    /// <summary>
    /// Parses user input: ASCII, Arabic-Indic (٠-٩) and Eastern Arabic-Indic (۰-۹) digits; "." or "٫" as decimal point;
    /// "," "٬" or spaces as grouping; a leading minus or surrounding parentheses for negatives.
    /// </summary>
    public static bool TryParseInput(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (PetraMathEvaluator.ContainsOperator(text) && PetraMathEvaluator.TryEvaluate(text, out var evaluated))
        {
            value = evaluated;
            return true;
        }

        var normalized = NormalizeInput(text);
        if (normalized.Length == 0) return false;
        var negative = normalized.StartsWith('(') && normalized.EndsWith(')');
        if (negative)
        {
            normalized = normalized[1..^1];
            if (normalized.StartsWith('+') || normalized.StartsWith('-')) return false;
        }
        if (!decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out value)) return false;
        if (negative) value = -Math.Abs(value);
        return true;
    }

    /// <summary>Converts native digits and Arabic separators to ASCII and drops grouping characters.</summary>
    public static string NormalizeInput(string? text) =>
        NormalizeInputWith(text, mode, mode == NumberDisplayMode.OperatingSystem ? operatingSystemCulture : culture, decimals);

    /// <summary>
    /// Normalizes with explicit settings and no shared state. A separator that is ambiguous under those settings (for example
    /// "1,5" when "," groups thousands) returns an empty string: invalid input is refused, never guessed.
    /// </summary>
    internal static string NormalizeInputWith(string? text, NumberDisplayMode displayMode, CultureInfo uiCulture, DecimalDisplayMode decimalMode)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var format = DisplayFormat(displayMode, uiCulture, decimalMode);
        var group = format.NumberGroupSeparator;
        var point = format.NumberDecimalSeparator;
        var raw = new StringBuilder(text.Length);
        foreach (var ch in text.Trim())
        {
            if (ch is >= '٠' and <= '٩') raw.Append((char)('0' + ch - '٠'));
            else if (ch is >= '۰' and <= '۹') raw.Append((char)('0' + ch - '۰'));
            else if (ch is '\u061c' or '\u200e' or '\u200f') continue;
            else if (ch == '−') raw.Append('-');
            else if (ch == '٫') raw.Append(point);
            else if (ch is '٬' or ' ' or '\u00a0' or '\u202f') raw.Append(group);
            else if (ch == ',') raw.Append(point == "," ? point : group);
            else if (ch == '.') raw.Append(group == "." && point != "." ? group : point);
            else raw.Append(ch);
        }
        var value = raw.ToString();
        var parentheses = value.StartsWith('(') && value.EndsWith(')');
        if (parentheses) value = value[1..^1];
        if (value.Contains('(') || value.Contains(')')) return string.Empty;
        var sign = value.StartsWith('-') || value.StartsWith('+') ? value[..1] : string.Empty;
        if (sign.Length != 0) value = value[1..];
        if (parentheses && sign.Length != 0) return string.Empty;
        if (!value.Any(char.IsAsciiDigit)) return string.Empty;
        var parts = value.Split(point, StringSplitOptions.None);
        if (parts.Length > 2 || parts.Length == 2 && !parts[1].All(char.IsAsciiDigit)) return string.Empty;
        var groups = parts[0].Split(group, StringSplitOptions.None);
        var sizes = format.NumberGroupSizes.Length == 0 ? new[] { 3 } : format.NumberGroupSizes;
        if (groups.Length > 1)
        {
            for (var index = groups.Length - 1; index >= 0; index--)
            {
                var size = sizes[Math.Min(groups.Length - 1 - index, sizes.Length - 1)];
                if (groups[index].Length == 0 || !groups[index].All(char.IsAsciiDigit)
                    || index > 0 && (size == 0 || groups[index].Length != size)
                    || index == 0 && size > 0 && groups[index].Length > size) return string.Empty;
            }
        }
        else if (!groups[0].All(char.IsAsciiDigit)) return string.Empty;
        var integral = string.Concat(groups);
        if (integral.Length == 0) integral = "0";
        var normalized = sign + integral + (parts.Length == 2 ? "." + parts[1] : string.Empty);
        return parentheses ? "(" + normalized + ")" : normalized;
    }

    /// <summary>Number of significant decimals actually written (trailing zeros count as written).</summary>
    public static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;

    /// <summary>Invariant wire text without grouping, keeping exactly the decimals the user typed.</summary>
    public static string ToWire(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Validates input for a kind; the error is a localization key or null when valid.</summary>
    public static string? Validate(string? text, NumberKind kind, bool allowNegative, int? maximumScale = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!TryParseInput(text, out var value)) return "UI_InvalidNumber";
        if (!allowNegative && value < 0m) return "UI_NegativeNotAllowed";
        if (Scale(value) > (maximumScale ?? MaximumScale(kind))) return "UI_TooManyDecimals";
        return null;
    }

    private static NumberFormatInfo DisplayFormat(NumberDisplayMode displayMode, CultureInfo uiCulture, DecimalDisplayMode decimalMode = DecimalDisplayMode.Automatic)
    {
        var useCulture = displayMode is NumberDisplayMode.Culture or NumberDisplayMode.OperatingSystem;
        var format = (NumberFormatInfo)(useCulture ? uiCulture.NumberFormat : NumberFormatInfo.InvariantInfo).Clone();
        format.NativeDigits = new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        format.DigitSubstitution = DigitShapes.None;
        format.NumberGroupSizes = displayMode switch
        {
            NumberDisplayMode.GroupTwo => [2],
            NumberDisplayMode.NoGrouping => [],
            NumberDisplayMode.GroupThree or NumberDisplayMode.Invariant => [3],
            _ => format.NumberGroupSizes,
        };
        format.NumberDecimalSeparator = decimalMode switch
        {
            DecimalDisplayMode.Dot => ".", DecimalDisplayMode.Comma => ",", DecimalDisplayMode.Arabic => "٫",
            DecimalDisplayMode.OperatingSystem => operatingSystemCulture.NumberFormat.NumberDecimalSeparator,
            _ => (displayMode == NumberDisplayMode.Invariant || displayMode == NumberDisplayMode.GroupThree) ? "." : format.NumberDecimalSeparator,
        };
        if (displayMode is NumberDisplayMode.Invariant or NumberDisplayMode.GroupThree)
        {
            format.NumberGroupSeparator = ",";
        }
        if (format.NumberGroupSeparator == format.NumberDecimalSeparator) format.NumberGroupSeparator = "\u202f";
        return NumberFormatInfo.ReadOnly(format);
    }
}

/// <summary>
/// Displays numbers (wire strings, decimals or integers). ConverterParameter is a comma list of: money (default),
/// quantity, rate, percent, signed (parentheses for negatives), blank (zero shows as empty).
/// </summary>
public sealed class NumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var options = (parameter as string ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var kind = options.Contains("quantity") ? NumberKind.Quantity : options.Contains("rate") ? NumberKind.Rate
            : options.Contains("percent") ? NumberKind.Percent : NumberKind.Money;
        decimal? number = value switch
        {
            decimal d => d,
            int i => i,
            long l => l,
            string s when PetraNumberFormat.TryParseWire(s, out var parsed) => parsed,
            _ => null,
        };
        if (number is null) return value as string ?? string.Empty;
        if (options.Contains("blank") && number == 0m) return string.Empty;
        return PetraNumberFormat.Format(number.Value, kind, options.Contains("signed"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        var options = (parameter as string ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var kind = options.Contains("quantity") ? NumberKind.Quantity : options.Contains("rate") ? NumberKind.Rate
            : options.Contains("percent") ? NumberKind.Percent : NumberKind.Money;

        if (string.IsNullOrWhiteSpace(text))
        {
            if (targetType == typeof(string)) return string.Empty;
            if (targetType == typeof(decimal?)) return null!;
            if (targetType == typeof(decimal))
            {
                if (options.Contains("zeroifempty")) return 0m;
                return System.Windows.DependencyProperty.UnsetValue;
            }
            return System.Windows.DependencyProperty.UnsetValue;
        }

        if (!PetraNumberFormat.TryParseInput(text, out var number)
            || PetraNumberFormat.Scale(number) > PetraNumberFormat.MaximumScale(kind)
            || number < 0m && !options.Contains("negative") && !options.Contains("signed"))
        {
            throw new FormatException(PetraInputText.Localize("UI_InvalidNumber"));
        }

        if (targetType == typeof(decimal) || targetType == typeof(decimal?)) return number;
        if (targetType == typeof(string)) return PetraNumberFormat.ToWire(number);
        return System.Windows.DependencyProperty.UnsetValue;
    }
}

/// <summary>True when the displayed or wire value is negative; drives the negative tone together with the sign.</summary>
public sealed class NegativeNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        decimal d => d < 0m,
        int i => i < 0,
        long l => l < 0,
        string s => PetraNumberFormat.IsNegative(s),
        _ => false,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Reusable TextBlock with tabular ASCII numerals aligned to the right edge.</summary>
public class PetraMoneyTextBlock : System.Windows.Controls.TextBlock
{
    public PetraMoneyTextBlock()
    {
        FlowDirection = System.Windows.FlowDirection.LeftToRight;
        TextAlignment = System.Windows.TextAlignment.Right;
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        System.Windows.Media.NumberSubstitution.SetSubstitution(this, System.Windows.Media.NumberSubstitutionMethod.European);
    }
}

/// <summary>Reusable TextBlock for quantities with tabular numerals.</summary>
public class PetraQuantityTextBlock : PetraMoneyTextBlock
{
}

/// <summary>Reusable TextBlock for exchange rates and precision factors.</summary>
public class PetraRateTextBlock : PetraMoneyTextBlock
{
}

/// <summary>Standardized accounting numeric DataGrid column with right alignment and ASCII European numerals.</summary>
public class PetraNumericDataGridColumn : System.Windows.Controls.DataGridTextColumn
{
    public PetraNumericDataGridColumn()
    {
        var style = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
        style.Setters.Add(new System.Windows.Setter(System.Windows.FrameworkElement.FlowDirectionProperty, System.Windows.FlowDirection.LeftToRight));
        style.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.TextAlignmentProperty, System.Windows.TextAlignment.Right));
        style.Setters.Add(new System.Windows.Setter(System.Windows.FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch));
        style.Setters.Add(new System.Windows.Setter(System.Windows.Media.NumberSubstitution.SubstitutionProperty, System.Windows.Media.NumberSubstitutionMethod.European));
        ElementStyle = style;

        var editingStyle = new System.Windows.Style(typeof(System.Windows.Controls.TextBox));
        editingStyle.Setters.Add(new System.Windows.Setter(System.Windows.FrameworkElement.FlowDirectionProperty, System.Windows.FlowDirection.LeftToRight));
        editingStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBox.TextAlignmentProperty, System.Windows.TextAlignment.Right));
        editingStyle.Setters.Add(new System.Windows.Setter(System.Windows.Media.NumberSubstitution.SubstitutionProperty, System.Windows.Media.NumberSubstitutionMethod.European));
        EditingElementStyle = editingStyle;
    }
}

