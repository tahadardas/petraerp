using PetraERP.Desktop.Controls;
using Xunit;

namespace PetraERP.Desktop.Tests;

public class ArabicTafqeetTests
{
    [Fact]
    public void ZeroAmountReturnsZeroTafqeet()
    {
        var text = ArabicTafqeet.ToWords(0m, "SYP");
        Assert.Equal("فقط صفر ليرة سورية لا غير", text);

        var generic = ArabicTafqeet.ToWords(0m, "");
        Assert.Equal("فقط صفر لا غير", generic);
    }

    [Fact]
    public void BasicIntegersAreCorrectlyWritten()
    {
        Assert.Contains("واحد", ArabicTafqeet.ToWords(1m));
        Assert.Contains("اثنان", ArabicTafqeet.ToWords(2m));
        Assert.Contains("خمسة عشر", ArabicTafqeet.ToWords(15m));
        Assert.Contains("خمسة وعشرون", ArabicTafqeet.ToWords(25m));
        Assert.Contains("مائة", ArabicTafqeet.ToWords(100m));
        Assert.Contains("مائة وخمسة", ArabicTafqeet.ToWords(105m));
        Assert.Contains("مائة وخمسون", ArabicTafqeet.ToWords(150m));
    }

    [Fact]
    public void ThousandsAndMillionsScalesAreAccurate()
    {
        Assert.Contains("ألف", ArabicTafqeet.ToWords(1000m));
        Assert.Contains("ألفان", ArabicTafqeet.ToWords(2000m));
        Assert.Contains("خمسة آلاف", ArabicTafqeet.ToWords(5000m));
        Assert.Contains("مائة وخمسون ألف", ArabicTafqeet.ToWords(150000m));
        Assert.Contains("مليون ومائتان وخمسون ألف وخمسمائة", ArabicTafqeet.ToWords(1250500m));
        Assert.Contains("مليار", ArabicTafqeet.ToWords(1000000000m));
    }

    [Fact]
    public void DecimalFractionsAndCurrencyAreCorrectlyAppended()
    {
        var text = ArabicTafqeet.ToWords(150000.75m, "SYP");
        Assert.StartsWith("فقط ", text);
        Assert.Contains("مائة وخمسون ألف ليرة سورية", text);
        Assert.Contains("وخمسة وسبعون قرشاً", text);
        Assert.EndsWith(" لا غير", text);
    }

    [Fact]
    public void NegativeAmountPrependsSalib()
    {
        var text = ArabicTafqeet.ToWords(-2500m, "USD");
        Assert.StartsWith("سالب فقط ", text);
        Assert.Contains("ألفان وخمسمائة دولار أمريكي", text);
        Assert.EndsWith(" لا غير", text);
    }

    [Fact]
    public void ThreeDecimalCurrenciesReadThousandthsAsSubUnits()
    {
        var text = ArabicTafqeet.ToWords(1.234m, "KWD");
        Assert.Contains("واحد دينار كويتي", text);
        Assert.Contains("ومائتان وأربعة وثلاثون فلساً", text);
    }

    [Fact]
    public void RoundingCarriesIntoTheIntegralPartAndNeverReadsHundredSubUnits()
    {
        var carried = ArabicTafqeet.ToWords(1.999m, "USD");
        Assert.Contains("اثنان دولار أمريكي", carried);
        Assert.DoesNotContain("سنت", carried);

        Assert.Equal("فقط صفر دولار أمريكي لا غير", ArabicTafqeet.ToWords(0.004m, "USD"));
    }

    [Fact]
    public void OutOfRangeAmountsThrowButDisplayVariantReturnsEmpty()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArabicTafqeet.ToWords(1_000_000_000_000_000m, "USD"));
        Assert.Equal(string.Empty, ArabicTafqeet.ToWordsOrEmpty(79_000_000_000_000_000_000m, "USD"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArabicTafqeet.ToWords(1m, "USD", null, 4));
    }

    [Fact]
    public void SubUnitEndingWithTaMarbutaTakesTanweenNotAlef()
    {
        Assert.Contains("خمسون هللةً", ArabicTafqeet.ToWords(100.50m, "SAR"));
    }

    [Fact]
    public void KnownCurrenciesResolveProperLabels()
    {
        var sar = ArabicTafqeet.ToWords(100.50m, "SAR");
        Assert.Contains("ريال سعودي", sar);
        // Ordinal: the sub-unit now carries a tanween, which culture-aware matching treats as part of the preceding letter.
        Assert.Contains("هللة", sar, StringComparison.Ordinal);

        var usd = ArabicTafqeet.ToWords(50.25m, "USD");
        Assert.Contains("دولار أمريكي", usd);
        Assert.Contains("سنت", usd);
    }
}
