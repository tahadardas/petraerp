using PetraERP.Domain.Primitives;
using PetraERP.Domain.Sales;

namespace PetraERP.Domain.Tests;

public sealed class SalesReturnAllocationTests
{
    private static readonly SalesLineAmounts Sold = new(100.0000m, 10.0000m, 14.4000m, 5.0000m, 3.0000m, 61.0000m);

    [Fact]
    public void PartialReturnsAreProportionalAndTheLastReturnTakesTheExactRemainder()
    {
        // Three returns of 1 out of 3 units: 33.3333 + 33.3333 + 33.3334 = 100.0000.
        var first = SalesReturnAllocation.Allocate(3m, Sold, 0m, SalesLineAmounts.Zero, 1m);
        Assert.Equal(new SalesLineAmounts(33.3333m, 3.3333m, 4.8000m, 1.6666m, 1.0000m, 20.3333m), first);
        var second = SalesReturnAllocation.Allocate(3m, Sold, 1m, first, 1m);
        var both = first.Add(second);
        var last = SalesReturnAllocation.Allocate(3m, Sold, 2m, both, 1m);
        Assert.Equal(Sold, both.Add(last));
        Assert.Equal(33.3334m, last.SubTotal);
        Assert.Equal(Sold.LineTotal, first.LineTotal + second.LineTotal + last.LineTotal);
    }

    [Fact]
    public void TinyAmountsSplitManyTimesNeverLeaveANegativeRemainder()
    {
        var sold = new SalesLineAmounts(0.0002m, 0m, 0m, 0m, 0m, 0.0003m);
        var returned = SalesLineAmounts.Zero;
        for (var i = 0; i < 3; i++)
            returned = returned.Add(SalesReturnAllocation.Allocate(4m, sold, i, returned, 1m));
        var last = SalesReturnAllocation.Allocate(4m, sold, 3m, returned, 1m);
        Assert.True(last.SubTotal >= 0m && last.Cost >= 0m);
        Assert.Equal(sold, returned.Add(last));
    }

    [Fact]
    public void FullSingleReturnEqualsTheOriginalLine() =>
        Assert.Equal(Sold, SalesReturnAllocation.Allocate(2.5m, Sold, 0m, SalesLineAmounts.Zero, 2.5m));

    [Theory]
    [InlineData(1.0001, SalesReturnAllocation.OverReturn)]
    [InlineData(0, SalesReturnAllocation.QuantityInvalid)]
    [InlineData(-1, SalesReturnAllocation.QuantityInvalid)]
    [InlineData(0.00001, SalesReturnAllocation.QuantityInvalid)]
    public void OverReturnAndInvalidQuantitiesAreRejected(double quantity, string code)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SalesReturnAllocation.Allocate(3m, Sold, 2m, SalesLineAmounts.Zero, (decimal)quantity));
        Assert.Equal(code, RuleViolation.CodeOf(exception));
    }
}
