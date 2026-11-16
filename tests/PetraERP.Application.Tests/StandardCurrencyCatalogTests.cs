using PetraERP.Application.Currencies;

namespace PetraERP.Application.Tests;

public sealed class StandardCurrencyCatalogTests
{
    [Fact]
    public void CatalogOffersArabCurrenciesPlusUsdAndEurWithUniqueIsoCodes()
    {
        var codes = StandardCurrencyCatalog.All.Select(c => c.Code).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches("^[A-Z]{3}$", code));
        foreach (var expected in new[] { "SYP", "USD", "EUR", "JOD", "SAR", "AED", "EGP", "IQD", "LBP", "KWD", "MAD" })
            Assert.Contains(expected, codes);
        Assert.All(StandardCurrencyCatalog.All, c => Assert.False(string.IsNullOrWhiteSpace(c.NameArabic)));
    }

    [Theory]
    [InlineData("KWD", 3)]
    [InlineData("JOD", 3)]
    [InlineData("IQD", 3)]
    [InlineData("DJF", 0)]
    [InlineData("SYP", 2)]
    [InlineData("USD", 2)]
    public void DecimalPlacesFollowIsoMinorUnits(string code, int places) =>
        Assert.Equal(places, StandardCurrencyCatalog.Find(code)!.DecimalPlaces);

    [Fact]
    public void EveryCatalogDecimalPlaceCountIsSupportedByTheMoneyScale() =>
        Assert.All(StandardCurrencyCatalog.All, c => Assert.InRange(c.DecimalPlaces, 0, 4));

    [Fact]
    public void FindIgnoresCaseAndWhitespaceAndRejectsUnknownCodes()
    {
        Assert.Equal("USD", StandardCurrencyCatalog.Find(" usd ")?.Code);
        Assert.Null(StandardCurrencyCatalog.Find("لير"));
        Assert.Null(StandardCurrencyCatalog.Find(null));
    }
}
