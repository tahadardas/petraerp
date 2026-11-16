using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Treasury;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Treasury;

namespace PetraERP.Infrastructure.Treasury;

public sealed partial class SqlTreasuryVoucherService
{
    private sealed record PreparedSettlement(InvoiceSettlementCommand Input, SettlementInvoice Invoice, decimal BookedBase, decimal SettledBase);

    private static IReadOnlyList<InvoiceSettlementCommand> AllocationInputs(SaveVoucherCommand command)
    {
        if (command.InvoiceAllocations.Count > 0 && command.Allocations.Count > 0)
            throw SettlementError("allocation_duplicate", "استخدم مجموعة تخصيص واحدة", "Supply one allocation collection only.");
        return command.InvoiceAllocations.Count > 0 ? command.InvoiceAllocations
            : command.Allocations.Select(a => new InvoiceSettlementCommand(a.SalesInvoiceId,a.Amount,a.Amount,1m)).ToArray();
    }

    private async Task<IReadOnlyList<PreparedSettlement>> PrepareAsync(Guid company, Guid branch, VoucherKind kind, Guid party,
        string voucherCurrency, decimal voucherRate, decimal availableAmount, decimal availableBase,
        DateTimeOffset date, IReadOnlyList<InvoiceSettlementCommand> inputs, CancellationToken token)
    {
        if (inputs.Count > 200 || inputs.Any(l => l is null) || inputs.Select(l => l.InvoiceId).Distinct().Count() != inputs.Count)
            throw SettlementError("allocation_duplicate", "الفواتير يجب أن تكون مميزة حتى 200 سطر", "Provide at most200 distinct invoice allocations.");
        var invoices = await SettlementQueries.LoadInvoicesAsync(context,company,branch,kind,party,null,null,token).ConfigureAwait(false);
        var result = new List<PreparedSettlement>();
        decimal remaining = availableAmount, baseRemaining = availableBase;
        foreach (var line in inputs.OrderBy(l => l.InvoiceId))
        {
            Rule(() => VoucherRules.RequireAmount(line.InvoiceAmount), "invoiceAmount");
            Rule(() => VoucherRules.RequireAmount(line.SettlementAmount), "settlementAmount");
            Rule(() => DocumentCurrencyPolicy.RequireRate(line.CrossRate), "crossRate");
            var invoice = invoices.SingleOrDefault(i => i.Id == line.InvoiceId)
                ?? throw SettlementError("allocation_invoice_invalid", "الفاتورة ليست آجلة مرحلة لهذا الطرف والفرع", "Invoice must be a posted credit original of this party and branch.");
            if (invoice.DateUtc.UtcDateTime.Date > date.UtcDateTime.Date)
                throw SettlementError("allocation_date", "تاريخ التسوية يسبق الفاتورة", "Settlement date precedes the invoice.");
            if (invoice.CurrencyCode == voucherCurrency && (line.CrossRate != 1 || line.InvoiceAmount != line.SettlementAmount)
                || decimal.Round(checked(line.SettlementAmount * line.CrossRate),4,MidpointRounding.ToEven) != line.InvoiceAmount)
                throw SettlementError("cross_rate", "مبلغ الفاتورة يساوي مبلغ السند × التحويل؛ السعر 1 للعملة نفسها", "Invoice amount must equal settlement amount × cross rate; same currency requires1.");
            if (line.InvoiceAmount > invoice.Outstanding || line.SettlementAmount > remaining)
                throw SettlementError("invoice_over_allocated", "التخصيص يتجاوز المتبقي؛ أعد التحميل", "Allocation exceeds the remaining invoice or advance; reload.");
            var booked = line.InvoiceAmount == invoice.Outstanding ? invoice.OutstandingBase : DocumentCurrencyPolicy.ToBase(line.InvoiceAmount,invoice.ExchangeRate);
            var settled = line.SettlementAmount == remaining ? baseRemaining : DocumentCurrencyPolicy.ToBase(line.SettlementAmount,voucherRate);
            if (booked <= 0 || settled <= 0 || booked > invoice.OutstandingBase || settled > baseRemaining)
                throw SettlementError("settlement_precision", "المبلغ لا يمثل بدقة عملة الأساس؛ عدل الدفعة أو راجع المطابقة", "Payment is not representable at base precision; adjust it or review reconciliation.");
            result.Add(new(line,invoice,booked,settled));
            remaining -= line.SettlementAmount; baseRemaining -= settled;
        }
        return result;
    }

    private SettlementBatchRow NewBatch(VoucherRow voucher, Guid actor, string key, string fingerprint, string reason, DateTimeOffset date,
        bool initial, IReadOnlyList<PreparedSettlement> prepared)
    {
        var batch = new SettlementBatchRow { Id = Guid.NewGuid(),CompanyId=voucher.CompanyId,BranchId=voucher.BranchId,VoucherId=voucher.Id,
            ActorId=actor,RequestKey=key,Fingerprint=fingerprint,Reason=reason,DateUtc=date.ToUniversalTime(),RecordedAtUtc=timeProvider.GetUtcNow(),IsInitial=initial };
        foreach (var p in prepared)
            batch.Applications.Add(new SettlementApplicationRow { Id=Guid.NewGuid(),BatchId=batch.Id,VoucherId=voucher.Id,
                SalesInvoiceId=p.Invoice.IsOpenItem ? null : (voucher.Kind==(byte)VoucherKind.Receipt?p.Invoice.Id:null),
                PurchaseInvoiceId=p.Invoice.IsOpenItem ? null : (voucher.Kind==(byte)VoucherKind.Payment?p.Invoice.Id:null),
                OpenItemId=p.Invoice.IsOpenItem ? p.Invoice.Id : null,
                InvoiceCurrency=p.Invoice.CurrencyCode,SettlementCurrency=voucher.CurrencyCode,InvoiceAmount=p.Input.InvoiceAmount,SettlementAmount=p.Input.SettlementAmount,
                CrossRate=p.Input.CrossRate,InvoiceRate=p.Invoice.ExchangeRate,SettlementRate=voucher.ExchangeRate,BookedBaseAmount=p.BookedBase,SettlementBaseAmount=p.SettledBase,AccountId=p.Invoice.AccountId });
        context.SettlementBatches.Add(batch);
        return batch;
    }

    private static BusinessRuleException SettlementError(string code, string arabic, string english)
        => new("treasury."+code,arabic+" / "+english);
}
