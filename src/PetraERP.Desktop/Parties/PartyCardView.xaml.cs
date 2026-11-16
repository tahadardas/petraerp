using System.Windows;
using System.Windows.Controls;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Parties;

public partial class PartyCardView : UserControl
{
    private const int AccountsPage = 2;

    public PartyCardView()
    {
        InitializeComponent();
    }

    /// <summary>"حسابات...": the party's accounts, discount counter-account and VAT account.</summary>
    private void AccountsClicked(object sender, RoutedEventArgs e) => NavList.SelectedIndex = AccountsPage;

    /// <summary>"إغلاق": the shell's normal document close.</summary>
    private void CloseClicked(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is DesktopViewModel shell) _ = shell.CloseActiveDocumentCommand.ExecuteAsync(null);
    }
}
