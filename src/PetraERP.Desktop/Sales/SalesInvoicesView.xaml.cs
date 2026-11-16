using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Sales;

public partial class SalesInvoicesView : UserControl
{
    public SalesInvoicesView()
    {
        InitializeComponent();
        // Loads the next server page when the operator scrolls near the end (or the first page does not fill the grid).
        InvoicesGrid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(GridScrolled));
    }

    private void GridScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not SalesInvoicesViewModel vm || !vm.HasMore) return;
        var nearEnd = e.ExtentHeight <= e.ViewportHeight + 1 || e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 40;
        if (nearEnd) _ = vm.LoadMoreAsync();
    }

    /// <summary>Column-header sort is executed by the server over the whole result; the grid never reorders its loaded rows locally.</summary>
    private void ColumnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not SalesInvoicesViewModel vm) return;
        var field = SalesInvoicesViewModel.ServerSortField(e.Column.SortMemberPath);
        if (field is null) return;
        var descending = e.Column.SortDirection == System.ComponentModel.ListSortDirection.Ascending;
        foreach (var column in InvoicesGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = descending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending;
        _ = vm.ApplySortAsync(field, descending);
    }

    private void RowOpened(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.DependencyObject source && Controls.GridEntry.FindParent<DataGridRow>(source) is not null
            && DataContext is SalesInvoicesViewModel vm && vm.OpenCardCommand.CanExecute(null))
        {
            vm.OpenCardCommand.Execute(null);
        }
    }

}
