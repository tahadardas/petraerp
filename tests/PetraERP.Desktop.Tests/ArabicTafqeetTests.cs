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
    public void KnownCurrenciesResolveProperLabels()
    {
        var sar = ArabicTafqeet.ToWords(100.50m, "SAR");
        Assert.Contains("ريال سعودي", sar);
        Assert.Contains("هللة", sar);

        var usd = ArabicTafqeet.ToWords(50.25m, "USD");
        Assert.Contains("دولار أمريكي", usd);
        Assert.Contains("سنت", usd);
    }
}
