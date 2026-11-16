namespace PetraERP.Desktop.Controls;

/// <summary>A selectable code + bilingual name entry for <see cref="LookupPicker"/>, with optional barcode.</summary>
public sealed record LookupItem(Guid Id, string Code, string NameArabic, string? NameEnglish, bool IsActive = true, string? Barcode = null)
{
    public string Display(bool arabic)
    {
        var name = arabic || string.IsNullOrWhiteSpace(NameEnglish) ? NameArabic : NameEnglish;
        return string.IsNullOrWhiteSpace(Code) ? name : Code + " — " + name;
    }

    public override string ToString() => Display(true);
}

/// <summary>
/// Type-ahead ranking used by <see cref="LookupPicker"/>: exact code or barcode, then code prefix, then Arabic/English name
/// containment, each in code order. Inactive entries are never offered for new selections.
/// </summary>
public static class LookupFilter
{
    public const int MaximumResults = 50;

    public static IReadOnlyList<LookupItem> Filter(IEnumerable<LookupItem> source, string? text, int maximum = MaximumResults)
    {
        ArgumentNullException.ThrowIfNull(source);
        var term = LookupText.Normalize(text);
        var active = source.Where(static item => item.IsActive);
        if (term.Length == 0)
        {
            return active.OrderBy(static item => item.Code, StringComparer.Ordinal).Take(maximum).ToArray();
        }

        return active
            .Select(item => (item, rank: Rank(item, term)))
            .Where(static pair => pair.rank < 3)
            .OrderBy(static pair => pair.rank)
            .ThenBy(static pair => pair.item.Code, StringComparer.Ordinal)
            .Select(static pair => pair.item)
            .Take(maximum)
            .ToArray();
    }

    private static int Rank(LookupItem item, string term)
    {
        var code = LookupText.Normalize(item.Code);
        if (string.Equals(code, term, StringComparison.Ordinal)) return 0;
        if (!string.IsNullOrEmpty(item.Barcode) && string.Equals(LookupText.Normalize(item.Barcode), term, StringComparison.Ordinal)) return 0;
        if (code.StartsWith(term, StringComparison.Ordinal)) return 1;
        if (LookupText.Matches(term, item.Code, item.NameArabic, item.NameEnglish, item.Barcode)) return 2;
        return 3;
    }
}
