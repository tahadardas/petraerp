using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Sales;
using Xunit;

namespace PetraERP.Desktop.Tests;

public sealed class CascadingDiscountTests
{
    [Theory]
    [InlineData("10", true)]
    [InlineData("10.5", true)]
    [InlineData("10%", true)]
    [InlineData("10+5", true)]
    [InlineData("10% + 5%", true)]
    [InlineData("10 + 5 + 2", true)]
    [InlineData("10% + 5% + 2%", true)]
    [InlineData("0", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("100", true)]
    [InlineData("100+10", true)]
    [InlineData("-5", false)]
    [InlineData("105", false)]
    [InlineData("10 + -2", false)]
    [InlineData("abc", false)]
    [InlineData("10 + xyz", false)]
    public void IsValidExpressionValidatesCorrectly(string? expression, bool expectedValid)
    {
        var isValid = CascadingDiscountCalculator.IsValidExpression(expression);
        Assert.Equal(expectedValid, isValid);

        var requiredInputValid = RequiredInput.DiscountExpression(expression);
        Assert.Equal(expectedValid, requiredInputValid);
    }

    [Fact]
    public void SingleTierDiscountCalculatesCorrectly()
    {
        const decimal baseAmount = 1000m;
        var discount = CascadingDiscountCalculator.CalculateTieredDiscountAmount(baseAmount, "10", out var effectivePct);

        Assert.Equal(100.0000m, discount);
        Assert.Equal(10.0000m, effectivePct);
    }

    [Fact]
    public void TwoTierDiscountCalculatesCorrectly()
    {
        // 1000 * 10% = 100 (remainder 900)
        // 900 * 5% = 45 (remainder 855)
        // Total discount = 145, effective % = 14.5%
        const decimal baseAmount = 1000m;
        var discount = CascadingDiscountCalculator.CalculateTieredDiscountAmount(baseAmount, "10+5", out var effectivePct);

        Assert.Equal(145.0000m, discount);
        Assert.Equal(14.5000m, effectivePct);
    }

    [Fact]
    public void ThreeTierDiscountCalculatesCorrectly()
    {
        // 1000 * 10% = 100 (remainder 900)
        // 900 * 5% = 45 (remainder 855)
        // 855 * 2% = 17.10 (remainder 837.90)
        // Total discount = 162.10, effective % = 16.21%
        const decimal baseAmount = 1000m;
        var discount = CascadingDiscountCalculator.CalculateTieredDiscountAmount(baseAmount, "10% + 5% + 2%", out var effectivePct);

        Assert.Equal(162.1000m, discount);
        Assert.Equal(16.2100m, effectivePct);
    }

    [Fact]
    public void EffectivePercentageFormulaMatchesCascadingMath()
    {
        // 1 - (1 - 0.10) * (1 - 0.05) * (1 - 0.02) = 1 - 0.8379 = 0.1621 (16.21%)
        var effectivePct = CascadingDiscountCalculator.CalculateEffectivePercentage("10 + 5 + 2");
        Assert.Equal(16.2100m, effectivePct);
    }

    [Fact]
    public void SalesInvoiceLineItemCascadingDiscountUpdatesLineProperties()
    {
        var line = new SalesInvoiceLineItem
        {
            Quantity = "2.0000",
            UnitPrice = "500.0000", // Subtotal = 1000.0000
            TaxPercentage = "0.0000"
        };

        // User enters multi-tier discount expression
        line.DiscountPercentage = "10 + 5 + 2";

        Assert.Equal("162.1000", line.DiscountAmount);
        Assert.Equal(16.2100m, line.EffectiveDiscountPercentage);
        Assert.Equal("16.2100", line.EffectiveDiscountPercentageText);
        Assert.Equal("16.21%", line.EffectiveDiscountDisplay);
        Assert.Equal("837.9000", line.LineTotal);

        // When quantity changes, discount recalculates dynamically
        line.Quantity = "4.0000"; // Subtotal = 2000.0000
        // Tier 1: 2000 * 10% = 200 (rem 1800)
        // Tier 2: 1800 * 5% = 90 (rem 1710)
        // Tier 3: 1710 * 2% = 34.20 (rem 1675.80)
        // Total discount = 324.20
        Assert.Equal("324.2000", line.DiscountAmount);
        Assert.Equal("1675.8000", line.LineTotal);
    }
}
