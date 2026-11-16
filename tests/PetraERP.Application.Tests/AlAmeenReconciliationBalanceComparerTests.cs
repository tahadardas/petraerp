using PetraERP.Application.AlAmeenMigration.Reconciliation;

namespace PetraERP.Application.Tests;

public sealed class AlAmeenReconciliationBalanceComparerTests
{
    private static readonly Guid Branch = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Account = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Party = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Item = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid Warehouse = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid Unit = Guid.Parse("00000000-0000-0000-0000-000000000006");

    [Fact]
    public void IdenticalDetailedBalancesHaveNoDifferences()
    {
        var balances = new ReconciliationBalances(
            [new(new(Branch, Account, "USD"), 2000m, 850m, 2000m, 850m)],
            [new(new(Branch, Party, Account, "INV-1", "USD"), 1150m, 1150m)],
            [],
            [new(new(Branch, Item, Warehouse, Unit), 80m, 1200m)]);

        var result = ReconciliationBalanceComparer.Compare(balances, balances, AccountComparisonMode.FullHistory);

        Assert.Empty(result.Accounts);
        Assert.Empty(result.Receivables);
        Assert.Empty(result.Payables);
        Assert.Empty(result.Stock);
    }

    [Fact]
    public void MatchingGrandTotalsDoNotHideDocumentOrStockDifferences()
    {
        var source = Empty() with
        {
            Receivables = [new(new(Branch, Party, Account, "A", "USD"), 100m, 100m), new(new(Branch, Party, Account, "B", "USD"), 50m, 50m)],
            Stock = [new(new(Branch, Item, Warehouse, Unit), 80m, 1200m)],
        };
        var target = Empty() with
        {
            Receivables = [new(new(Branch, Party, Account, "A", "USD"), 90m, 90m), new(new(Branch, Party, Account, "B", "USD"), 60m, 60m)],
            Stock = [new(new(Branch, Item, Warehouse, Unit), 79m, 1200m)],
        };

        var result = ReconciliationBalanceComparer.Compare(source, target, AccountComparisonMode.FullHistory);

        Assert.Equal(4, result.Receivables.Count);
        var difference = Assert.Single(result.Stock);
        Assert.Equal("Quantity", difference.Measure);
        Assert.Equal(80m, difference.Source);
        Assert.Equal(79m, difference.Target);
    }

    [Fact]
    public void MissingKeysAreNotTreatedAsZeroAndCurrencyIsPartOfIdentity()
    {
        var source = Empty() with { Accounts = [new(new(Branch, Account, "USD"), 0m, 0m, 0m, 0m)] };
        var target = Empty() with { Accounts = [new(new(Branch, Account, "EUR"), 0m, 0m, 0m, 0m)] };

        var result = ReconciliationBalanceComparer.Compare(source, target, AccountComparisonMode.FullHistory);

        Assert.Equal(8, result.Accounts.Count);
        Assert.Equal(4, result.Accounts.Count(x => x.Kind == BalanceDifferenceKind.MissingFromTarget && x.Target is null));
        Assert.Equal(4, result.Accounts.Count(x => x.Kind == BalanceDifferenceKind.MissingFromSource && x.Source is null));
    }

    [Fact]
    public void DuplicateKeysAndInvalidBranchAreRejected()
    {
        var row = new AccountBalance(new(Branch, Account, "USD"), 1m, 0m, 1m, 0m);
        Assert.Throws<ArgumentException>(() => ReconciliationBalanceComparer.Compare(Empty() with { Accounts = [row, row] }, Empty(), AccountComparisonMode.FullHistory));
        Assert.Throws<ArgumentException>(() => ReconciliationBalanceComparer.Compare(
            Empty() with { Accounts = [row with { Key = new(Guid.Empty, Account, "USD") }] }, Empty(), AccountComparisonMode.FullHistory));
    }

    [Fact]
    public void CutoverComparesNetWhileFullHistoryRetainsGrossMovement()
    {
        var source = Empty() with { Accounts = [new(new(Branch, Account, "USD"), 2000m, 850m, 2400m, 1020m)] };
        var opening = Empty() with { Accounts = [new(new(Branch, Account, "USD"), 1150m, 0m, 1380m, 0m)] };

        Assert.Empty(ReconciliationBalanceComparer.Compare(source, opening, AccountComparisonMode.Cutover).Accounts);
        Assert.Equal(4, ReconciliationBalanceComparer.Compare(source, opening, AccountComparisonMode.FullHistory).Accounts.Count);
    }

    [Fact]
    public void CutoverDetectsBaseCurrencyDifferenceAndRejectsUnknownMode()
    {
        var source = Empty() with { Accounts = [new(new(Branch, Account, "USD"), 50m, 100m, 60m, 120m)] };
        var target = Empty() with { Accounts = [new(new(Branch, Account, "USD"), 0m, 50m, 0m, 59m)] };

        var difference = Assert.Single(ReconciliationBalanceComparer.Compare(source, target, AccountComparisonMode.Cutover).Accounts);
        Assert.Equal("BaseNet", difference.Measure);
        Assert.Equal(-60m, difference.Source);
        Assert.Equal(-59m, difference.Target);
        Assert.Throws<ArgumentOutOfRangeException>(() => ReconciliationBalanceComparer.Compare(source, target, (AccountComparisonMode)99));
    }

    private static ReconciliationBalances Empty() => new([], [], [], []);
}
