using PetraERP.Desktop.Controls;
using Xunit;

namespace PetraERP.Desktop.Tests;

public class PetraMathEvaluatorTests
{
    [Fact]
    public void BasicOperationsEvaluateCorrectly()
    {
        Assert.True(PetraMathEvaluator.TryEvaluate("10*5", out var mul));
        Assert.Equal(50m, mul);

        Assert.True(PetraMathEvaluator.TryEvaluate("100+25", out var add));
        Assert.Equal(125m, add);

        Assert.True(PetraMathEvaluator.TryEvaluate("500/2", out var div));
        Assert.Equal(250m, div);

        Assert.True(PetraMathEvaluator.TryEvaluate("20-5", out var sub));
        Assert.Equal(15m, sub);
    }

    [Fact]
    public void OperatorPrecedenceAndParenthesesAreRespected()
    {
        Assert.True(PetraMathEvaluator.TryEvaluate("10+5*2", out var precedence));
        Assert.Equal(20m, precedence);

        Assert.True(PetraMathEvaluator.TryEvaluate("(10+5)*2", out var parens));
        Assert.Equal(30m, parens);

        Assert.True(PetraMathEvaluator.TryEvaluate("100 - (20 + 30) / 2", out var complex));
        Assert.Equal(75m, complex);
    }

    [Fact]
    public void DecimalsAndAlternativeGlyphsAreHandled()
    {
        Assert.True(PetraMathEvaluator.TryEvaluate("1.5*10", out var dec));
        Assert.Equal(15m, dec);

        Assert.True(PetraMathEvaluator.TryEvaluate("10×5", out var cross));
        Assert.Equal(50m, cross);

        Assert.True(PetraMathEvaluator.TryEvaluate("50÷2", out var obelus));
        Assert.Equal(25m, obelus);

        Assert.True(PetraMathEvaluator.TryEvaluate("١٠ * ٥", out var arabicDigits));
        Assert.Equal(50m, arabicDigits);
    }

    [Fact]
    public void InvalidExpressionsAndDivisionByZeroSafelyFail()
    {
        Assert.False(PetraMathEvaluator.TryEvaluate("10/0", out _));
        Assert.False(PetraMathEvaluator.TryEvaluate("10+", out _));
        Assert.False(PetraMathEvaluator.TryEvaluate("abc", out _));
        Assert.False(PetraMathEvaluator.TryEvaluate("(10+5", out _));
        Assert.False(PetraMathEvaluator.TryEvaluate("", out _));
        Assert.False(PetraMathEvaluator.TryEvaluate(null, out _));
    }

    [Fact]
    public void ContainsOperatorDistinguishesFormulasFromStandardNegatives()
    {
        Assert.True(PetraMathEvaluator.ContainsOperator("10*5"));
        Assert.True(PetraMathEvaluator.ContainsOperator("10+5"));
        Assert.True(PetraMathEvaluator.ContainsOperator("100 - 25"));
        Assert.True(PetraMathEvaluator.ContainsOperator("(10+5)"));

        Assert.False(PetraMathEvaluator.ContainsOperator("-100"));
        Assert.False(PetraMathEvaluator.ContainsOperator("(100)"));
        Assert.False(PetraMathEvaluator.ContainsOperator("1234.56"));
    }

    private static bool Eval(string text, NumberDisplayMode mode, DecimalDisplayMode decimals, out decimal value) =>
        PetraMathEvaluator.TryEvaluateWith(text, mode, System.Globalization.CultureInfo.InvariantCulture, decimals, out value);

    [Theory]
    [InlineData("1,000.5+2", "1002.5")]
    [InlineData("١٬٠٠٠٫٥+٢", "1002.5")]
    [InlineData("1,000 * 2", "2000")]
    [InlineData("(1,500.25 - 0.25) / 2", "750")]
    public void GroupingCommaAndDecimalPointFollowTheSettings(string expression, string expected)
    {
        Assert.True(Eval(expression, NumberDisplayMode.Invariant, DecimalDisplayMode.Automatic, out var value));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData("1,5+1")]
    [InlineData("1,00+1")]
    [InlineData("1,0000+1")]
    [InlineData("1.5.5+1")]
    [InlineData("1,000,+1")]
    public void AmbiguousOrMalformedSeparatorsAreRefusedNotGuessed(string expression)
    {
        Assert.False(Eval(expression, NumberDisplayMode.Invariant, DecimalDisplayMode.Automatic, out _));
    }

    [Fact]
    public void DecimalCommaSettingMakesCommaTheDecimalSeparator()
    {
        Assert.True(Eval("1,5*2", NumberDisplayMode.NoGrouping, DecimalDisplayMode.Comma, out var value));
        Assert.Equal(3m, value);
    }

    [Fact]
    public void PetraNumberFormatTryParseInputEvaluatesInlineExpressions()
    {
        Assert.True(PetraNumberFormat.TryParseInput("25*4", out var val));
        Assert.Equal(100m, val);

        Assert.True(PetraNumberFormat.TryParseInput("150 + 50", out var sum));
        Assert.Equal(200m, sum);
    }
}
