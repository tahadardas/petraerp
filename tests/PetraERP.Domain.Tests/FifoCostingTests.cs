using PetraERP.Domain.Inventory;

namespace PetraERP.Domain.Tests;

public sealed class FifoCostingTests
{
    private static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7(), C = Guid.CreateVersion7();

    private static FifoLayerBalance[] Layers() =>
    [
        new(A, 10m, 50m),
        new(B, 10m, 70m),
        new(C, 10m, 90m),
    ];

    [Fact]
    public void IssueOfFifteenTakesOldestLayersAndCostsEightyFive()
    {
        Assert.True(FifoCosting.TryIssue(Layers(), 15m, out var slices, out var total));
        Assert.Equal(85m, total);
        Assert.Equal(new[] { (A, 10m, 50m), (B, 5m, 35m) }, slices.Select(s => (s.LayerId, s.Quantity, s.Cost)).ToArray());
    }

    [Fact]
    public void WeightedAverageOfTheSameStockCostsOneHundredFive()
    {
        Assert.True(WeightedAverageCosting.TryValue(30m, 210m, 15m, out var wac));
        Assert.Equal(105m, wac);
    }

    [Fact]
    public void SecondIssueContinuesFromTheRemainderOfThePartlyConsumedLayer()
    {
        var afterFirst = new FifoLayerBalance[] { new(B, 5m, 35m), new(C, 10m, 90m) };
        Assert.True(FifoCosting.TryIssue(afterFirst, 10m, out var slices, out var total));
        Assert.Equal(35m + 45m, total);
        Assert.Equal(2, slices.Count);
    }

    [Fact]
    public void ExhaustingALayerReleasesItsExactRemainingValueWithoutRoundingResidue()
    {
        var layers = new FifoLayerBalance[] { new(A, 3m, 10m) };
        Assert.True(FifoCosting.TryIssue(layers, 1m, out _, out var first));
        Assert.Equal(3.3333m, first);
        Assert.True(FifoCosting.TryIssue([new FifoLayerBalance(A, 2m, 10m - first)], 2m, out _, out var rest));
        Assert.Equal(10m, first + rest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public void InvalidOrExcessiveQuantityIsRejected(int quantity) =>
        Assert.False(FifoCosting.TryIssue(Layers(), quantity, out var slices, out _) || slices.Count > 0);

    [Fact]
    public void QuantityWithMoreThanFourDecimalsIsRejected() =>
        Assert.False(FifoCosting.TryIssue(Layers(), 1.00001m, out _, out _));
}
