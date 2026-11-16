namespace PetraERP.Application.AlAmeenMigration.Reconciliation;

/// <summary>Compares independently prepared, mapped balances without rounding or aggregating away individual identities.</summary>
public static class ReconciliationBalanceComparer
{
    public static ReconciliationDifferences Compare(ReconciliationBalances source, ReconciliationBalances target, AccountComparisonMode mode)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        Func<AccountBalance, IReadOnlyList<(string Name, decimal Value)>> accountMeasures = mode switch
        {
            AccountComparisonMode.Cutover => AccountNetMeasures,
            AccountComparisonMode.FullHistory => AccountMeasures,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        return new ReconciliationDifferences(
            CompareRows(source.Accounts, target.Accounts, x => x.Key, ValidateAccount, accountMeasures),
            CompareRows(source.Receivables, target.Receivables, x => x.Key, ValidateOpenItem, OpenItemMeasures),
            CompareRows(source.Payables, target.Payables, x => x.Key, ValidateOpenItem, OpenItemMeasures),
            CompareRows(source.Stock, target.Stock, x => x.Key, ValidateStock, StockMeasures));
    }

    private static List<BalanceDifference<TKey>> CompareRows<TRow, TKey>(
        IReadOnlyList<TRow> source,
        IReadOnlyList<TRow> target,
        Func<TRow, TKey> keySelector,
        Action<TKey> validateKey,
        Func<TRow, IReadOnlyList<(string Name, decimal Value)>> measures)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var sourceRows = Index(source, keySelector, validateKey);
        var targetRows = Index(target, keySelector, validateKey);
        var differences = new List<BalanceDifference<TKey>>();

        foreach (var (key, sourceRow) in sourceRows)
        {
            if (!targetRows.TryGetValue(key, out var targetRow))
            {
                foreach (var measure in measures(sourceRow))
                    differences.Add(new(key, measure.Name, measure.Value, null, BalanceDifferenceKind.MissingFromTarget));
                continue;
            }

            var sourceMeasures = measures(sourceRow);
            var targetMeasures = measures(targetRow);
            for (var i = 0; i < sourceMeasures.Count; i++)
            {
                if (sourceMeasures[i].Value != targetMeasures[i].Value)
                {
                    differences.Add(new(key, sourceMeasures[i].Name, sourceMeasures[i].Value, targetMeasures[i].Value, BalanceDifferenceKind.DifferentValue));
                }
            }
        }

        foreach (var (key, targetRow) in targetRows)
        {
            if (!sourceRows.ContainsKey(key))
                foreach (var measure in measures(targetRow))
                    differences.Add(new(key, measure.Name, null, measure.Value, BalanceDifferenceKind.MissingFromSource));
        }

        return differences;
    }

    private static Dictionary<TKey, TRow> Index<TRow, TKey>(IReadOnlyList<TRow> rows, Func<TRow, TKey> keySelector, Action<TKey> validateKey)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, TRow>();
        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            var key = keySelector(row);
            validateKey(key);
            if (!result.TryAdd(key, row)) throw new ArgumentException("Duplicate reconciliation balance key.", nameof(rows));
        }
        return result;
    }

    private static void ValidateAccount(AccountBalanceKey key)
    {
        if (key.BranchId == Guid.Empty || key.AccountId == Guid.Empty || !ValidCurrency(key.CurrencyCode)) throw new ArgumentException("Invalid account balance key.");
    }

    private static void ValidateOpenItem(OpenItemBalanceKey key)
    {
        if (key.BranchId == Guid.Empty || key.PartyId == Guid.Empty || key.AccountId == Guid.Empty || string.IsNullOrWhiteSpace(key.SourceDocumentId) || !ValidCurrency(key.CurrencyCode))
            throw new ArgumentException("Invalid open item balance key.");
    }

    private static void ValidateStock(StockBalanceKey key)
    {
        if (key.BranchId == Guid.Empty || key.ItemId == Guid.Empty || key.WarehouseId == Guid.Empty || key.UnitId == Guid.Empty) throw new ArgumentException("Invalid stock balance key.");
    }

    private static bool ValidCurrency(string value) => value is { Length: 3 } && value.All(c => c is >= 'A' and <= 'Z');

    private static IReadOnlyList<(string Name, decimal Value)> AccountMeasures(AccountBalance row) =>
        [(nameof(row.Debit), row.Debit), (nameof(row.Credit), row.Credit), (nameof(row.BaseDebit), row.BaseDebit), (nameof(row.BaseCredit), row.BaseCredit)];

    private static IReadOnlyList<(string Name, decimal Value)> AccountNetMeasures(AccountBalance row) =>
        [("TransactionNet", row.Debit - row.Credit), ("BaseNet", row.BaseDebit - row.BaseCredit)];

    private static IReadOnlyList<(string Name, decimal Value)> OpenItemMeasures(OpenItemBalance row) =>
        [(nameof(row.Outstanding), row.Outstanding), (nameof(row.BaseCarryingAmount), row.BaseCarryingAmount)];

    private static IReadOnlyList<(string Name, decimal Value)> StockMeasures(StockBalance row) =>
        [(nameof(row.Quantity), row.Quantity), (nameof(row.BaseValue), row.BaseValue)];
}
