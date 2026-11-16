using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Printing;

namespace PetraERP.Desktop.Treasury;

public sealed partial class VouchersViewModel
{
    private RelayCommand? printCommand;

    /// <summary>Raised with the document to preview or print; the shell adds the company context and opens the print window.</summary>
    public event Action<PrintJob>? PrintRequested;

    /// <summary>Preview / print of the saved voucher (Ctrl+P); an unsaved or being-edited one must be saved first.</summary>
    public RelayCommand PrintCommand => printCommand ??= new RelayCommand(Print);

    private void Print()
    {
        if (Current is not { Id: not null } saved || IsEditing)
        {
            Operation.SetError("PRN_SaveFirst");
            return;
        }

        PrintRequested?.Invoke(new PrintJob(PrintDocumentBuilders.VoucherKindOf(saved), PrintDocumentBuilders.Voucher(saved), Localization["D_Vouchers"] + " " + (saved.VoucherNumber ?? string.Empty)));
    }
}
