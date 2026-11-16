using System.Globalization;

namespace PetraERP.Desktop.Controls;

/// <summary>Client completeness and unified numeric validation; server validation and authorization remain authoritative.</summary>
public static class RequiredInput
{
    public static bool Selection(Guid? value) => value is { } id && id != Guid.Empty;
    public static bool Currency(string? value) => value?.Trim() is { Length: 3 } text && text.All(char.IsAsciiLetter);

    /// <summary>
    /// Validates numeric text entry. Accepts ASCII digits, standard thousands grouping, and normalized input digits.
    /// Rejects malformed grouping, ambiguous decimal points, out-of-range scale, or unpermitted negatives.
    /// </summary>
    public static bool AmountText(string? value, int scale = 4, bool positive = false, bool allowNegative = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        var kind = scale > PetraNumberFormat.MaximumScale(NumberKind.Money) ? NumberKind.Rate : NumberKind.Money;
        if (PetraNumberFormat.Validate(trimmed, kind, allowNegative, scale) is not null
            || !PetraNumberFormat.TryParseInput(trimmed, out var number)) return false;
        if (positive && number <= 0m) return false;
        return true;
    }

    /// <summary>Parses localized or grouped user input text into a decimal domain value.</summary>
    public static bool TryParseAmount(string? value, out decimal number)
    {
        number = 0m;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return PetraNumberFormat.TryParseInput(value.Trim(), out number);
    }

    /// <summary>Converts user-entered numeric text into invariant wire decimal format.</summary>
    public static string? ToWire(string? value) =>
        TryParseAmount(value, out var number) ? PetraNumberFormat.ToWire(number) : null;

    public static bool DiscountExpression(string? value) =>
        Sales.CascadingDiscountCalculator.IsValidExpression(value);
}
