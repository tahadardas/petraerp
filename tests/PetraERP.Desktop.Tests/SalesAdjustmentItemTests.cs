using PetraERP.ApiClient;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Tests;

/// <summary>The discounts-and-additions row: percentage and amount follow each other against the lines' subtotal.</summary>
public sealed class SalesAdjustmentItemTests
{
    private static SalesAdjustmentItem Row(decimal basis, out Counter changes)
    {
        var counter = new Counter();
        changes = counter;
        return new SalesAdjustmentItem { Basis = () => basis, Changed = () => counter.Value++ };
    }

    private sealed class Counter { public int Value; }

    [Fact]
    public void TypingAPercentageComputesTheAmountFromTheSubtotal()
    {
        var row = Row(200m, out _);
        row.Percentage = "5";
        Assert.Equal("10", row.Amount);
        Assert.Equal(10m, row.AmountValue);
    }

    [Fact]
    public void TypingAnAmountComputesThePercentage()
    {
        var row = Row(200m, out _);
        row.Amount = "25";
        Assert.Equal("12.5", row.Percentage);
    }

    [Fact]
    public void WithoutASubtotalTheAmountIsKeptAndNoPercentageIsInvented()
    {
        var row = Row(0m, out _);
        row.Amount = "30";
        Assert.Equal(string.Empty, row.Percentage);
        Assert.Equal(30m, row.AmountValue);
    }

    [Fact]
    public void ARowEnteredAsAPercentageTracksTheSubtotalWhenTheLinesChange()
    {
        var subtotal = 200m;
        var row = new SalesAdjustmentItem { Basis = () => subtotal };
        row.Percentage = "10";
        Assert.Equal(20m, row.AmountValue);
        subtotal = 300m;
        row.Rebase();
        Assert.Equal(30m, row.AmountValue);
    }

    [Fact]
    public void LoadingAStoredRowKeepsItsAmountEvenBeforeTheLinesAreLoaded()
    {
        var row = Row(0m, out var changes);
        row.LoadValues(SalesInvoiceAdjustmentKind.Addition, Guid.NewGuid(), "25.0000", "12.5", "تغليف", null);
        Assert.Equal(25m, row.AmountValue);
        Assert.Equal("12.5", row.Percentage);
        Assert.Equal(0, changes.Value);
        Assert.True(row.IsComplete);
    }

    [Fact]
    public void ARowNeedsAnAccountAndAPositiveAmountToBeComplete()
    {
        var row = Row(100m, out _);
        Assert.True(row.IsBlank);
        row.Amount = "5";
        Assert.False(row.IsBlank);
        Assert.False(row.IsComplete);
        row.AccountId = Guid.NewGuid();
        Assert.True(row.IsComplete);
    }
}
