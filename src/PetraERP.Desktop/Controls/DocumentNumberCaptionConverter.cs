using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PetraERP.Desktop.Controls;

/// <summary>Displays only a server-assigned number or a localized statement of when it will be assigned.</summary>
public sealed class DocumentNumberCaptionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var number = values.Length > 0 && values[0] is not null && values[0] != DependencyProperty.UnsetValue
            ? System.Convert.ToString(values[0], CultureInfo.InvariantCulture) : null;
        return !string.IsNullOrWhiteSpace(number) ? number : values.Length > 1 && values[1] != DependencyProperty.UnsetValue ? values[1] : string.Empty;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
