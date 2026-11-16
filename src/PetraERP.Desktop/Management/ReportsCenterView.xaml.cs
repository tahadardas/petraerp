using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Management;

public partial class ReportsCenterView : UserControl
{
    public ReportsCenterView() => InitializeComponent();

    /// <summary>TreeView.SelectedItem cannot be bound: a selected report becomes the page's selection (a menu group clears it).</summary>
    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> args)
    {
        if (DataContext is ReportsCenterViewModel model) model.SelectedEntry = args.NewValue as ReportEntryRow;
    }

    /// <summary>Double-click opens the selected report when it is openable (the same command as the button).</summary>
    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (DataContext is ReportsCenterViewModel model && ReportTree.SelectedItem is ReportEntryRow { IsOpenable: true } && model.OpenCommand.CanExecute(null))
            model.OpenCommand.Execute(null);
    }
}
