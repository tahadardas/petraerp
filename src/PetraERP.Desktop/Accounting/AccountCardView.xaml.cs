using System.Windows.Controls;
using System.Windows.Input;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Accounting;

/// <summary>Code-behind only forwards double-clicks to the view model's drill-down commands.</summary>
public partial class AccountCardView : UserControl
{
    public AccountCardView() => InitializeComponent();

    private void ChildOpened(object sender, MouseButtonEventArgs args)
    {
        if (DataContext is AccountCardViewModel model && ChildrenGrid.SelectedItem is AccountResponse child) model.OpenChildCommand.Execute(child);
    }

    private void MovementOpened(object sender, MouseButtonEventArgs args)
    {
        if (DataContext is AccountCardViewModel model && MovementsGrid.SelectedItem is AccountLedgerLineResponse line) model.OpenJournalCommand.Execute(line);
    }
}
