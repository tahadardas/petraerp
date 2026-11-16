using PetraERP.Domain.FinancialValues;

namespace PetraERP.Domain.Tests;

public sealed class CurrencyRoundingTests
{
    [Theory]
    [InlineData(2.5, 0, CurrencyRoundingMode.HalfAwayFromZero, 3)]
    [InlineData(-2.5, 0, CurrencyRoundingMode.HalfAwayFromZero, -3)]
    [InlineData(2.5, 0, CurrencyRoundingMode.HalfToEven, 2)]
    [InlineData(3.5, 0, CurrencyRoundingMode.HalfToEven, 4)]
    [InlineData(2.999, 0, CurrencyRoundingMode.TowardZero, 2)]
    [InlineData(-2.999, 0, CurrencyRoundingMode.TowardZero, -2)]
    [InlineData(2.001, 0, CurrencyRoundingMode.AwayFromZero, 3)]
    [InlineData(-2.001, 0, CurrencyRoundingMode.AwayFromZero, -3)]
    [InlineData(2.0, 0, CurrencyRoundingMode.AwayFromZero, 2)]
    [InlineData(1.2345, 2, CurrencyRoundingMode.HalfAwayFromZero, 1.23)]
    [InlineData(1.2351, 2, CurrencyRoundingMode.AwayFromZero, 1.24)]
    [InlineData(1.2351, 3, CurrencyRoundingMode.TowardZero, 1.235)]
    public void RoundsExactlyPerMode(double amount, int places, CurrencyRoundingMode mode, double expected) =>
        Assert.Equal((decimal)expected, CurrencyRounding.Round((decimal)amount, places, mode));

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void RejectsUnsupportedDecimalPlaces(int places) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CurrencyRounding.Round(1m, places, CurrencyRoundingMode.HalfAwayFromZero));

    [Fact]
    public void RejectsUnknownMode() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CurrencyRounding.Round(1m, 2, (CurrencyRoundingMode)99));

    [Fact]
    public void SupportedPlacesMatchMoneyScale()
    {
        Assert.True(CurrencyRounding.IsValidPlaces(0));
        Assert.True(CurrencyRounding.IsValidPlaces(FinancialPrecision.MoneyScale));
        Assert.False(CurrencyRounding.IsValidPlaces(FinancialPrecision.MoneyScale + 1));
    }
}
