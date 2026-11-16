using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PetraERP.Contracts.Financial;

/// <summary>
/// The ADR-0002 wire format for financial values: an invariant decimal string such as "1234.5600".
/// Parsing is strict: no exponent, grouping, whitespace, leading '+', or excess fractional digits. A value
/// with more fractional digits than allowed is rejected, never rounded. Formatting always emits the scale.
/// </summary>
public static class FinancialDecimalText
{
    public const int MoneyScale = 4;
    public const int QuantityScale = 6;
    public const int RateScale = 8;

    /// <summary>OpenAPI pattern for a money amount string (15 integer digits, up to 4 fractional digits).</summary>
    public const string MoneyPattern = "^-?(0|[1-9][0-9]{0,14})(\\.[0-9]{1,4})?$";

    /// <summary>Integer digits of a decimal(19,8) exchange rate.</summary>
    public const int RateIntegerDigits = 11;

    /// <summary>OpenAPI pattern for an unsigned exchange rate string (11 integer digits, up to 8 fractional digits).</summary>
    public const string RatePattern = "^(0|[1-9][0-9]{0,10})(\\.[0-9]{1,8})?$";

    public static string Format(decimal value, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, 28);
        if (decimal.Round(value, scale, MidpointRounding.ToZero) != value)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The value has more fractional digits than the wire scale.");
        }

        return value.ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string? text, int maximumScale, int maximumIntegerDigits, [NotNullWhen(true)] out decimal? value)
    {
        value = null;
        if (string.IsNullOrEmpty(text) || text.Length > 64)
        {
            return false;
        }

        var span = text.AsSpan();
        var negative = span[0] == '-';
        if (negative)
        {
            span = span[1..];
        }

        var point = span.IndexOf('.');
        var integerPart = point < 0 ? span : span[..point];
        var fractionPart = point < 0 ? ReadOnlySpan<char>.Empty : span[(point + 1)..];
        if (integerPart.IsEmpty || integerPart.Length > maximumIntegerDigits || (point >= 0 && fractionPart.IsEmpty)
            || fractionPart.Length > maximumScale || (integerPart.Length > 1 && integerPart[0] == '0')
            || !IsDigits(integerPart) || !IsDigits(fractionPart))
        {
            return false;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool IsDigits(ReadOnlySpan<char> characters)
    {
        foreach (var character in characters)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
