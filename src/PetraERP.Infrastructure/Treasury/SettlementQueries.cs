using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Treasury;

internal sealed record SettlementInvoice(Guid Id, Guid PartyId, Guid BranchId, string? Number, DateTimeOffset DateUtc, string CurrencyCode,
    decimal ExchangeRate, decimal Total, decimal Returned, decimal Paid, decimal Outstanding, decimal OutstandingBase, Guid AccountId, bool IsOpenItem = false);

/// <summary>Derived open items, never an editable balance. Call under the company write lock before allocating.</summary>
internal static class SettlementQueries
{
    internal sealed record Seed(Guid Id, Guid PartyId, Guid BranchId, string? Number, DateTimeOffset DateUtc, string CurrencyCode,
        decimal Rate, decimal Total, Guid JournalId, Guid? Source, Guid? Reverses);

    internal sealed record ControlLine(Guid JournalId, Guid AccountId, decimal BaseDebit, decimal BaseCredit);
    internal sealed record AppliedLine(Guid Id, decimal InvoiceAmount, decimal BookedBaseAmount);
    internal sealed record LegacyLine(Guid Id, decimal Amount);

    internal static async Task<IReadOnlyList<SettlementInvoice>> LoadInvoicesAsync(FinancialDbContext db, Guid companyId, Guid? branchId,
        VoucherKind kind, Guid? partyId, string? currency, DateTimeOffset? asOf, CancellationToken token, IReadOnlyCollection<Guid>? partyIds = null,
        Guid? documentId = null)
    {
        var end = asOf ?? DateTimeOffset.MaxValue;
        // documentId narrows the seed set to that invoice plus its returns and reversals (everything Compose needs for it) and skips
        // the party's open items; the arithmetic for that invoice is unchanged.
        var hasDocument = documentId.HasValue;
        // One query set for many parties (e.g. a list page) instead of one per party.
        var hasPartySet = partyIds is not null;
        var partySet = partyIds?.ToArray() ?? [];
        List<Seed> source;
        if (kind == VoucherKind.Receipt)
        {
            source = await db.SalesInvoices.AsNoTracking().Where(i => i.CompanyId == companyId && (!branchId.HasValue || i.BranchId == branchId)
                && (!partyId.HasValue || i.CustomerId == partyId) && (!hasPartySet || partySet.Contains(i.CustomerId)) && (currency == null || i.CurrencyCode == currency)
                && (!hasDocument || i.Id == documentId || i.ReturnOfInvoiceId == documentId || i.ReversesInvoiceId == documentId)
                && i.PaymentMethod == (int)PaymentMethod.Credit && (i.Status == 2 || i.Status == 3) && i.JournalEntryId != null && i.InvoiceDateUtc < end
                && db.JournalEntries.Any(j=>j.Id==i.JournalEntryId && j.CompanyId==companyId && j.BranchId==i.BranchId && (j.State==4 || j.State==5)))
                .Select(i => new Seed(i.Id,i.CustomerId,i.BranchId,i.InvoiceNumber,i.InvoiceDateUtc,i.CurrencyCode,i.ExchangeRate,i.GrandTotal,i.JournalEntryId!.Value,i.ReturnOfInvoiceId,i.ReversesInvoiceId))
                .ToListAsync(token).ConfigureAwait(false);
        }
        else
        {
            source = await db.PurchaseInvoices.AsNoTracking().Where(i => i.CompanyId == companyId && (!branchId.HasValue || i.BranchId == branchId)
                && (!partyId.HasValue || i.SupplierId == partyId) && (!hasPartySet || partySet.Contains(i.SupplierId)) && (currency == null || i.CurrencyCode == currency)
                && (!hasDocument || i.Id == documentId || i.ReturnOfInvoiceId == documentId || i.ReversesInvoiceId == documentId)
                && i.PaymentMethod == (int)PaymentMethod.Credit && (i.Status == 2 || i.Status == 3) && i.JournalEntryId != null && i.InvoiceDateUtc < end
                && db.JournalEntries.Any(j=>j.Id==i.JournalEntryId && j.CompanyId==companyId && j.BranchId==i.BranchId && (j.State==4 || j.State==5)))
                .Select(i => new Seed(i.Id,i.SupplierId,i.BranchId,i.InvoiceNumber,i.InvoiceDateUtc,i.CurrencyCode,i.ExchangeRate,i.GrandTotal,i.JournalEntryId!.Value,i.ReturnOfInvoiceId,i.ReversesInvoiceId))
                .ToListAsync(token).ConfigureAwait(false);
        }
        var originals = SelectOriginals(source);
        var ids = originals.Select(i => i.Id).ToArray();
        var journalIds = source.Select(i => i.JournalId).ToArray();
        var controls = await db.JournalLines.AsNoTracking().Where(l => l.CompanyId == companyId && journalIds.Contains(l.JournalId)
                && (l.Description == "ذمم/نقد البيع" || l.Description == "ذمة المورد" || l.Description == "تخفيض ذمة الزبون بالمرتجع"))
            .Select(l => new ControlLine(l.JournalId,l.AccountId,l.BaseDebit,l.BaseCredit)).ToListAsync(token).ConfigureAwait(false);
        var apps = await db.SettlementApplications.AsNoTracking().Where(a => a.Batch!.CompanyId == companyId
            && (!branchId.HasValue || a.Batch.BranchId == branchId) && a.Batch.DateUtc < end
            && (a.Batch.State == 1 || a.Batch.ReversedAtUtc >= end)
            && db.JournalEntries.Any(j=>j.Id==a.Batch.JournalId && j.CompanyId==companyId && j.BranchId==a.Batch.BranchId && (j.State==4 || j.State==5))
            && db.JournalEntries.Any(j=>j.Id==a.Voucher!.JournalEntryId && j.CompanyId==companyId && j.BranchId==a.Batch.BranchId && (j.State==4 || j.State==5))
            && a.Voucher!.VoucherDateUtc < end && (a.Voucher.Status == 2 || a.Voucher.Status == 3 && a.Voucher.ReversedAtUtc >= end)
            && (kind == VoucherKind.Receipt ? a.SalesInvoiceId != null && ids.Contains(a.SalesInvoiceId.Value) : a.PurchaseInvoiceId != null && ids.Contains(a.PurchaseInvoiceId.Value)))
            .Select(a => new AppliedLine(kind == VoucherKind.Receipt ? a.SalesInvoiceId!.Value : a.PurchaseInvoiceId!.Value,a.InvoiceAmount,a.BookedBaseAmount))
            .ToListAsync(token).ConfigureAwait(false);
        var legacy = await db.VoucherAllocations.AsNoTracking().Where(a => kind == VoucherKind.Receipt && ids.Contains(a.SalesInvoiceId)
            && a.Voucher!.CompanyId == companyId && a.Voucher.VoucherDateUtc < end
            && db.JournalEntries.Any(j=>j.Id==a.Voucher.JournalEntryId && j.CompanyId==companyId && j.BranchId==a.Voucher.BranchId && (j.State==4 || j.State==5))
            && (a.Voucher.Status == 2 || a.Voucher.Status == 3 && a.Voucher.ReversedAtUtc >= end))
            .Select(a => new LegacyLine(a.SalesInvoiceId,a.Amount)).ToListAsync(token).ConfigureAwait(false);
        var result = Compose(kind, source, originals, controls, apps, legacy);
        if (hasDocument) return result;

        // Open Items (prior year rollover, opening balances, or migration open items)
        var partyType = kind == VoucherKind.Receipt ? (byte)1 : (byte)2;
        var openItems = await db.OpenItems.AsNoTracking().Where(o => o.CompanyId == companyId
            && (!branchId.HasValue || o.BranchId == branchId)
            && (!partyId.HasValue || o.PartyId == partyId)
            && (!hasPartySet || partySet.Contains(o.PartyId))
            && (currency == null || o.CurrencyCode == currency)
            && o.PartyType == partyType
            && o.OutstandingAmount > 0
            && o.State != 4 // not cancelled
            && o.OriginDocumentDateUtc < end)
            .ToListAsync(token).ConfigureAwait(false);

        foreach (var item in openItems)
        {
            result.Add(new SettlementInvoice(
                item.Id,
                item.PartyId,
                item.BranchId,
                item.OriginDocumentNumber ?? item.SourceReference,
                item.OriginDocumentDateUtc,
                item.CurrencyCode,
                item.ExchangeRate,
                item.OriginalAmount,
                0m,
                item.SettledAmount,
                item.OutstandingAmount,
                item.CurrentBaseCarryingAmount,
                item.AccountId,
                IsOpenItem: true));
        }

        return result;
    }

    /// <summary>Original (non-return, non-reversal, not reversed) documents; one pass with a hash set instead of a scan per invoice.</summary>
    internal static Seed[] SelectOriginals(IReadOnlyList<Seed> source)
    {
        var reversed = new HashSet<Guid>();
        foreach (var row in source)
            if (row.Reverses is { } target) reversed.Add(target);
        return source.Where(i => i.Source is null && i.Reverses is null && !reversed.Contains(i.Id)).ToArray();
    }

    /// <summary>
    /// Pure composition of the open balance of every original invoice. Every per-invoice lookup is a hash lookup built once
    /// (O(N + M) instead of O(N·M)); the arithmetic, the rounding calls, the iteration order and the two exceptions are unchanged.
    /// </summary>
    internal static List<SettlementInvoice> Compose(VoucherKind kind, IReadOnlyList<Seed> source, IReadOnlyList<Seed> originals,
        IReadOnlyList<ControlLine> controls, IReadOnlyList<AppliedLine> apps, IReadOnlyList<LegacyLine> legacy)
    {
        var controlsByJournal = controls.ToLookup(l => l.JournalId);
        var returnsBySource = source.Where(r => r.Source is not null).ToLookup(r => r.Source!.Value);
        var appsById = apps.ToLookup(a => a.Id);
        var legacyById = legacy.ToLookup(a => a.Id);
        var result = new List<SettlementInvoice>(originals.Count);
        foreach (var invoice in originals)
        {
            var originalControls = controlsByJournal[invoice.JournalId].ToArray();
            // Read the actual frozen journal target, not today's mutable party mapping or a shared-account total.
            var accountIds = originalControls.Select(l => l.AccountId).Distinct().ToArray();
            if (accountIds.Length != 1)
                throw new BusinessRuleException("treasury.control_provenance", "تعذر تحديد حساب ذمة الفاتورة من قيدها؛ يلزم مراجعة المصدر / Invoice control-account provenance is missing or ambiguous; review the original journal.");
            var returns = returnsBySource[invoice.Id].ToArray();
            var returned = returns.Sum(r => r.Total);
            var appPaid = appsById[invoice.Id].ToArray();
            var legacyPaid = legacyById[invoice.Id].ToArray();
            var oldPaid = legacyPaid.Sum(a => a.Amount);
            var paid = oldPaid + appPaid.Sum(a => a.InvoiceAmount);
            var originalBase = kind == VoucherKind.Receipt ? originalControls.Sum(l => l.BaseDebit - l.BaseCredit) : originalControls.Sum(l => l.BaseCredit - l.BaseDebit);
            var returnedBase = returns.Select(r => r.JournalId).Distinct()
                .SelectMany(journal => controlsByJournal[journal])
                .Sum(l => kind == VoucherKind.Receipt ? l.BaseCredit - l.BaseDebit : l.BaseDebit - l.BaseCredit);
            var remainingBase = originalBase - returnedBase - appPaid.Sum(a => a.BookedBaseAmount) - legacyPaid.Sum(a => DocumentCurrencyPolicy.ToBase(a.Amount, invoice.Rate));
            var remaining = invoice.Total - returned - paid;
            if (remaining < 0 || remainingBase < 0 || remaining == 0 && remainingBase != 0)
                throw new BusinessRuleException("treasury.reconciliation_required", "تخصيصات الفاتورة أو قيمتها الدفترية غير متطابقة؛ راجع حركاتها قبل المتابعة / Invoice allocations and carrying balance require reconciliation before continuing.");
            result.Add(new(invoice.Id, invoice.PartyId, invoice.BranchId, invoice.Number, invoice.DateUtc, invoice.CurrencyCode, invoice.Rate,
                invoice.Total, returned, paid, remaining, remainingBase, accountIds[0], IsOpenItem: false));
        }
        return result;
    }

    internal static async Task<decimal> AvailableAdvanceBaseAsync(FinancialDbContext db, Guid companyId, Guid? branchId, VoucherKind kind, Guid partyId, CancellationToken token)
    {
        var rows = await db.Vouchers.AsNoTracking().Where(v => v.CompanyId == companyId && (!branchId.HasValue || v.BranchId == branchId)
            && v.PartyId == partyId && v.Kind == (byte)kind && v.Status == 2 && !v.Lines.Any()
            && db.JournalEntries.Any(j=>j.Id==v.JournalEntryId && j.CompanyId==companyId && j.BranchId==v.BranchId && (j.State==4 || j.State==5)))
            .Select(v => new { v.Id,v.Amount,v.ExchangeRate,Legacy = v.Allocations.Sum(a => (decimal?)a.Amount) ?? 0m }).ToListAsync(token).ConfigureAwait(false);
        var ids = rows.Select(v => v.Id).ToArray();
        var applied = await db.SettlementApplications.AsNoTracking().Where(a => ids.Contains(a.VoucherId) && a.Batch!.State == 1
            && db.JournalEntries.Any(j=>j.Id==a.Batch.JournalId && j.CompanyId==companyId && j.BranchId==a.Batch.BranchId && (j.State==4 || j.State==5)))
            .Select(a => new { a.VoucherId,a.SettlementAmount,a.SettlementBaseAmount }).ToListAsync(token).ConfigureAwait(false);
        decimal total=0m;
        foreach(var voucher in rows)
        {
            var applications=applied.Where(a=>a.VoucherId==voucher.Id).ToArray();
            var available=voucher.Amount-voucher.Legacy-applications.Sum(a=>a.SettlementAmount);
            var availableBase=DocumentCurrencyPolicy.ToBase(voucher.Amount-voucher.Legacy,voucher.ExchangeRate)-applications.Sum(a=>a.SettlementBaseAmount);
            if(available<0 || availableBase<0 || available==0 && availableBase!=0)
                throw new BusinessRuleException("treasury.reconciliation_required", "رصيد الدفعة وتطبيقاتها غير متطابق؛ راجع سجل التسويات / Advance and applications require reconciliation.");
            total+=availableBase;
        }
        return total;
    }
}
