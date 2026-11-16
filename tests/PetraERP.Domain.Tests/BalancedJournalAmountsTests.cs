using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;

namespace PetraERP.Domain.Tests;

public sealed class BalancedJournalAmountsTests
{
    private static readonly CurrencyCode Currency = CurrencyCode.Create("USD");

    private static JournalLineAmounts Debit(decimal amount) =>
        JournalLineAmounts.Create(Money.Create(amount, Currency), Money.Create(0m, Currency));

    private static JournalLineAmounts Credit(decimal amount) =>
        JournalLineAmounts.Create(Money.Create(0m, Currency), Money.Create(amount, Currency));

    [Fact]
    public void ExactBalanceIsRequiredWithoutTolerance()
    {
        var valid = BalancedJournalAmounts.Create([Debit(0.1m), Debit(0.2m), Credit(0.3m)]);
        Assert.Equal(0.3m, valid.Total.Amount);
        Assert.Throws<ArgumentException>(() => BalancedJournalAmounts.Create([Debit(1m), Credit(0.9999m)]));
    }

    [Fact]
    public void DifferentCurrenciesCannotBalanceEachOther()
    {
        var foreignCredit = JournalLineAmounts.Create(Money.Create(0m, CurrencyCode.Create("SYP")), Money.Create(1m, CurrencyCode.Create("SYP")));
        Assert.Throws<ArgumentException>(() => BalancedJournalAmounts.Create([Debit(1m), foreignCredit]));
    }

    [Fact]
    public void EmptySingleOrNullLinesCannotPassValidation()
    {
        Assert.Throws<ArgumentNullException>(() => BalancedJournalAmounts.Create(null!));
        Assert.Throws<ArgumentException>(() => BalancedJournalAmounts.Create([]));
        Assert.Throws<ArgumentException>(() => BalancedJournalAmounts.Create([Debit(1m)]));
        Assert.Throws<ArgumentException>(() => BalancedJournalAmounts.Create([Debit(1m), null!]));
    }

    [Fact]
    public void AcceptedSnapshotCannotBeChangedThroughTheCallerList()
    {
        var source = new List<JournalLineAmounts> { Debit(1m), Credit(1m) };
        var accepted = BalancedJournalAmounts.Create(source);
        source[0] = Debit(5m);
        Assert.Equal(1m, accepted.Lines[0].Debit.Amount);
        var exposed = Assert.IsAssignableFrom<IList<JournalLineAmounts>>(accepted.Lines);
        Assert.Throws<NotSupportedException>(() => exposed[0] = Debit(5m));
    }

    [Fact]
    public void TotalsMustStayWithinDeclaredStorageBounds()
    {
        var max = FinancialPrecision.MaximumMoney;
        Assert.Throws<ArgumentOutOfRangeException>(() => BalancedJournalAmounts.Create(
            [Debit(max), Debit(0.0001m), Credit(max), Credit(0.0001m)]));
    }

    [Fact]
    public void GeneratedBalancedJournalsPreserveEveryLineAndExactTotal()
    {
        var random = new Random(772301);
        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var lines = new List<JournalLineAmounts>();
            var total = 0m;
            var count = random.Next(1, 12);
            for (var index = 0; index < count; index++)
            {
                var amount = random.Next(1, 100000000) / 10000m;
                total += amount;
                lines.Add(Debit(amount));
            }

            lines.Add(Credit(total));
            var journal = BalancedJournalAmounts.Create(lines);
            Assert.Equal(total, journal.Total.Amount);
            Assert.Equal(count + 1, journal.Lines.Count);
        }
    }
}
