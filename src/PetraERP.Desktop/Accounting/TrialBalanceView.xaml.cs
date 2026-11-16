using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Accounting;

public partial class TrialBalanceView : UserControl
{
    public TrialBalanceView() => InitializeComponent();

    /// <summary>Double-click forwards to the drill-down command; no business logic here.</summary>
    private void RowOpened(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is TrialBalanceViewModel model && model.OpenLedgerCommand.CanExecute(null)) _ = model.OpenLedgerCommand.ExecuteAsync(null);
    }
}
