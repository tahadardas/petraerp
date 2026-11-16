using System.Globalization;
using PetraERP.Domain.FinancialValues;

namespace PetraERP.Domain.Tests;

public sealed class FinancialFormattingTests
{
    [Fact]
    public void InvariantFinancialTextIsStableUnderArabicCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SY");
            var usd = CurrencyCode.Create("USD");
            Assert.Equal("1234.5600", Money.Create(1234.56m, usd).ToInvariantString());
            Assert.Equal("-0.000001", Quantity.Create(-0.000001m).ToInvariantString());
            Assert.Equal("1.23456789", ExchangeRate.Create(usd, CurrencyCode.Create("SYP"), 1.23456789m).ToInvariantString());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
