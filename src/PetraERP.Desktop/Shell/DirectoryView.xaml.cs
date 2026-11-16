using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Shell;

public partial class DirectoryView : UserControl
{
    public DirectoryView() => InitializeComponent();

    private void GroupSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is DirectoryViewModelBase model) model.SelectedNode = e.NewValue as DirectoryEntry;
    }

    private void CardOpened(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is DirectoryViewModelBase { SelectedRow: not null } model && model.OpenCommand.CanExecute(null)) model.OpenCommand.Execute(null);
    }
}
