using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.GeneralLedger;

public partial class JournalsView : UserControl
{
    public JournalsView() => InitializeComponent();

    /// <summary>Double-click forwards to the open command; no business logic here.</summary>
    private void JournalOpened(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GeneralLedgerViewModel model && model.OpenJournalCommand.CanExecute(null)) _ = model.OpenJournalCommand.ExecuteAsync(null);
    }
}
