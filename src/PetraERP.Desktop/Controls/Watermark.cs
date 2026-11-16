using System.Windows;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Attached property for placeholder / watermark hint text on TextBox and ComboBox input controls.
/// </summary>
public static class Watermark
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Watermark), new PropertyMetadata(null));

    public static string? GetText(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(TextProperty);
    }

    public static void SetText(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextProperty, value);
    }
}
