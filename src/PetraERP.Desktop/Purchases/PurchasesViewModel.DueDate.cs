using System.Globalization;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Purchases;

/// <summary>
/// The due date of a credit purchase is the invoice date plus the supplier's payment term days. It is information for the operator;
/// the open item the server creates when the invoice posts remains the authority for what is owed and when.
/// </summary>
public sealed partial class PurchasesViewModel
{
    private int paymentTermDays;
    private int termsRequest;

    public bool ShowDueDate => DraftPaymentMethod == PaymentMethod.Credit && DraftDate is not null && DraftSupplierId is not null;

    public string DueDateDisplay => DraftDate is { } date
        ? date.Date.AddDays(paymentTermDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : string.Empty;

    private async Task LoadPaymentTermsAsync(Guid? supplierId)
    {
        var attempt = ++termsRequest;
        paymentTermDays = 0;
        if (supplierId is not { } id || !HasContext) return;
        try
        {
            var party = await partiesClient.GetPartyAsync(CompanyId, BranchId, id, Token).ConfigureAwait(true);
            if (attempt != termsRequest) return;
            paymentTermDays = Math.Max(0, party.PaymentTermDays ?? 0);
            Refresh();
        }
        catch (Exception exception) when (exception is OperationCanceledException or System.Net.Http.HttpRequestException or PetraApiException)
        {
            // A failed read leaves the due date equal to the invoice date; it never blocks the purchase.
        }
    }
}
