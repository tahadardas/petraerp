using System.Globalization;
using System.Text;

namespace PetraERP.Desktop.Controls;

/// <summary>Presentation-only search folding; original names and financial codes are never rewritten.</summary>
public static class LookupText
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var result = new StringBuilder(input.Length);
        foreach (var value in input)
        {
            if (value == '\u0640' || char.GetUnicodeCategory(value) == UnicodeCategory.NonSpacingMark) continue;
            result.Append(value switch
            {
                'آ' or 'أ' or 'إ' or 'ٱ' => 'ا', 'ة' => 'ه', 'ى' => 'ي',
                >= '\u0660' and <= '\u0669' => (char)('0' + value - '\u0660'),
                >= '\u06f0' and <= '\u06f9' => (char)('0' + value - '\u06f0'),
                _ => char.ToLowerInvariant(value),
            });
        }
        return result.ToString().Trim();
    }

    public static bool Matches(string? query, params string?[] values)
    {
        var text = Normalize(query);
        if (text.Length == 0) return true;
        var candidates = values.Select(Normalize).ToArray();
        if (candidates.Any(value => value.Contains(text, StringComparison.Ordinal))) return true;
        var words = Words(string.Join(' ', candidates));
        return Words(text).All(token => words.Any(word => word.StartsWith(token, StringComparison.Ordinal)
            || word.Length > 2 && word.StartsWith("ال", StringComparison.Ordinal) && word.AsSpan(2).StartsWith(token.AsSpan(), StringComparison.Ordinal)));
    }

    private static string[] Words(string text) => text.Split([' ', '\t', '\r', '\n', '-', '_', '/', '\\', ',', '.', ';', ':', '(', ')', '[', ']', '{', '}'], StringSplitOptions.RemoveEmptyEntries);
}
