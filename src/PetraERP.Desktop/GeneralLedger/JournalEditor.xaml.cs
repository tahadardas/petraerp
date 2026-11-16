using System.Windows.Controls;

namespace PetraERP.Desktop.GeneralLedger;

public partial class JournalEditor : UserControl
{
    private readonly HashSet<ValidationError> errors = [];
    public JournalEditor() => InitializeComponent();
    private void InputValidationChanged(object sender, ValidationErrorEventArgs args)
    {
        if (args.Action == ValidationErrorEventAction.Added) errors.Add(args.Error); else errors.Remove(args.Error);
        if (DataContext is GeneralLedgerViewModel model) model.HasInputErrors = errors.Count > 0;
    }
}
