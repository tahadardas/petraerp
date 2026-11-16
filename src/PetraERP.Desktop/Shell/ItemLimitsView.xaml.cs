using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Shell;

public partial class ItemLimitsView : UserControl
{
    public ItemLimitsView() => InitializeComponent();

    private void RowOpened(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ItemLimitsViewModel model && model.OpenCommand.CanExecute(null)) model.OpenCommand.Execute(null);
    }
}
