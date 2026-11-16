using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PetraERP.Desktop.Accounting;

/// <summary>Code-behind only maps pointer gestures and focus onto view-model commands.</summary>
public partial class ChartOfAccountsView : UserControl
{
    private ChartOfAccountsViewModel? model;

    public ChartOfAccountsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (model is not null) { model.EditRequested -= FocusEditor; model.DeleteRequested -= FocusDeleteReason; }
            model = DataContext as ChartOfAccountsViewModel;
            if (model is not null) { model.EditRequested += FocusEditor; model.DeleteRequested += FocusDeleteReason; }
        };
        Unloaded += (_, _) => { if (model is not null) { model.EditRequested -= FocusEditor; model.DeleteRequested -= FocusDeleteReason; } model = null; };
    }

    /// <summary>Right-click selects the clicked node before the context menu opens, so every action targets that node.</summary>
    private void RowRightClicked(object sender, MouseButtonEventArgs args)
    {
        for (var element = args.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            // Replace the selection (not add to it): with extended selection, IsSelected alone leaves SelectedItem on the old row.
            if (element is DataGridRow row) { if (sender is DataGrid grid) grid.SelectedItem = row.Item; else row.IsSelected = true; row.Focus(); return; }
        }
    }

    private void RowOpened(object sender, MouseButtonEventArgs args)
    {
        if (model?.OpenCardCommand.CanExecute(null) == true) _ = model.OpenCardCommand.ExecuteAsync(null);
    }

    private void FocusDeleteReason(object? sender, EventArgs args) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => DeleteReasonBox.Focus());

    private void FocusEditor(object? sender, EventArgs args) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { NameArabicBox.Focus(); NameArabicBox.SelectAll(); });
}
