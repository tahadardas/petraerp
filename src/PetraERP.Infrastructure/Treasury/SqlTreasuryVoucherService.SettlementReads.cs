using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Treasury;

namespace PetraERP.Infrastructure.Treasury;

public sealed partial class SqlTreasuryVoucherService : ISettlementService
{
    public async Task<SettlementInvoicePageResult> InvoicesAsync(Guid company, Guid branch, Guid party, VoucherKind kind, string? currency,
        SettlementStateFilter state, int page, int pageSize, CancellationToken token)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(state) || page < 1 || pageSize is < 1 or > 200 || (long)(page-1)*pageSize>int.MaxValue)
            throw SettlementError("query", "راجع النوع والحالة ورقم الصفحة", "Invalid kind, state or paging.");
        if (!await context.Parties.AnyAsync(p => p.Id==party && p.CompanyId==company,token).ConfigureAwait(false)) throw new ResourceNotFoundException("Party");
        var rows = await SettlementQueries.LoadInvoicesAsync(context,company,branch,kind,party,string.IsNullOrWhiteSpace(currency)?null:currency.Trim().ToUpperInvariant(),null,token).ConfigureAwait(false);
        static SettlementStateFilter State(SettlementInvoice i) => i.Outstanding<=0?SettlementStateFilter.Settled:i.Paid>0?SettlementStateFilter.Partial:SettlementStateFilter.Open;
        var filtered = rows.Where(i => state==SettlementStateFilter.All || State(i)==state).OrderBy(i => i.DateUtc).ThenBy(i => i.Id).ToArray();
        return new(page,pageSize,filtered.Length,filtered.Skip((page-1)*pageSize).Take(pageSize).Select(i => new SettlementInvoiceResult(i.Id,i.PartyId,i.Number,i.DateUtc,i.CurrencyCode,
            i.ExchangeRate,i.Total,i.Returned,i.Paid,i.Outstanding,i.OutstandingBase,State(i))).ToArray());
    }

    private sealed record TargetMeta(Guid Id, string? Number, DateTimeOffset DateUtc, string CurrencyCode);

    private async Task<VoucherDetailResult> EnrichSettlementsAsync(VoucherDetailResult result, VoucherRow voucher, CancellationToken token)
    {
        var batches = await context.SettlementBatches.AsNoTracking().Include(b => b.Applications)
            .Where(b => b.CompanyId==voucher.CompanyId && b.BranchId==voucher.BranchId && b.VoucherId==voucher.Id)
            .OrderBy(b => b.RecordedAtUtc).ThenBy(b => b.Id).ToListAsync(token).ConfigureAwait(false);
        static Guid TargetId(Guid? sales, Guid? purchase, Guid? openItem) => sales ?? purchase ?? openItem ?? Guid.Empty;
        var ids = voucher.AllocationPlans.Select(p => TargetId(p.SalesInvoiceId, p.PurchaseInvoiceId, p.OpenItemId))
            .Concat(batches.SelectMany(b => b.Applications).Select(a => TargetId(a.SalesInvoiceId, a.PurchaseInvoiceId, a.OpenItemId)))
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        var invoiceRows = await context.SalesInvoices.AsNoTracking().Where(i => i.CompanyId==voucher.CompanyId && ids.Contains(i.Id))
            .Select(i => new TargetMeta(i.Id, i.InvoiceNumber, i.InvoiceDateUtc, i.CurrencyCode)).ToListAsync(token).ConfigureAwait(false);
        var purchaseRows = await context.PurchaseInvoices.AsNoTracking().Where(i => i.CompanyId==voucher.CompanyId && ids.Contains(i.Id))
            .Select(i => new TargetMeta(i.Id, i.InvoiceNumber, i.InvoiceDateUtc, i.CurrencyCode)).ToListAsync(token).ConfigureAwait(false);
        var openItemRows = await context.OpenItems.AsNoTracking().Where(i => i.CompanyId==voucher.CompanyId && ids.Contains(i.Id))
            .Select(i => new TargetMeta(i.Id, i.OriginDocumentNumber ?? i.SourceReference ?? i.Id.ToString(), i.OriginDocumentDateUtc, i.CurrencyCode))
            .ToListAsync(token).ConfigureAwait(false);
        var metadata = invoiceRows.Concat(purchaseRows).Concat(openItemRows).ToDictionary(i => i.Id);
        InvoiceSettlementResult Map(Guid id, Guid target, decimal invoiceAmount, decimal amount, decimal rate) => metadata.TryGetValue(target,out var m)
            ? new(id,target,m.Number,m.DateUtc,m.CurrencyCode,invoiceAmount,amount,rate)
            : throw new ResourceNotFoundException("SettlementInvoice");
        var allocated = voucher.Status==(byte)VoucherStatus.Reversed?0:result.Allocations.Sum(a => a.Amount)+batches.Where(b=>b.State==1).SelectMany(b=>b.Applications).Sum(a=>a.SettlementAmount);
        if (voucher.Status is 0 or 1) allocated += voucher.AllocationPlans.Sum(a=>a.SettlementAmount);
        return result with
        {
            AllocatedAmount=allocated,
            AdvanceAmount=voucher.Status!=(byte)VoucherStatus.Reversed && (voucher.Kind==(byte)VoucherKind.Receipt || voucher.PartyId!=null && voucher.Lines.Count==0) ? voucher.Amount-allocated :0,
            InvoiceAllocations=voucher.AllocationPlans.Select(a=>Map(a.Id,TargetId(a.SalesInvoiceId, a.PurchaseInvoiceId, a.OpenItemId),a.InvoiceAmount,a.SettlementAmount,a.CrossRate)).ToArray(),
            SettlementBatches=batches.Select(b=>new SettlementBatchResult(b.Id,b.DateUtc,b.State,b.Reason,b.JournalId,b.RowVersion,
                b.Applications.Select(a=>Map(a.Id,TargetId(a.SalesInvoiceId, a.PurchaseInvoiceId, a.OpenItemId),a.InvoiceAmount,a.SettlementAmount,a.CrossRate)).ToArray())
                { IsInitial=b.IsInitial,ReversalJournalId=b.ReversalJournalId,ReversedAtUtc=b.ReversedAtUtc,ReversedBy=b.ReversedBy,ReversalReason=b.ReversalReason }).ToArray(),
        };
    }
}
