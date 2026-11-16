using PetraERP.Domain.Primitives;
using PetraERP.Domain.Purchases;

namespace PetraERP.Domain.Tests;

public sealed class PurchaseCostingTests
{
    [Fact]
    public void BaseCurrencyPurchaseCostsEqualLineNets()
    {
        var lines = new[] { PurchaseLineMoney.Create(2m, 1000m, 50m, 247m), PurchaseLineMoney.Create(1.5m, 10m, 0m, 0m) };
        var amounts = PurchaseCosting.Allocate(1m, lines);
        Assert.Equal(1950m, lines[0].Net); Assert.Equal(2197m, lines[0].Total);
        Assert.Equal(2212m, amounts.Gross); Assert.Equal(247m, amounts.Tax); Assert.Equal(1965m, amounts.Inventory);
        Assert.Equal([1950m, 15m], amounts.LineCosts);
    }

    [Fact]
    public void ForeignCurrencyRoundingIsAbsorbedSoJournalAndStockAgree()
    {
        // 3 lines of 0.3333 at a rate of 0.0333: each net rounds alone; the last line takes the remainder.
        var lines = Enumerable.Range(0, 3).Select(_ => PurchaseLineMoney.Create(1m, 0.3333m, 0m, 0.0001m)).ToArray();
        var amounts = PurchaseCosting.Allocate(0.0333m, lines);
        Assert.Equal(amounts.Gross, amounts.Tax + amounts.Inventory);
        Assert.Equal(amounts.Inventory, amounts.LineCosts.Sum());
        Assert.Equal(PurchaseCosting.ToBase(lines.Sum(l => l.Total), 0.0333m), amounts.Gross);
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(1, -1, 0, 0)]
    [InlineData(1, 10, 10.0001, 0)]
    [InlineData(1, 10, 0, -1)]
    [InlineData(0.00001, 10, 0, 0)]
    public void InvalidLinesAreRejected(double quantity, double price, double discount, double tax)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PurchaseLineMoney.Create((decimal)quantity, (decimal)price, (decimal)discount, (decimal)tax));
        Assert.Equal(PurchaseLineMoney.LineInvalid, RuleViolation.CodeOf(exception));
    }
}
