using System.Windows;
using System.Windows.Controls;

namespace PetraERP.Desktop.Controls;

/// <summary>Visual bridge only. ViewModels clear transient secret properties on submission and session reset.</summary>
public static class PasswordInput
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached("Value", typeof(string), typeof(PasswordInput), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ValueChanged));
    private static readonly DependencyProperty UpdatingProperty = DependencyProperty.RegisterAttached("Updating", typeof(bool), typeof(PasswordInput));
    public static string GetValue(DependencyObject target) => (string?)target.GetValue(ValueProperty) ?? string.Empty;
    public static void SetValue(DependencyObject target, string value) => target.SetValue(ValueProperty, value);
    private static void ValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not PasswordBox box) return;
        box.PasswordChanged -= Changed;
        if (!(bool)box.GetValue(UpdatingProperty)) box.Password = args.NewValue as string ?? string.Empty;
        box.PasswordChanged += Changed;
    }
    private static void Changed(object sender, RoutedEventArgs args)
    {
        var box = (PasswordBox)sender;
        box.SetValue(UpdatingProperty, true);
        try { box.SetCurrentValue(ValueProperty, box.Password); }
        finally { box.SetValue(UpdatingProperty, false); }
    }
}
