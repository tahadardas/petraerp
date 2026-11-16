using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Accounting;

public partial class GeneralJournalView : UserControl
{
    public GeneralJournalView() => InitializeComponent();

    /// <summary>Double-click forwards to the drill-down command; no business logic here.</summary>
    private void RowOpened(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GeneralJournalViewModel model && model.OpenJournalCommand.CanExecute(null)) _ = model.OpenJournalCommand.ExecuteAsync(null);
    }
}
