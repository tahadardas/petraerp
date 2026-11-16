using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PetraERP.Desktop.Accounting;

/// <summary>Code-behind only maps pointer gestures onto the selection; actions stay view-model commands.</summary>
public partial class CostCentersView : UserControl
{
    public CostCentersView() => InitializeComponent();

    /// <summary>Right-click selects the row under the pointer first, so context-menu actions never target the previous row.</summary>
    private void RowRightClicked(object sender, MouseButtonEventArgs args)
    {
        for (var element = args.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is DataGridRow row) { if (sender is DataGrid grid) grid.SelectedItem = row.Item; else row.IsSelected = true; row.Focus(); return; }
        }
    }

    private void OnCostCenterTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is CostCentersViewModel vm && e.NewValue is CostCenterTreeNode node)
        {
            vm.SelectTreeNode(node);
        }
    }
}
