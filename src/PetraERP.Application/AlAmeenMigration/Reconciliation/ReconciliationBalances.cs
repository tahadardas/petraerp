namespace PetraERP.Application.AlAmeenMigration.Reconciliation;

// Keys contain resolved Petra identities. An adapter must reject unresolved or ambiguous source mappings before comparison.
public readonly record struct AccountBalanceKey(Guid BranchId, Guid AccountId, string CurrencyCode);
public readonly record struct OpenItemBalanceKey(Guid BranchId, Guid PartyId, Guid AccountId, string SourceDocumentId, string CurrencyCode);
public readonly record struct StockBalanceKey(Guid BranchId, Guid ItemId, Guid WarehouseId, Guid UnitId);

public sealed record AccountBalance(AccountBalanceKey Key, decimal Debit, decimal Credit, decimal BaseDebit, decimal BaseCredit);
public sealed record OpenItemBalance(OpenItemBalanceKey Key, decimal Outstanding, decimal BaseCarryingAmount);
public sealed record StockBalance(StockBalanceKey Key, decimal Quantity, decimal BaseValue);

public sealed record ReconciliationBalances(
    IReadOnlyList<AccountBalance> Accounts,
    IReadOnlyList<OpenItemBalance> Receivables,
    IReadOnlyList<OpenItemBalance> Payables,
    IReadOnlyList<StockBalance> Stock);

public enum AccountComparisonMode
{
    Cutover = 1,
    FullHistory = 2,
}

public enum BalanceDifferenceKind
{
    MissingFromSource,
    MissingFromTarget,
    DifferentValue,
}

public sealed record BalanceDifference<TKey>(
    TKey Key,
    string Measure,
    decimal? Source,
    decimal? Target,
    BalanceDifferenceKind Kind)
    where TKey : notnull;

// An empty difference set only means the supplied rows match. It is not proof that either dataset is complete,
// frozen, independently produced or eligible for activation.
public sealed record ReconciliationDifferences(
    IReadOnlyList<BalanceDifference<AccountBalanceKey>> Accounts,
    IReadOnlyList<BalanceDifference<OpenItemBalanceKey>> Receivables,
    IReadOnlyList<BalanceDifference<OpenItemBalanceKey>> Payables,
    IReadOnlyList<BalanceDifference<StockBalanceKey>> Stock);
