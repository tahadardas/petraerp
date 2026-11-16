using System.Globalization;
using PetraERP.Domain.FinancialValues;

namespace PetraERP.Domain.Tests;

public sealed class ExchangeRateTests
{
    private static readonly CurrencyCode Source = CurrencyCode.Create("USD");
    private static readonly CurrencyCode Target = CurrencyCode.Create("SYP");

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.000000001")]
    [InlineData("100000000000")]
    public void RateRejectsNonpositiveUnrepresentableValues(string text)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExchangeRate.Create(Source, Target,
            decimal.Parse(text, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ConversionUsesExplicitDirectionAndPreservesRate()
    {
        var rate = ExchangeRate.Create(Source, Target, 12345.67890123m);
        var result = rate.Convert(Money.Create(2m, Source), MidpointRounding.ToEven);
        Assert.Equal(24691.3578m, result.Amount);
        Assert.Equal(Target, result.Currency);
        Assert.Equal(12345.67890123m, rate.Value);
        Assert.Throws<ArgumentException>(() => rate.Convert(Money.Create(2m, Target), MidpointRounding.ToEven));
    }

    [Theory]
    [InlineData("0.0001", MidpointRounding.ToEven, "0.0000")]
    [InlineData("0.0003", MidpointRounding.ToEven, "0.0002")]
    [InlineData("-0.0003", MidpointRounding.ToEven, "-0.0002")]
    [InlineData("0.0001", MidpointRounding.AwayFromZero, "0.0001")]
    [InlineData("-0.0001", MidpointRounding.AwayFromZero, "-0.0001")]
    [InlineData("0.0001", MidpointRounding.ToZero, "0.0000")]
    [InlineData("-0.0001", MidpointRounding.ToNegativeInfinity, "-0.0001")]
    [InlineData("0.0001", MidpointRounding.ToNegativeInfinity, "0.0000")]
    [InlineData("0.0001", MidpointRounding.ToPositiveInfinity, "0.0001")]
    [InlineData("-0.0001", MidpointRounding.ToPositiveInfinity, "0.0000")]
    public void ConversionHonorsSignedRoundingPolicy(string text, MidpointRounding policy, string expected)
    {
        var rate = ExchangeRate.Create(Source, Target, 0.5m);
        var result = rate.Convert(Money.Create(decimal.Parse(text, CultureInfo.InvariantCulture), Source), policy);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), result.Amount);
    }

    [Fact]
    public void ConversionHandlesMaximumCoefficientsWithoutHiddenRounding()
    {
        var tinyRate = ExchangeRate.Create(Source, Target, 0.00000001m);
        Assert.Equal(10000000m, tinyRate.Convert(Money.Create(FinancialPrecision.MaximumMoney, Source), MidpointRounding.ToEven).Amount);
        var maximumRate = ExchangeRate.Create(Source, Target, FinancialPrecision.MaximumRate);
        Assert.Equal(10000000m, maximumRate.Convert(Money.Create(0.0001m, Source), MidpointRounding.ToEven).Amount);
        Assert.Throws<OverflowException>(() => maximumRate.Convert(Money.Create(FinancialPrecision.MaximumMoney, Source), MidpointRounding.ToEven));
    }

    [Fact]
    public void InvalidRoundingAndCurrencyIdentityAreRejected()
    {
        var rate = ExchangeRate.Create(Source, Target, 1m);
        Assert.Throws<ArgumentOutOfRangeException>(() => rate.Convert(Money.Create(1m, Source), (MidpointRounding)99));
        Assert.Throws<ArgumentException>(() => ExchangeRate.Create(Source, Source, 2m));
        Assert.Equal(1m, ExchangeRate.Create(Source, Source, 1m).Value);
        Assert.Equal(-12m, ExchangeRate.Create(Source, Source, 1m).Convert(Money.Create(-12m, Source), MidpointRounding.ToEven).Amount);
        Assert.Equal(0m, rate.Convert(Money.Create(0m, Source), MidpointRounding.AwayFromZero).Amount);
        Assert.Throws<ArgumentNullException>(() => ExchangeRate.Create(null!, Target, 1m));
        Assert.Throws<ArgumentNullException>(() => ExchangeRate.Create(Source, null!, 1m));
        Assert.Throws<ArgumentNullException>(() => rate.Convert(null!, MidpointRounding.ToEven));
    }

    [Fact]
    public void RandomizedConversionsMatchAnIndependentBoundedDecimalOracle()
    {
        var random = new Random(812761);
        foreach (var policy in Enum.GetValues<MidpointRounding>())
        {
            for (var iteration = 0; iteration < 2000; iteration++)
            {
                var amount = random.NextInt64(-1000000000, 1000000000) / 10000m;
                var rateValue = random.NextInt64(1, 100000000000) / 100000000m;
                // At most 20 significant digits: this bounded decimal product
                // is exact, independent of the production integer quantizer.
                var expected = decimal.Round(amount * rateValue, 4, policy);
                var actual = ExchangeRate.Create(Source, Target, rateValue)
                    .Convert(Money.Create(amount, Source), policy);
                Assert.Equal(expected, actual.Amount);
            }
        }
    }
}
