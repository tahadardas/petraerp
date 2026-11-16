using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Moves keyboard focus to a named validation target when a view model rejects a command.
/// The monotonically increasing request value lets the same invalid field be focused on repeated Save attempts.
/// </summary>
public static class ValidationFocus
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(ValidationFocus), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(string), typeof(ValidationFocus), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RequestProperty = DependencyProperty.RegisterAttached(
        "Request", typeof(int), typeof(ValidationFocus), new PropertyMetadata(0, OnRequestChanged));

    public static string GetKey(DependencyObject element) => (string)element.GetValue(KeyProperty);
    public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);
    public static string GetTarget(DependencyObject element) => (string)element.GetValue(TargetProperty);
    public static void SetTarget(DependencyObject element, string value) => element.SetValue(TargetProperty, value);
    public static int GetRequest(DependencyObject element) => (int)element.GetValue(RequestProperty);
    public static void SetRequest(DependencyObject element, int value) => element.SetValue(RequestProperty, value);

    private static void OnRequestChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement root || (int)args.NewValue <= 0) return;
        root.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Focus(root));
    }

    private static void Focus(FrameworkElement root)
    {
        var target = GetTarget(root);
        if (string.IsNullOrWhiteSpace(target) || Find(root, target) is not FrameworkElement element) return;

        element.BringIntoView();
        if (element is LookupPicker picker)
        {
            picker.FocusSearch();
            return;
        }

        if (!element.Focus()) return;
        if (element is TextBox textBox) textBox.SelectAll();
    }

    private static DependencyObject? Find(DependencyObject root, string target)
    {
        if (string.Equals(GetKey(root), target, StringComparison.Ordinal)) return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var result = Find(VisualTreeHelper.GetChild(root, index), target);
            if (result is not null) return result;
        }

        return null;
    }
}
