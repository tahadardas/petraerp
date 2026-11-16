using System.Globalization;
using System.Windows.Data;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Tests;

public sealed class EqualityConverterTests
{
    [Fact]
    public void ConvertSingleValueMatchesParameter()
    {
        var converter = new EqualityConverter();
        var match = converter.Convert("Customer", typeof(bool), "Customer", CultureInfo.InvariantCulture);
        var mismatch = converter.Convert("Supplier", typeof(bool), "Customer", CultureInfo.InvariantCulture);

        Assert.Equal(true, match);
        Assert.Equal(false, mismatch);
    }

    [Fact]
    public void ConvertSingleValueHandlesNulls()
    {
        var converter = new EqualityConverter();
        Assert.Equal(true, converter.Convert((object?)null, typeof(bool), (object?)null, CultureInfo.InvariantCulture));
        Assert.Equal(false, converter.Convert("Customer", typeof(bool), (object?)null, CultureInfo.InvariantCulture));
        Assert.Equal(false, converter.Convert((object?)null, typeof(bool), "Customer", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBackReturnsParameterWhenTrueAndDoNothingWhenFalse()
    {
        var converter = new EqualityConverter();
        var resultTrue = converter.ConvertBack(true, typeof(string), "Customer", CultureInfo.InvariantCulture);
        var resultFalse = converter.ConvertBack(false, typeof(string), "Customer", CultureInfo.InvariantCulture);

        Assert.Equal("Customer", resultTrue);
        Assert.Equal(Binding.DoNothing, resultFalse);
    }

    [Fact]
    public void ConvertMultiValueComparesFirstTwoElements()
    {
        var converter = new EqualityConverter();
        var match = converter.Convert(["ABC", "ABC"], typeof(bool), (object?)null, CultureInfo.InvariantCulture);
        var mismatch = converter.Convert(["ABC", "XYZ"], typeof(bool), (object?)null, CultureInfo.InvariantCulture);
        var shortArray = converter.Convert(["ABC"], typeof(bool), (object?)null, CultureInfo.InvariantCulture);

        Assert.Equal(true, match);
        Assert.Equal(false, mismatch);
        Assert.Equal(false, shortArray);
    }
}
