using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports.Professional;
using PetraERP.Infrastructure.Parties.OpenItems;

namespace PetraERP.Infrastructure.Reports.Professional;

public sealed partial class SqlProfessionalReportService
{
    private IQueryable<PartyEffect> PartyEffects(LedgerReportScope scope, Guid partyId, bool supplier, string currency)
    {
        var posted = context.JournalEntries.AsNoTracking().Where(j => j.CompanyId == scope.CompanyId && j.BranchId == scope.BranchId && PostedStates.Contains(j.State));
        IQueryable<PartyEffect> invoices;
        if (supplier)
        {
            invoices = context.PurchaseInvoices.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId && i.BranchId == scope.BranchId
                && i.SupplierId == partyId && i.PaymentMethod == 1 && i.CurrencyCode == currency && posted.Any(j => j.Id == i.JournalEntryId))
                .Select(i => new PartyEffect
                {
                    EffectId = i.Id, IsReversal = false, DocumentId = i.Id, JournalId = i.JournalEntryId, Source = i.ReturnOfInvoiceId == null && i.ReversesInvoiceId == null ? ProfessionalReportSource.PurchaseInvoice : ProfessionalReportSource.PurchaseReturn,
                    PatternRevisionId = i.PatternRevisionId, Number = i.InvoiceNumber, DateUtc = i.InvoiceDateUtc, CreatedUtc = i.CreatedAtUtc,
                    Description = i.Notes ?? string.Empty, Debit = i.ReturnOfInvoiceId != null || i.ReversesInvoiceId != null ? i.GrandTotal : 0m, Credit = i.ReturnOfInvoiceId == null && i.ReversesInvoiceId == null ? i.GrandTotal : 0m,
                });
        }
        else
        {
            invoices = context.SalesInvoices.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId && i.BranchId == scope.BranchId
                && i.CustomerId == partyId && i.PaymentMethod == 1 && i.CurrencyCode == currency && posted.Any(j => j.Id == i.JournalEntryId))
                .Select(i => new PartyEffect
                {
                    EffectId = i.Id, IsReversal = false, DocumentId = i.Id, JournalId = i.JournalEntryId, Source = i.ReturnOfInvoiceId == null && i.ReversesInvoiceId == null ? ProfessionalReportSource.SalesInvoice : ProfessionalReportSource.SalesReturn,
                    PatternRevisionId = i.PatternRevisionId, Number = i.InvoiceNumber, DateUtc = i.InvoiceDateUtc, CreatedUtc = i.CreatedAtUtc,
                    Description = i.Notes ?? string.Empty, Debit = i.ReturnOfInvoiceId == null && i.ReversesInvoiceId == null ? i.GrandTotal : 0m,
                    Credit = i.ReturnOfInvoiceId != null || i.ReversesInvoiceId != null ? i.GrandTotal : 0m,
                });
        }
        var kind = supplier ? (byte)2 : (byte)1;
        var source = supplier ? ProfessionalReportSource.PaymentVoucher : ProfessionalReportSource.ReceiptVoucher;
        var vouchers = context.Vouchers.AsNoTracking().Where(v => v.CompanyId == scope.CompanyId && v.BranchId == scope.BranchId
            && v.Kind == kind && v.CurrencyCode == currency && posted.Any(j => j.Id == v.JournalEntryId));
        var direct = vouchers.Where(v => v.PartyId == partyId && !v.Lines.Any()).Select(v => new PartyEffect
        {
            EffectId = v.Id, IsReversal = false, DocumentId = v.Id, JournalId = v.JournalEntryId, Source = source, PatternRevisionId = v.PatternRevisionId,
            Number = v.VoucherNumber, DateUtc = v.VoucherDateUtc, CreatedUtc = v.CreatedAtUtc, Description = v.Notes ?? string.Empty,
            Debit = supplier ? v.Amount : 0m, Credit = supplier ? 0m : v.Amount,
        });
        var directReversed = vouchers.Where(v => v.PartyId == partyId && !v.Lines.Any() && v.ReversedAtUtc != null && posted.Any(j => j.Id == v.ReversalJournalEntryId))
            .Select(v => new PartyEffect
            {
                EffectId = v.Id, IsReversal = true, DocumentId = v.Id, JournalId = v.ReversalJournalEntryId, Source = source, PatternRevisionId = v.PatternRevisionId,
                Number = v.VoucherNumber, DateUtc = v.ReversedAtUtc!.Value, CreatedUtc = v.CreatedAtUtc, Description = v.Notes ?? string.Empty,
                Debit = supplier ? 0m : v.Amount, Credit = supplier ? v.Amount : 0m,
            });
        var distributions = vouchers.SelectMany(v => v.Lines.Where(l => l.PartyId == partyId), (v, l) => new PartyEffect
        {
            EffectId = l.Id, IsReversal = false, DocumentId = v.Id, JournalId = v.JournalEntryId, Source = source, PatternRevisionId = v.PatternRevisionId,
            Number = v.VoucherNumber, DateUtc = v.VoucherDateUtc, CreatedUtc = v.CreatedAtUtc, Description = (v.Notes ?? string.Empty) + " — " + (l.Description ?? string.Empty),
            Debit = supplier ? l.Amount : 0m, Credit = supplier ? 0m : l.Amount,
        });
        var distributionReversed = vouchers.Where(v => v.ReversedAtUtc != null && posted.Any(j => j.Id == v.ReversalJournalEntryId))
            .SelectMany(v => v.Lines.Where(l => l.PartyId == partyId), (v, l) => new PartyEffect
            {
                EffectId = l.Id, IsReversal = true, DocumentId = v.Id, JournalId = v.ReversalJournalEntryId, Source = source, PatternRevisionId = v.PatternRevisionId,
                Number = v.VoucherNumber, DateUtc = v.ReversedAtUtc!.Value, CreatedUtc = v.CreatedAtUtc, Description = (v.Notes ?? string.Empty) + " — " + (l.Description ?? string.Empty),
                Debit = supplier ? 0m : l.Amount, Credit = supplier ? l.Amount : 0m,
            });
        var applications = context.SettlementApplications.AsNoTracking().Where(a => a.Batch!.CompanyId == scope.CompanyId && a.Batch.BranchId == scope.BranchId
            && (a.Batch.State == 1 || a.Batch.State == 2) && a.Voucher!.PartyId == partyId && a.Voucher.Kind == kind
            && posted.Any(j => j.Id == a.Voucher.JournalEntryId)
            && a.InvoiceCurrency != a.SettlementCurrency && (a.InvoiceCurrency == currency || a.SettlementCurrency == currency));
        var settled = applications.Select(a => new PartyEffect
        {
            EffectId = a.Id, IsReversal = false, DocumentId = a.VoucherId, JournalId = a.Batch!.JournalId ?? a.Voucher!.JournalEntryId, Source = ProfessionalReportSource.Settlement,
            PatternRevisionId = a.Voucher!.PatternRevisionId, Number = a.Voucher.VoucherNumber, DateUtc = a.Batch!.DateUtc,
            CreatedUtc = a.Batch.RecordedAtUtc, Description = a.Batch.Reason,
            Debit = supplier ? a.InvoiceCurrency == currency ? a.InvoiceAmount : 0m : a.SettlementCurrency == currency ? a.SettlementAmount : 0m,
            Credit = supplier ? a.SettlementCurrency == currency ? a.SettlementAmount : 0m : a.InvoiceCurrency == currency ? a.InvoiceAmount : 0m,
        });
        var unsettled = applications.Where(a => a.Batch!.ReversedAtUtc != null).Select(a => new PartyEffect
        {
            EffectId = a.Id, IsReversal = true, DocumentId = a.VoucherId, JournalId = a.Batch!.ReversalJournalId ?? a.Voucher!.ReversalJournalEntryId, Source = ProfessionalReportSource.Settlement,
            PatternRevisionId = a.Voucher!.PatternRevisionId, Number = a.Voucher.VoucherNumber, DateUtc = a.Batch!.ReversedAtUtc!.Value,
            CreatedUtc = a.Batch.RecordedAtUtc, Description = a.Batch.ReversalReason ?? string.Empty,
            Debit = supplier ? a.SettlementCurrency == currency ? a.SettlementAmount : 0m : a.InvoiceCurrency == currency ? a.InvoiceAmount : 0m,
            Credit = supplier ? a.InvoiceCurrency == currency ? a.InvoiceAmount : 0m : a.SettlementCurrency == currency ? a.SettlementAmount : 0m,
        });
        // Brought-forward balances (prior-year rollovers, opening balances, migration items) enter the statement
        // at their original amount; current-year settlements against them already appear as voucher rows above.
        var partyType = supplier ? (byte)OpenItemPartyType.Supplier : (byte)OpenItemPartyType.Customer;
        var broughtForward = context.OpenItems.AsNoTracking().Where(o => o.CompanyId == scope.CompanyId && o.BranchId == scope.BranchId
            && o.PartyId == partyId && o.PartyType == partyType && o.CurrencyCode == currency
            && o.OriginType != (byte)OpenItemOriginType.CurrentYearInvoice && o.State != (byte)OpenItemState.Cancelled)
            .Select(o => new PartyEffect
            {
                EffectId = o.Id, IsReversal = false, DocumentId = o.OriginDocumentId ?? o.Id, JournalId = null, Source = ProfessionalReportSource.Settlement,
                // Open-item number columns carry the database default collation; document numbers elsewhere are BIN2,
                // so the union branch must collate explicitly or SQL Server rejects the UNION ALL.
                PatternRevisionId = null, Number = EF.Functions.Collate(o.OriginDocumentNumber ?? o.SourceReference, "Latin1_General_100_BIN2"), DateUtc = o.OriginDocumentDateUtc,
                CreatedUtc = o.CreatedAtUtc,
                Description = o.OriginType == (byte)OpenItemOriginType.PriorYearInvoiceRollover ? "رصيد مرحّل من سنة مالية سابقة"
                    : o.OriginType == (byte)OpenItemOriginType.OpeningBalance ? "رصيد افتتاحي" : "رصيد منتقل من بيانات الترحيل",
                Debit = supplier ? 0m : o.OriginalAmount, Credit = supplier ? o.OriginalAmount : 0m,
            });
        return invoices.Concat(direct).Concat(directReversed).Concat(distributions).Concat(distributionReversed).Concat(settled).Concat(unsettled).Concat(broughtForward);
    }
}
