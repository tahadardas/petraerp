namespace PetraERP.Infrastructure.AlAmeenMigration;

/// <summary>
/// Al-Ameen does not store an accounting category on a posting account. The category follows the chart layout, so it is
/// derived from the root of each account's branch. The convention below was reviewed against the Dardas 2025/2026 charts:
/// root 1 = assets, root 2 = liabilities, roots 6 and 7 = revenue, root 10 and the equity branch 211 = equity, every
/// other root = expenses. The result is reported in the preview so the owner confirms it before anything is written.
/// </summary>
internal static class AlAmeenAccountClassifier
{
    internal const short Assets = 0;
    internal const short Liabilities = 1;
    internal const short Equity = 2;
    internal const short Revenue = 3;
    internal const short Expenses = 4;

    internal sealed record Row(Guid Id, Guid? ParentId, string Code, string Name);

    /// <summary>
    /// Classifies every account and re-roots an account whose category differs from its parent's (PetraERP requires a child
    /// to share its parent's category; Al-Ameen nests equity under fixed liabilities). Leaves are the only posting accounts.
    /// </summary>
    internal static List<SourceAccount> Classify(IReadOnlyList<Row> rows)
    {
        var byId = rows.ToDictionary(r => r.Id);
        var parentsWithChildren = rows.Where(r => r.ParentId is Guid p && byId.ContainsKey(p)).Select(r => r.ParentId!.Value).ToHashSet();

        short TypeOf(Row row)
        {
            var current = row;
            var chain = new List<string> { current.Code };
            var guard = 0;
            while (current.ParentId is Guid parent && byId.TryGetValue(parent, out var next) && guard++ < 64)
            {
                current = next;
                chain.Add(current.Code);
            }

            var root = chain[^1];
            if (chain.Contains("211", StringComparer.Ordinal) || root == "10")
                return Equity;
            return root switch
            {
                "1" => Assets,
                "2" => Liabilities,
                "6" or "7" => Revenue,
                _ => Expenses,
            };
        }

        var types = rows.ToDictionary(r => r.Id, TypeOf);
        return rows.Select(r =>
        {
            var parent = r.ParentId is Guid p && byId.ContainsKey(p) && types[p] == types[r.Id] ? p : (Guid?)null;
            return new SourceAccount(r.Id, parent, r.Code, r.Name, types[r.Id], (short)(parentsWithChildren.Contains(r.Id) ? 0 : 1));
        }).ToList();
    }

    internal static string Describe(short type) => type switch
    {
        Assets => "أصول",
        Liabilities => "خصوم",
        Equity => "حقوق ملكية",
        Revenue => "إيرادات",
        _ => "مصروفات",
    };
}
