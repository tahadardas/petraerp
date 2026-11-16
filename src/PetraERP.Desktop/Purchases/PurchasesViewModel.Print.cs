using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Printing;
using PetraERP.Reporting.OpenSource.Printing;

namespace PetraERP.Desktop.Purchases;

public sealed partial class PurchasesViewModel
{
    private RelayCommand? printCommand;
    private RelayCommand? designTemplateCommand;

    /// <summary>Raised with the document to preview or print; the shell adds the company context and opens the print window.</summary>
    public event Action<PrintJob>? PrintRequested;

    /// <summary>Preview / print of the saved purchase invoice (Ctrl+P); an unsaved or being-edited one must be saved first.</summary>
    public RelayCommand PrintCommand => printCommand ??= new RelayCommand(Print);

    /// <summary>Opens the visual print template designer (مصمم القوالب والفواتير) for this invoice type.</summary>
    public RelayCommand DesignTemplateCommand => designTemplateCommand ??= new RelayCommand(DesignTemplate);

    private void DesignTemplate()
    {
        var sampleOrSaved = Current is { Id: not null } saved
            ? PrintDocumentBuilders.PurchaseInvoice(saved)
            : PrintFieldCatalog.Sample(PrintDocumentKind.PurchaseInvoice);
        PrintRequested?.Invoke(new PrintJob(PrintDocumentKind.PurchaseInvoice, sampleOrSaved, Localization["PRN_TemplateDesignerTitle"]));
    }

    private void Print()
    {
        if (Current is not { Id: not null } saved || IsEditing)
        {
            Operation.SetError("PRN_SaveFirst");
            return;
        }

        PrintRequested?.Invoke(new PrintJob(PrintDocumentKind.PurchaseInvoice, PrintDocumentBuilders.PurchaseInvoice(saved), Localization["D_PurchaseInvoices"] + " " + (saved.InvoiceNumber ?? string.Empty)));
    }
}
