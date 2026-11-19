namespace PetraERP.Infrastructure;

/// <summary>A set of document lines that become one journal line; a single line when journal detail is on.</summary>
internal sealed class JournalLineGroup<TKey, T>(TKey key, List<T> items) : IEnumerable<T>
{
    public TKey Key { get; } = key;

    public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class JournalLineGrouping
{
    /// <summary>
    /// Groups document lines by <paramref name="key"/> (first-appearance order), or gives every line its own group when
    /// <paramref name="detailed"/> is set (Al-Ameen: one journal line per document line, still inside ONE journal entry).
    /// </summary>
    public static IEnumerable<JournalLineGroup<TKey, T>> GroupForJournal<T, TKey>(this IEnumerable<T> source, Func<T, TKey> key, bool detailed) where TKey : notnull
    {
        if (detailed)
        {
            foreach (var item in source) yield return new JournalLineGroup<TKey, T>(key(item), [item]);
            yield break;
        }

        foreach (var group in source.GroupBy(key)) yield return new JournalLineGroup<TKey, T>(group.Key, [.. group]);
    }
}
