using System.Windows;
using System.Windows.Controls;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Inventory;

public partial class ItemGroupCardView : UserControl
{
    private const int AccountsPage = 1;

    public ItemGroupCardView()
    {
        InitializeComponent();
    }

    /// <summary>"حسابات...": the group's accounting accounts.</summary>
    private void AccountsClicked(object sender, RoutedEventArgs e) => NavList.SelectedIndex = AccountsPage;

    /// <summary>"إغلاق": the shell's normal document close.</summary>
    private void CloseClicked(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is DesktopViewModel shell) _ = shell.CloseActiveDocumentCommand.ExecuteAsync(null);
    }
}
