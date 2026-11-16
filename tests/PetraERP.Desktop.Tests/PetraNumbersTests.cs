using System.Globalization;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Tests;

[Collection(NumberFormatterGroup.Name)]
public sealed class PetraNumbersTests
{
    [Fact]
    public void GroupThreeSeparatesEveryThreeDigits()
    {
        var formatted = PetraNumberFormat.FormatWith(1234567890m, NumberKind.Quantity, false, NumberDisplayMode.GroupThree, CultureInfo.InvariantCulture, DecimalDisplayMode.Dot);
        Assert.Equal("1,234,567,890", formatted);
    }

    [Fact]
    public void GroupTwoSeparatesEveryTwoDigits()
    {
        var formatted = PetraNumberFormat.FormatWith(1234567890m, NumberKind.Quantity, false, NumberDisplayMode.GroupTwo, CultureInfo.InvariantCulture, DecimalDisplayMode.Dot);
        Assert.Equal("12,34,56,78,90", formatted);
    }

    [Fact]
    public void NoGroupingProducesContiguousDigits()
    {
        var formatted = PetraNumberFormat.FormatWith(1234567890m, NumberKind.Quantity, false, NumberDisplayMode.NoGrouping, CultureInfo.InvariantCulture, DecimalDisplayMode.Dot);
        Assert.Equal("1234567890", formatted);
    }

    [Fact]
    public void InvariantModeProducesStandardCommaThreeGroupingAndPeriodDecimal()
    {
        var formatted = PetraNumberFormat.FormatWith(1234567.89m, NumberKind.Money, false, NumberDisplayMode.Invariant, CultureInfo.InvariantCulture, DecimalDisplayMode.Automatic);
        Assert.Equal("1,234,567.89", formatted);
    }

    [Fact]
    public void DecimalSeparatorModesWorkIndependentlyFromGrouping()
    {
        var dot = PetraNumberFormat.FormatWith(1234.56m, NumberKind.Money, false, NumberDisplayMode.GroupThree, CultureInfo.InvariantCulture, DecimalDisplayMode.Dot);
        Assert.Equal("1,234.56", dot);

        var comma = PetraNumberFormat.FormatWith(1234.56m, NumberKind.Money, false, NumberDisplayMode.NoGrouping, CultureInfo.InvariantCulture, DecimalDisplayMode.Comma);
        Assert.Equal("1234,56", comma);

        var arabic = PetraNumberFormat.FormatWith(1234.56m, NumberKind.Money, false, NumberDisplayMode.GroupThree, new CultureInfo("ar-SA"), DecimalDisplayMode.Arabic);
        Assert.Contains("٫", arabic);
    }

    [Fact]
    public void TryParseWireParsesInvariantDecimalsOnly()
    {
        Assert.True(PetraNumberFormat.TryParseWire("1234567.89", out var value));
        Assert.Equal(1234567.89m, value);

        Assert.True(PetraNumberFormat.TryParseWire("-500.25", out var negative));
        Assert.Equal(-500.25m, negative);

        Assert.False(PetraNumberFormat.TryParseWire("invalid", out _));
        Assert.False(PetraNumberFormat.TryParseWire(null, out _));
    }

    [Fact]
    public void NonNumericStringsAreNeverCorruptedByFormat()
    {
        // Account codes, document numbers, or tax identifiers passed to Format(wire) that are not valid decimals remain untouched
        var code = "ACC-1001-A";
        Assert.Equal("ACC-1001-A", PetraNumberFormat.Format(code));
    }

    [Fact]
    public void ArabicIndicInputDigitsParseCorrectlyToInvariantWireFormat()
    {
        // ٤٥٦.٧٨ -> 456.78
        var arabicInput = "٤٥٦٫٧٨";
        PetraNumberFormat.Configure(NumberDisplayMode.Culture, new CultureInfo("ar-SA"), DecimalDisplayMode.Arabic);
        try
        {
            Assert.True(PetraNumberFormat.TryParseInput(arabicInput, out var parsed));
            Assert.Equal(456.78m, parsed);
        }
        finally
        {
            PetraNumberFormat.Configure(NumberDisplayMode.Invariant, CultureInfo.InvariantCulture, DecimalDisplayMode.Automatic);
        }
    }

    [Theory]
    [InlineData("1,250,500.75", "1250500.75")]
    [InlineData("١٬٢٥٠٬٥٠٠٫٧٥", "1250500.75")]
    [InlineData("0.0001", "0.0001")]
    public void NumberConverterRoundTripsUserInputToInvariantDecimal(string input, string expected)
    {
        var converter = new NumberConverter();
        var value = Assert.IsType<decimal>(converter.ConvertBack(input, typeof(decimal), "rate", CultureInfo.InvariantCulture));
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), value);
    }

    [Fact]
    public void NumberConverterRejectsNegativeMoneyUnlessSignedIsExplicit()
    {
        var converter = new NumberConverter();
        Assert.Throws<FormatException>(() => converter.ConvertBack("-12,500.75", typeof(decimal), null, CultureInfo.InvariantCulture));
        Assert.Equal(-12500.75m, converter.ConvertBack("-12,500.75", typeof(decimal), "signed", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RequiredAmountAndConverterShareTheSameScaleContract()
    {
        Assert.True(RequiredInput.AmountText("1,250.50"));
        Assert.False(RequiredInput.AmountText("1.23456"));
        Assert.True(RequiredInput.AmountText("0.00000001", 8, true));
    }

    [Fact]
    public void ExchangeRateSupportsEightDecimalsRoundTrip()
    {
        var converter = new NumberConverter();
        var typed = "1.12345678";
        var parsed = Assert.IsType<decimal>(converter.ConvertBack(typed, typeof(decimal), "rate", CultureInfo.InvariantCulture));
        Assert.Equal(1.12345678m, parsed);

        var displayed = Assert.IsType<string>(converter.Convert(parsed, typeof(string), "rate", CultureInfo.InvariantCulture));
        Assert.Equal("1.12345678", displayed);

        // Wire conversion keeps all 8 digits without scientific notation or truncation
        var wire = PetraNumberFormat.ToWire(parsed);
        Assert.Equal("1.12345678", wire);
        Assert.True(PetraNumberFormat.TryParseWire(wire, out var reopened));
        Assert.Equal(parsed, reopened);
    }

    [Fact]
    public void ExchangeRateNineDecimalsRejectedWithValidation()
    {
        var converter = new NumberConverter();
        var invalid = "1.123456789"; // 9 decimals exceeds domain scale 8
        Assert.Throws<FormatException>(() => converter.ConvertBack(invalid, typeof(decimal), "rate", CultureInfo.InvariantCulture));

        var error = PetraNumberFormat.Validate(invalid, NumberKind.Rate, false, 8);
        Assert.Equal("UI_TooManyDecimals", error);
    }

    [Fact]
    public void NumberConverterDoesNotCoerceBlankToZero()
    {
        var converter = new NumberConverter();

        // Nullable decimal must yield null, not 0m
        var nullableResult = converter.ConvertBack("", typeof(decimal?), "rate", CultureInfo.InvariantCulture);
        Assert.Null(nullableResult);

        var whitespaceNullable = converter.ConvertBack("   ", typeof(decimal?), "rate", CultureInfo.InvariantCulture);
        Assert.Null(whitespaceNullable);

        // Non-nullable decimal without explicit zeroifempty must yield UnsetValue (preventing silent 0 override)
        var nonNullableResult = converter.ConvertBack("", typeof(decimal), "rate", CultureInfo.InvariantCulture);
        Assert.Equal(System.Windows.DependencyProperty.UnsetValue, nonNullableResult);

        // With zeroifempty option, explicit coercion is allowed
        var zeroResult = converter.ConvertBack("", typeof(decimal), "rate,zeroifempty", CultureInfo.InvariantCulture);
        Assert.Equal(0m, zeroResult);
    }

    [Fact]
    public void NegativeMoneyAllowedWhereLegitimatelyConfigured()
    {
        var converter = new NumberConverter();

        // Standard money rejects negative by default
        Assert.Throws<FormatException>(() => converter.ConvertBack("-500.50", typeof(decimal), "money", CultureInfo.InvariantCulture));

        // When 'negative' or 'signed' option is passed, negative money is allowed
        var neg1 = Assert.IsType<decimal>(converter.ConvertBack("-500.50", typeof(decimal), "negative", CultureInfo.InvariantCulture));
        Assert.Equal(-500.50m, neg1);

        var neg2 = Assert.IsType<decimal>(converter.ConvertBack("-500.50", typeof(decimal), "signed", CultureInfo.InvariantCulture));
        Assert.Equal(-500.50m, neg2);
    }

    [Fact]
    public void SourceValueEditStoredValueReopenedValueRoundTrip()
    {
        var converter = new NumberConverter();
        var sourceValue = 1.12345678m;

        // 1. Source value formatted for UI
        var displayed = converter.Convert(sourceValue, typeof(string), "rate", CultureInfo.InvariantCulture);
        Assert.Equal("1.12345678", displayed);

        // 2. User edits/types in UI
        var editedText = "1.12345678";
        var editedValue = Assert.IsType<decimal>(converter.ConvertBack(editedText, typeof(decimal), "rate", CultureInfo.InvariantCulture));
        Assert.Equal(sourceValue, editedValue);

        // 3. Stored to server wire format
        var wireStored = PetraNumberFormat.ToWire(editedValue);
        Assert.Equal("1.12345678", wireStored);

        // 4. Reopened from server wire format
        Assert.True(PetraNumberFormat.TryParseWire(wireStored, out var reopenedValue));
        Assert.Equal(sourceValue, reopenedValue);

        // 5. Re-rendered in UI
        var reopenedDisplay = converter.Convert(reopenedValue, typeof(string), "rate", CultureInfo.InvariantCulture);
        Assert.Equal("1.12345678", reopenedDisplay);
    }
}
