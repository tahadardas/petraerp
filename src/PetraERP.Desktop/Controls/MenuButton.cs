using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Makes a button that carries a <see cref="ContextMenu"/> open it on an ordinary left click, the way Al-Ameen's
/// "عمليات / عرض / تقارير / الوثائق / عمليات الضرائب / مزيد" buttons do. Without this the menu only opens on right click,
/// so the button looks dead. The menu opens above the bar (the bars sit at the bottom edge of the window).
/// </summary>
public static class MenuButton
{
    public static readonly DependencyProperty OpensMenuProperty = DependencyProperty.RegisterAttached(
        "OpensMenu", typeof(bool), typeof(MenuButton), new PropertyMetadata(false, OnOpensMenuChanged));

    public static bool GetOpensMenu(DependencyObject element) { ArgumentNullException.ThrowIfNull(element); return (bool)element.GetValue(OpensMenuProperty); }
    public static void SetOpensMenu(DependencyObject element, bool value) { ArgumentNullException.ThrowIfNull(element); element.SetValue(OpensMenuProperty, value); }

    private static void OnOpensMenuChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ButtonBase button) return;
        button.Click -= OnClick;
        if ((bool)args.NewValue) button.Click += OnClick;
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase { ContextMenu: { } menu } button) return;
        if (menu.IsOpen) { menu.IsOpen = false; return; }
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Top;
        menu.DataContext = button.DataContext;
        menu.FlowDirection = button.FlowDirection;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
