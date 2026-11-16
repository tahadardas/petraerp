using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Data;
using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Inventory;

public sealed class InventoryEnumCaptionConverter : IValueConverter
{
    private static readonly ResourceManager Resources = new("PetraERP.Desktop.Localization.Strings", typeof(LocalizationService).Assembly);
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value == DependencyProperty.UnsetValue ? string.Empty : Resources.GetString($"{parameter}{value}", culture) ?? value.ToString() ?? string.Empty;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
