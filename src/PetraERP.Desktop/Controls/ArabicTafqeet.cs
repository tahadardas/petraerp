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
    public static string ToWords(decimal amount, string? currency = null, string? subUnit = null)
    {
        if (amount == 0m)
        {
            var zeroCurrency = ResolveCurrency(currency);
            return string.IsNullOrWhiteSpace(zeroCurrency.Currency)
                ? "فقط صفر لا غير"
                : $"فقط صفر {zeroCurrency.Currency} لا غير";
        }

        var isNegative = amount < 0m;
        var absAmount = Math.Abs(amount);
        var integral = (long)Math.Truncate(absAmount);
        var fraction = (int)Math.Round((absAmount - integral) * 100m, MidpointRounding.AwayFromZero);

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
                if (fraction is >= 11 and <= 99) sb.Append("اً");
            }
            else
            {
                sb.Append(" جزءاً من مائة");
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
