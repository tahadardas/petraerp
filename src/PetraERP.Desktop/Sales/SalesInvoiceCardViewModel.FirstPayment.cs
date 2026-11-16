using System.Globalization;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Sales;

/// <summary>
/// The "first payment" of a new credit invoice (Al-Ameen): an amount paid at once, recorded as a receipt voucher allocated to the invoice
/// once the invoice is posted. The fields exist only while a new credit invoice is being entered.
/// </summary>
public sealed partial class SalesInvoiceCardViewModel
{
    private string firstPaymentAmount = string.Empty;
    private SettlementMethod firstPaymentMethod = SettlementMethod.Cash;

    /// <summary>Records the payment as a treasury voucher; absent when the shell has no treasury client (the fields then stay hidden).</summary>
    public FirstPaymentRegistrar? FirstPayment { get; init; }

    public string FirstPaymentAmount
    {
        get => firstPaymentAmount;
        set { if (SetProperty(ref firstPaymentAmount, value)) { OnPropertyChanged(nameof(FirstPaymentRemainingDisplay)); DraftChanged(); } }
    }

    public SettlementMethod FirstPaymentMethod
    {
        get => firstPaymentMethod;
        set { if (SetProperty(ref firstPaymentMethod, value)) DraftChanged(); }
    }

    public IReadOnlyList<SettlementMethodOption> FirstPaymentMethods =>
    [
        new(SettlementMethod.Cash, Localization["D_SettlementCash"]),
        new(SettlementMethod.Bank, Localization["D_SettlementBank"]),
    ];

    public bool ShowFirstPayment => FirstPayment is not null && IsEditing && InvoiceId is null && DraftPaymentMethod == PaymentMethod.Credit && DraftCustomerId is not null;

    /// <summary>What remains owed after the first payment, as Al-Ameen's "المبلغ المتبقي".</summary>
    public string FirstPaymentRemainingDisplay => (ParseDecimal(GrandTotalDisplay) - FirstPaymentValue).ToString("0.0000", CultureInfo.InvariantCulture);

    private decimal FirstPaymentValue => ParseDecimal(FirstPaymentAmount);

    private bool FirstPaymentExceedsTotal => ShowFirstPayment && FirstPaymentValue > ParseDecimal(GrandTotalDisplay);

    private void RefreshFirstPayment()
    {
        OnPropertyChanged(nameof(ShowFirstPayment));
        OnPropertyChanged(nameof(FirstPaymentRemainingDisplay));
    }

    private void ResetFirstPayment()
    {
        firstPaymentAmount = string.Empty;
        firstPaymentMethod = SettlementMethod.Cash;
        OnPropertyChanged(nameof(FirstPaymentAmount));
        OnPropertyChanged(nameof(FirstPaymentMethod));
        RefreshFirstPayment();
    }

    /// <summary>What to record after the invoice was saved: null when no first payment was entered for this new credit invoice.</summary>
    private (decimal Amount, SettlementMethod Method)? CaptureFirstPayment() =>
        ShowFirstPayment && FirstPaymentValue > 0m ? (FirstPaymentValue, FirstPaymentMethod) : null;

    private async Task RegisterFirstPaymentAsync(DocumentProcessingResultDto outcome, (decimal Amount, SettlementMethod Method)? payment)
    {
        if (payment is not { } entered || FirstPayment is null || Invoice is not { Id: { } invoiceId, CustomerId: { } customerId } posted) return;
        if (outcome.PostingApplied != true)
        {
            SaveOutcomeIsWarning = true;
            SaveOutcomeMessage = (SaveOutcomeMessage + " " + Localization["D_FirstPaymentPending"]).Trim();
            return;
        }

        var result = await FirstPayment.RegisterAsync(new FirstPaymentRequest(CompanyId, BranchId, invoiceId, posted.InvoiceNumber, customerId, entered.Amount, entered.Method,
            posted.CurrencyCode ?? string.Empty, posted.ExchangeRate, posted.InvoiceDateUtc ?? DateTimeOffset.UtcNow), Token).ConfigureAwait(true);
        SaveOutcomeIsWarning = !result.Registered;
        SaveOutcomeMessage = (SaveOutcomeMessage + " " + result.Message).Trim();
        if (result.Registered) await LoadInvoiceAsync(Token).ConfigureAwait(true);
    }
}

/// <summary>A settlement method choice for the first payment's combo box.</summary>
public sealed record SettlementMethodOption(SettlementMethod Value, string Label);
