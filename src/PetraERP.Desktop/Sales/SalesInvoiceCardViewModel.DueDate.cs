using System.Globalization;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Sales;

/// <summary>
/// Al-Ameen shows the due date of a credit invoice from the payment terms. Here it is the invoice date plus the customer's payment
/// term days. It is information for the operator; the open item the server creates remains the authority for collection.
/// </summary>
public sealed partial class SalesInvoiceCardViewModel
{
    private int paymentTermDays;
    private int termsRequest;

    public bool ShowDueDate => DraftPaymentMethod == PaymentMethod.Credit && DraftInvoiceDate is not null && DraftCustomerId is not null;

    public string DueDateDisplay => DraftInvoiceDate is { } date ? date.Date.AddDays(paymentTermDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;

    private async Task LoadPaymentTermsAsync(Guid? customerId)
    {
        var request = ++termsRequest;
        paymentTermDays = 0;
        RefreshDueDate();
        if (customerId is not { } id || !HasContext) return;
        try
        {
            var party = await partiesClient.GetPartyAsync(CompanyId, BranchId, id, Token).ConfigureAwait(true);
            if (request != termsRequest) return;
            paymentTermDays = Math.Max(0, party.PaymentTermDays ?? 0);
        }
        catch (Exception exception) when (exception is OperationCanceledException or System.Net.Http.HttpRequestException or PetraApiException)
        {
            // Information only: a failed read leaves the invoice date as the due date and never blocks the invoice.
        }

        RefreshDueDate();
    }

    private void RefreshDueDate()
    {
        OnPropertyChanged(nameof(ShowDueDate));
        OnPropertyChanged(nameof(DueDateDisplay));
    }
}
