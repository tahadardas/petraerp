using System.Text;
using System.Text.RegularExpressions;

namespace PetraERP.Domain.Parties;

public static partial class ArabicTextNormalizer
{
    private static readonly char[] TokenSeparators = [' ', '\t', '\r', '\n', '-', '_', '/', '\\', ',', '.', ';', ':', '(', ')', '[', ']', '{', '}'];

    [GeneratedRegex(@"[\u064B-\u0652\u0670\u0640]")]
    private static partial Regex DiacriticsAndTatweelRegex();

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // Remove diacritics and tatweel
        var text = DiacriticsAndTatweelRegex().Replace(input, string.Empty);

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                // Alef variations -> plain Alef
                case '\u0622': // آ
                case '\u0623': // أ
                case '\u0625': // إ
                case '\u0671': // ٱ
                    sb.Append('\u0627'); // ا
                    break;

                // Taa Marbuta -> Haa
                case '\u0629': // ة
                    sb.Append('\u0647'); // ه
                    break;

                // Alef Maksura -> Yaa
                case '\u0649': // ى
                    sb.Append('\u064A'); // ي
                    break;

                // Eastern Arabic-Indic Digits (٠-٩)
                case '\u0660': sb.Append('0'); break;
                case '\u0661': sb.Append('1'); break;
                case '\u0662': sb.Append('2'); break;
                case '\u0663': sb.Append('3'); break;
                case '\u0664': sb.Append('4'); break;
                case '\u0665': sb.Append('5'); break;
                case '\u0666': sb.Append('6'); break;
                case '\u0667': sb.Append('7'); break;
                case '\u0668': sb.Append('8'); break;
                case '\u0669': sb.Append('9'); break;

                // Extended Arabic-Indic / Persian Digits (۰-۹)
                case '\u06F0': sb.Append('0'); break;
                case '\u06F1': sb.Append('1'); break;
                case '\u06F2': sb.Append('2'); break;
                case '\u06F3': sb.Append('3'); break;
                case '\u06F4': sb.Append('4'); break;
                case '\u06F5': sb.Append('5'); break;
                case '\u06F6': sb.Append('6'); break;
                case '\u06F7': sb.Append('7'); break;
                case '\u06F8': sb.Append('8'); break;
                case '\u06F9': sb.Append('9'); break;

                default:
                    sb.Append(char.ToLowerInvariant(ch));
                    break;
            }
        }

        return sb.ToString().Trim();
    }

    public static string[] Tokenize(string? input)
    {
        var normalized = Normalize(input);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        return normalized.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Deterministic token-prefix matching: returns true if every token in <paramref name="query"/>
    /// is a prefix of at least one token in <paramref name="target"/>.
    /// E.g. 'مص 116', 'جب 116', '116 مص', 'مص ١١٦' all match 'مصاريف جبالة 116'.
    /// </summary>
    public static bool MatchesTokenPrefixes(string? query, string? target)
    {
        var queryTokens = Tokenize(query);
        if (queryTokens.Length == 0)
        {
            return true;
        }

        var targetTokens = Tokenize(target);
        if (targetTokens.Length == 0)
        {
            return false;
        }

        foreach (var qToken in queryTokens)
        {
            var matched = false;
            foreach (var tToken in targetTokens)
            {
                if (tToken.StartsWith(qToken, StringComparison.Ordinal)
                    || (tToken.Length > 2 && tToken.StartsWith("ال", StringComparison.Ordinal)
                        && tToken.AsSpan(2).StartsWith(qToken.AsSpan(), StringComparison.Ordinal)))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }
}
