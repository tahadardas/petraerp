using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Inventory;

public partial class ItemCardView : UserControl
{
    // Page positions of the page list (see ItemCardView.xaml): the Petra-specific pages follow the Al-Ameen ones.
    private const int AccountsPage = 10;
    private const int BarcodesPage = 11;

    public ItemCardView()
    {
        InitializeComponent();
    }

    /// <summary>"حسابات...": opens the item's accounting accounts and their inheritance.</summary>
    private void AccountsClicked(object sender, RoutedEventArgs e) => NavList.SelectedIndex = AccountsPage;

    /// <summary>The "..." beside a unit's barcode: every barcode of the item, with its unit and default flag.</summary>
    private void MoreBarcodesClicked(object sender, RoutedEventArgs e) => NavList.SelectedIndex = BarcodesPage;

    /// <summary>"إغلاق": the shell's normal document close (it asks before discarding unsaved changes).</summary>
    private void CloseClicked(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is DesktopViewModel shell) _ = shell.CloseActiveDocumentCommand.ExecuteAsync(null);
    }

    /// <summary>"استعراض...": picks the item picture. Only the path is stored; the file itself stays where it is.</summary>
    private void BrowseImageClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ItemCardViewModel model) return;
        var dialog = new OpenFileDialog
        {
            Title = "صورة المادة",
            Filter = "Images|*.bmp;*.jpg;*.jpeg;*.png;*.gif|All files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) model.DraftImagePath = dialog.FileName;
    }
}
