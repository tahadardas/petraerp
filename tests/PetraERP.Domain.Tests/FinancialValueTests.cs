using System.Globalization;
using PetraERP.Domain.FinancialValues;

namespace PetraERP.Domain.Tests;

public sealed class FinancialValueTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.Create("USD");
    private static readonly CurrencyCode Syp = CurrencyCode.Create("SYP");

    [Fact]
    public void MoneyAddsDecimalFractionsExactly()
    {
        var sum = Money.Create(0.1m, Usd).Add(Money.Create(0.2m, Usd));
        Assert.Equal(0.3m, sum.Amount);
        Assert.Equal(Usd, sum.Currency);
    }

    [Fact]
    public void MoneyRejectsMixedCurrencies()
    {
        Assert.Throws<InvalidOperationException>(() => Money.Create(1m, Usd).Add(Money.Create(1m, Syp)));
    }

    [Theory]
    [InlineData("1000000000000000")]
    [InlineData("-1000000000000000")]
    [InlineData("0.00001")]
    [InlineData("-0.00001")]
    public void MoneyRejectsUnrepresentableValues(string text)
    {
        var value = decimal.Parse(text, CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Create(value, Usd));
    }

    [Fact]
    public void MoneyAcceptsSignedBoundariesAndInsignificantZeroes()
    {
        Assert.Equal(FinancialPrecision.MaximumMoney, Money.Create(FinancialPrecision.MaximumMoney, Usd).Amount);
        Assert.Equal(-FinancialPrecision.MaximumMoney, Money.Create(-FinancialPrecision.MaximumMoney, Usd).Amount);
        Assert.Equal(1.23m, Money.Create(1.230000m, Usd).Amount);
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Create(FinancialPrecision.MaximumMoney, Usd).Add(Money.Create(0.0001m, Usd)));
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("ل.س")]
    [InlineData("١٢٣")]
    [InlineData(" USD")]
    public void CurrencyCodeRejectsInvalidShape(string value)
    {
        Assert.Throws<ArgumentException>(() => CurrencyCode.Create(value));
    }

    [Fact]
    public void CurrencyEqualityUsesValueAndMoneyRejectsNull()
    {
        Assert.Equal(Usd, CurrencyCode.Create("USD"));
        Assert.Equal(2m, Money.Create(1m, Usd).Add(Money.Create(1m, CurrencyCode.Create("USD"))).Amount);
        Assert.Throws<ArgumentNullException>(() => Money.Create(1m, null!));
        Assert.Throws<ArgumentNullException>(() => CurrencyCode.Create(null!));
    }

    [Theory]
    [InlineData("0.0000001")]
    [InlineData("10000000000000")]
    public void QuantityRejectsExcessPrecisionAndRange(string text)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Quantity.Create(decimal.Parse(text, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void QuantitySupportsExactSignedMovements()
    {
        Assert.Equal(-0.000001m, Quantity.Create(0.1m).Subtract(Quantity.Create(0.100001m)).Value);
        Assert.Equal(FinancialPrecision.MaximumQuantity, Quantity.Create(FinancialPrecision.MaximumQuantity).Value);
    }
}
