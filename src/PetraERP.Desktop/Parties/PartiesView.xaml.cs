using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Parties;

public partial class PartiesView : UserControl
{
    public PartiesView()
    {
        InitializeComponent();
    }

    private void RowOpened(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.DependencyObject source && Controls.GridEntry.FindParent<DataGridRow>(source) is not null
            && DataContext is PartiesViewModel vm && vm.OpenCardCommand.CanExecute(null))
        {
            vm.OpenCardCommand.Execute(null);
        }
    }

}
