using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Data;

namespace PetraERP.Desktop.Localization;

public sealed class StandardLabelConverter : IMultiValueConverter
{
    private static readonly ResourceManager Resources = new("PetraERP.Desktop.Localization.Strings", typeof(StandardLabelConverter).Assembly);
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var raw = values.FirstOrDefault();
        if (raw is null || raw == DependencyProperty.UnsetValue || raw == Binding.DoNothing)
            return string.Empty;
        var text = raw.ToString() ?? string.Empty;
        var language = values.Length > 1 && values[1] is string value && value == "en" ? "en" : "ar";
        var prefix = parameter is string key && (key.StartsWith("D_", StringComparison.Ordinal) || key == "BusinessOperationKind_") ? key : "UX_" + parameter + "_";
        return Resources.GetString(prefix + text, CultureInfo.GetCultureInfo(language)) ?? text;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
