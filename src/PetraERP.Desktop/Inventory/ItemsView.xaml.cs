using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Inventory;

public partial class ItemsView : UserControl
{
    public ItemsView()
    {
        InitializeComponent();
    }

    private void RowOpened(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.DependencyObject source && Controls.GridEntry.FindParent<DataGridRow>(source) is not null
            && DataContext is ItemsViewModel vm && vm.OpenCardCommand.CanExecute(null))
        {
            vm.OpenCardCommand.Execute(null);
        }
    }

    private void OnGroupTreeSelectionChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ItemsViewModel vm)
        {
            vm.SelectGroupNode(e.NewValue as ItemGroupTreeNode);
        }
    }
}
