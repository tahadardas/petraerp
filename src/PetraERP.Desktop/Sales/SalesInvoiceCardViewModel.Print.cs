using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Printing;
using PetraERP.Reporting.OpenSource.Printing;

namespace PetraERP.Desktop.Sales;

public sealed partial class SalesInvoiceCardViewModel
{
    private RelayCommand? printCommand;
    private RelayCommand? designTemplateCommand;

    /// <summary>Raised with the document to preview or print; the shell adds the company context and opens the print window.</summary>
    public event Action<PrintJob>? PrintRequested;

    /// <summary>Preview / print of the saved invoice (Ctrl+P). An unsaved or being-edited invoice must be saved first: the printout is the server's record.</summary>
    public RelayCommand PrintCommand => printCommand ??= new RelayCommand(Print);

    /// <summary>Opens the visual print template designer (مصمم القوالب والفواتير) for this invoice type.</summary>
    public RelayCommand DesignTemplateCommand => designTemplateCommand ??= new RelayCommand(DesignTemplate);

    private void DesignTemplate()
    {
        var sampleOrSaved = Invoice is { Id: not null } saved
            ? PrintDocumentBuilders.SalesInvoice(saved)
            : PrintFieldCatalog.Sample(PrintDocumentKind.SalesInvoice);
        PrintRequested?.Invoke(new PrintJob(PrintDocumentKind.SalesInvoice, sampleOrSaved, Localization["PRN_TemplateDesignerTitle"]));
    }

    private void Print()
    {
        if (Invoice is not { Id: not null } saved || IsEditing)
        {
            ValidationMessage = Localization["PRN_SaveFirst"];
            return;
        }

        ValidationMessage = string.Empty;
        PrintRequested?.Invoke(new PrintJob(PrintDocumentKind.SalesInvoice, PrintDocumentBuilders.SalesInvoice(saved), Localization["D_SalesInvoiceCard"] + " " + (saved.InvoiceNumber ?? string.Empty)));
    }
}
