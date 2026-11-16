using System.Globalization;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Treasury;

namespace PetraERP.Desktop.Sales;

/// <summary>What the operator entered as the first payment of a credit invoice.</summary>
public sealed record FirstPaymentRequest(Guid CompanyId, Guid BranchId, Guid InvoiceId, string? InvoiceNumber, Guid CustomerId, decimal Amount,
    SettlementMethod Method, string CurrencyCode, string? ExchangeRate, DateTimeOffset DateUtc);

public sealed record FirstPaymentResult(bool Registered, string Message, Guid? VoucherId);

/// <summary>
/// Al-Ameen's "first payment" of a credit invoice. It is recorded as an ordinary receipt voucher allocated to the posted invoice's open
/// item, so the cash/bank account, the numbering, the settlement and the customer's balance all go through the treasury rules; nothing
/// bypasses them. The request key is derived from the invoice, so a retry never creates a second voucher.
/// </summary>
public sealed class FirstPaymentRegistrar(ITreasuryVouchersClient vouchers, IPatternsClient patterns, ILocalizationService localization)
{
    public async Task<FirstPaymentResult> RegisterAsync(FirstPaymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var runnable = await patterns.GetRunnablePatternsAsync(request.CompanyId, request.BranchId, cancellationToken).ConfigureAwait(false);
            var pattern = runnable.FirstOrDefault(p => p.Id.HasValue && p.OperationKind == BusinessOperationKind.CashReceipt);
            if (pattern?.Id is not { } patternId)
                return new(false, localization["D_FirstPaymentNoPattern"], null);
            var detail = await patterns.GetPatternAsync(request.CompanyId, request.BranchId, patternId, cancellationToken).ConfigureAwait(false);
            if (detail.ActiveRevision?.Id is not { } revisionId)
                return new(false, localization["D_FirstPaymentNoPattern"], null);

            var amount = request.Amount.ToString("0.0000", CultureInfo.InvariantCulture);
            var outcome = await vouchers.SaveAndProcessAsync(request.CompanyId, request.BranchId, new SaveAndProcessVoucherRequest
            {
                IdempotencyKey = $"first-payment:{request.InvoiceId:N}",
                SaveAsDraft = false,
                Document = new SaveVoucherRequest
                {
                    Kind = VoucherKind.Receipt,
                    PatternRevisionId = revisionId,
                    PartyId = request.CustomerId,
                    SettlementMethod = request.Method,
                    VoucherDateUtc = request.DateUtc,
                    CurrencyCode = request.CurrencyCode,
                    ExchangeRate = request.ExchangeRate,
                    Amount = amount,
                    Notes = string.Format(CultureInfo.CurrentCulture, localization["D_FirstPaymentNote"], request.InvoiceNumber),
                    Allocations = [new VoucherAllocationRequest { SalesInvoiceId = request.InvoiceId, Amount = amount }],
                    InvoiceAllocations = [new InvoiceSettlementInputDto { InvoiceId = request.InvoiceId, InvoiceAmount = amount, SettlementAmount = amount, CrossRate = "1" }],
                },
            }, cancellationToken).ConfigureAwait(false);
            return outcome.PostingApplied == true
                ? new(true, string.Format(CultureInfo.CurrentCulture, localization["D_FirstPaymentRegistered"], outcome.DocumentNumber), outcome.DocumentId)
                : new(false, string.Format(CultureInfo.CurrentCulture, localization["D_FirstPaymentNotPosted"], outcome.DocumentNumber, outcome.Status), outcome.DocumentId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            return new(false, string.Format(CultureInfo.CurrentCulture, localization["D_FirstPaymentFailed"], exception.Message), null);
        }
    }
}
