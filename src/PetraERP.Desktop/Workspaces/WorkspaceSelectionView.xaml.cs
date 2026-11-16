using System.Windows.Controls;

namespace PetraERP.Desktop.Workspaces;

public partial class WorkspaceSelectionView : UserControl
{
    private void AdministratorPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is WorkspaceSelectionViewModel vm && sender is PasswordBox box) vm.AdministratorPassword = box.Password;
    }

    public WorkspaceSelectionView()
    {
        InitializeComponent();
    }
}
