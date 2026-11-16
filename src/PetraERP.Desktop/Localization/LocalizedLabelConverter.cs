using System.Globalization;
using System.Windows.Data;

namespace PetraERP.Desktop.Localization;

public sealed class LocalizedLabelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 3 && values[2] is false && values[1] is string { Length: > 0 } english ? english : values[0];
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
