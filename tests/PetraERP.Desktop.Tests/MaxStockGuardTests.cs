using PetraERP.Desktop.Inventory;

namespace PetraERP.Desktop.Tests;

/// <summary>The Al-Ameen maximum-stock warning: advisory, never triggered without a maximum.</summary>
public sealed class MaxStockGuardTests
{
    [Theory]
    [InlineData(90, 10, 100, false)]
    [InlineData(90, 11, 100, true)]
    [InlineData(0, 101, 100, true)]
    [InlineData(500, 500, 0, false)]
    public void WarnsOnlyWhenTheMaximumIsSetAndExceeded(int current, int incoming, int maximum, bool expected) =>
        Assert.Equal(expected, MaxStockGuard.Exceeds(current, incoming, maximum));

    [Fact]
    public void NeverWarnsWithoutAMaximum() => Assert.False(MaxStockGuard.Exceeds(500m, 500m, null));
}
