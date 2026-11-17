using System.Globalization;
using System.Text;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Deterministic Arabic financial Tafqeet (number-to-words) generator for invoices, receipts, and vouchers.
/// Follows standard Arabic accounting rules ("فقط ... لا غير") with exact grammatical scales and currency unit inflection.
/// </summary>
public static class ArabicTafqeet
{
    private static readonly string[] Ones =
    [
        "", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة",
        "عشرة", "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر",
        "ستة عشر", "سبعة عشر", "ثمانية عشر", "تسعة عشر"
    ];

    private static readonly string[] Tens =
    [
        "", "", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون"
    ];

    private static readonly string[] Hundreds =
    [
        "", "مائة", "مائتان", "ثلاثمائة", "أربعمائة", "خمسمائة", "ستمائة", "سبعمائة", "ثمانمائة", "تسعمائة"
    ];

    private static readonly (string Single, string Pair, string Plural, string Accusative)[] Scales =
    [
        ("", "", "", ""),
        ("ألف", "ألفان", "آلاف", "ألف"),
        ("مليون", "مليونان", "ملايين", "مليون"),
        ("مليار", "ملياران", "مليارات", "مليار"),
        ("تريليون", "تريليونان", "تريليونات", "تريليون")
    ];

    private static readonly Dictionary<string, (string Currency, string SubUnit)> KnownCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SYP"] = ("ليرة سورية", "قرش"),
        ["SAR"] = ("ريال سعودي", "هللة"),
        ["AED"] = ("درهم إماراتي", "فلس"),
        ["KWD"] = ("دينار كويتي", "فلس"),
        ["JOD"] = ("دينار أردني", "قرش"),
        ["EGP"] = ("جنيه مصري", "قرش"),
        ["USD"] = ("دولار أمريكي", "سنت"),
        ["EUR"] = ("يورو", "سنت"),
        ["TRY"] = ("ليرة تركية", "قرش"),
        ["QAR"] = ("ريال قطري", "درهم"),
        ["BHD"] = ("دينار بحريني", "فلس"),
        ["OMR"] = ("ريال عماني", "بيسة")
    };

    /// <summary>
    /// Converts a numeric amount to formal Arabic financial written text.
    /// Example: 150250.50 -> "فقط مائة وخمسون ألف ومئتان وخمسون ليرة سورية وخمسون قرشاً لا غير"
    /// </summary>
    public static string ToWords(decimal amount, string? currency = null, string? subUnit = null, int? fractionDigits = null)
    {
        var digits = fractionDigits ?? CurrencyFractionDigits(currency);
        if (digits is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(fractionDigits), "Fraction digits must be between 0 and 3.");

        // Round the WHOLE amount once to the currency's minor-unit precision (away from zero, like the posted amounts), so a
        // carry such as 1.999 -> 2.00 reaches the integral part and the fraction never reads "100" of 100.
        var rounded = decimal.Round(amount, digits, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded) > MaximumAmount) throw new ArgumentOutOfRangeException(nameof(amount), "Amount exceeds the supported tafqeet range.");

        if (rounded == 0m)
        {
            var zeroCurrency = ResolveCurrency(currency);
            return string.IsNullOrWhiteSpace(zeroCurrency.Currency)
                ? "فقط صفر لا غير"
                : $"فقط صفر {zeroCurrency.Currency} لا غير";
        }

        var isNegative = rounded < 0m;
        var absAmount = Math.Abs(rounded);
        var integral = (long)Math.Truncate(absAmount);
        var fraction = digits == 0 ? 0 : (int)((absAmount - integral) * Pow10(digits));

        var (currName, subName) = ResolveCurrency(currency, subUnit);
        var sb = new StringBuilder();

        sb.Append(isNegative ? "سالب فقط " : "فقط ");

        if (integral > 0)
        {
            sb.Append(ConvertIntegerToWords(integral));
            if (!string.IsNullOrWhiteSpace(currName))
            {
                sb.Append(' ').Append(currName);
            }
        }
        else
        {
            sb.Append("صفر");
            if (!string.IsNullOrWhiteSpace(currName))
            {
                sb.Append(' ').Append(currName);
            }
        }

        if (fraction > 0)
        {
            sb.Append(" و");
            sb.Append(ConvertGroup(fraction));
            if (!string.IsNullOrWhiteSpace(subName))
            {
                sb.Append(' ').Append(subName);
                // Accusative singular (tamyiz) after 11..99 of the last two digits: "قرشاً" / "هللةً".
                if (fraction % 100 is >= 11 and <= 99) sb.Append(subName.EndsWith('ة') ? "ً" : "اً");
            }
            else
            {
                sb.Append(" جزءاً من ").Append(ConvertIntegerToWords(Pow10(digits)));
            }
        }

        sb.Append(" لا غير");
        return sb.ToString();
    }

    /// <summary>
    /// Converts an integer into Arabic words without prefix or suffix.
    /// </summary>
    public static string ConvertIntegerToWords(long number)
    {
        if (number == 0) return "صفر";

        var groups = new List<int>();
        var temp = Math.Abs(number);
        while (temp > 0)
        {
            groups.Add((int)(temp % 1000));
            temp /= 1000;
        }

        var parts = new List<string>();
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var gVal = groups[i];
            if (gVal == 0) continue;

            if (i >= Scales.Length)
            {
                parts.Add(ConvertGroup(gVal));
                continue;
            }

            var scale = Scales[i];
            if (i == 0)
            {
                parts.Add(ConvertGroup(gVal));
            }
            else if (gVal == 1)
            {
                parts.Add(scale.Single);
            }
            else if (gVal == 2)
            {
                parts.Add(scale.Pair);
            }
            else if (gVal is >= 3 and <= 10)
            {
                parts.Add($"{ConvertGroup(gVal)} {scale.Plural}");
            }
            else
            {
                parts.Add($"{ConvertGroup(gVal)} {scale.Accusative}");
            }
        }

        return string.Join(" و", parts);
    }

    private static string ConvertGroup(int value)
    {
        var parts = new List<string>();
        var hundreds = value / 100;
        var remainder = value % 100;

        if (hundreds > 0 && hundreds < Hundreds.Length)
        {
            parts.Add(Hundreds[hundreds]);
        }

        if (remainder > 0)
        {
            if (remainder < 20)
            {
                parts.Add(Ones[remainder]);
            }
            else
            {
                var units = remainder % 10;
                var tens = remainder / 10;
                if (units > 0)
                {
                    parts.Add($"{Ones[units]} و{Tens[tens]}");
                }
                else
                {
                    parts.Add(Tens[tens]);
                }
            }
        }

        return string.Join(" و", parts);
    }

    /// <summary>Largest supported amount: below one quadrillion (the scale table ends at trillions).</summary>
    private const decimal MaximumAmount = 999_999_999_999_999.999m;

    /// <summary>Minor-unit digits per ISO code: 3 for dinars/rials divided into 1000 (KWD, BHD, OMR, JOD), otherwise 2.</summary>
    public static int CurrencyFractionDigits(string? currency) =>
        currency is not null && currency.Trim().ToUpperInvariant() is "KWD" or "BHD" or "OMR" or "JOD" ? 3 : 2;

    private static long Pow10(int digits) => digits switch { 0 => 1, 1 => 10, 2 => 100, _ => 1000 };

    private static (string Currency, string SubUnit) ResolveCurrency(string? currency, string? subUnit = null)
    {
        if (currency is null)
        {
            return ("ليرة سورية", subUnit ?? "قرش");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            return (string.Empty, subUnit ?? string.Empty);
        }

        var trimmed = currency.Trim();
        if (KnownCurrencies.TryGetValue(trimmed, out var known))
        {
            return (known.Currency, subUnit ?? known.SubUnit);
        }

        // If currency text is already in Arabic (e.g. "ليرة سورية" or "ريال")
        return (trimmed, subUnit ?? "قرش");
    }
}
