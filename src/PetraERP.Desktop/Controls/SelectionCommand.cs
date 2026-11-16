using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Controls;

public static class SelectionCommand
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached("Command", typeof(ICommand), typeof(SelectionCommand), new PropertyMetadata(null, Changed));
    public static ICommand? GetCommand(DependencyObject target) => (ICommand?)target.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject target, ICommand? command) => target.SetValue(CommandProperty, command);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ComboBox box) return;
        box.SelectionChanged -= Selected; if (args.NewValue is ICommand) box.SelectionChanged += Selected;
    }
    private static void Selected(object sender, SelectionChangedEventArgs args)
    {
        var box = (ComboBox)sender;
        // A OneWay binding refresh must not be mistaken for an explicit user selection.
        if (!box.IsKeyboardFocusWithin && !box.IsDropDownOpen) return;
        var command = GetCommand(box);
        if (command?.CanExecute(box.SelectedItem) == true) command.Execute(box.SelectedItem);
    }
}
