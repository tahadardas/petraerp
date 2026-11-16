using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Patterns;

public partial class DocumentPatternsView : UserControl
{
    public DocumentPatternsView()
    {
        InitializeComponent();
    }

    private void RowOpened(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.DependencyObject source && Controls.GridEntry.FindParent<DataGridRow>(source) is not null
            && DataContext is DocumentPatternsViewModel vm && vm.OpenCardCommand.CanExecute(null))
        {
            vm.OpenCardCommand.Execute(null);
        }
    }

}
