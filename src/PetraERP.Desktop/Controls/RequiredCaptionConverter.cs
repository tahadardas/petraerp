using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PetraERP.Desktop.Controls;

/// <summary>Conditional requirements use the same ViewModel flags as save validation.</summary>
public sealed class RequiredCaptionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] != DependencyProperty.UnsetValue ? values[0] + (values.Length > 1 && values[1] is true ? " *" : string.Empty) : string.Empty;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
