using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports.Professional;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Reports.Professional;

public sealed partial class SqlProfessionalReportService
{
    private sealed record SourceLink
    {
        public Guid JournalId { get; init; }
        public ProfessionalReportSource Source { get; init; }
        public Guid DocumentId { get; init; }
        public string? Number { get; init; }
        public Guid? PatternRevisionId { get; init; }
    }

    private IQueryable<SourceLink> Sources(LedgerReportScope scope)
    {
        var sales = context.SalesInvoices.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId && i.BranchId == scope.BranchId && i.JournalEntryId != null)
            .Select(i => new SourceLink { JournalId = i.JournalEntryId!.Value, Source = i.ReturnOfInvoiceId != null || i.ReversesInvoiceId != null ? ProfessionalReportSource.SalesReturn : ProfessionalReportSource.SalesInvoice, DocumentId = i.Id, Number = i.InvoiceNumber, PatternRevisionId = i.PatternRevisionId });
        var purchases = context.PurchaseInvoices.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId && i.BranchId == scope.BranchId && i.JournalEntryId != null)
            .Select(i => new SourceLink { JournalId = i.JournalEntryId!.Value, Source = i.ReturnOfInvoiceId == null && i.ReversesInvoiceId == null ? ProfessionalReportSource.PurchaseInvoice : ProfessionalReportSource.PurchaseReturn, DocumentId = i.Id, Number = i.InvoiceNumber, PatternRevisionId = i.PatternRevisionId });
        var vouchers = context.Vouchers.AsNoTracking().Where(v => v.CompanyId == scope.CompanyId && v.BranchId == scope.BranchId);
        var voucherPosted = vouchers.Where(v => v.JournalEntryId != null).Select(v => new SourceLink { JournalId = v.JournalEntryId!.Value, Source = v.Kind == 1 ? ProfessionalReportSource.ReceiptVoucher : ProfessionalReportSource.PaymentVoucher, DocumentId = v.Id, Number = v.VoucherNumber, PatternRevisionId = v.PatternRevisionId });
        var voucherReversed = vouchers.Where(v => v.ReversalJournalEntryId != null).Select(v => new SourceLink { JournalId = v.ReversalJournalEntryId!.Value, Source = v.Kind == 1 ? ProfessionalReportSource.ReceiptVoucher : ProfessionalReportSource.PaymentVoucher, DocumentId = v.Id, Number = v.VoucherNumber, PatternRevisionId = v.PatternRevisionId });
        var inventory = context.InventoryDocuments.AsNoTracking().Where(d => d.CompanyId == scope.CompanyId && d.BranchId == scope.BranchId);
        var inventoryPosted = inventory.Where(d => d.JournalId != null).Select(d => new SourceLink { JournalId = d.JournalId!.Value, Source = ProfessionalReportSource.InventoryDocument, DocumentId = d.Id, Number = d.Number, PatternRevisionId = null });
        var inventoryReversed = inventory.Where(d => d.ReversalJournalId != null).Select(d => new SourceLink { JournalId = d.ReversalJournalId!.Value, Source = ProfessionalReportSource.InventoryDocument, DocumentId = d.Id, Number = d.Number, PatternRevisionId = null });
        var batches = context.SettlementBatches.AsNoTracking().Where(b => b.CompanyId == scope.CompanyId && b.BranchId == scope.BranchId);
        var settled = batches.Where(b => b.JournalId != null && !vouchers.Any(v => v.JournalEntryId == b.JournalId))
            .Select(b => new SourceLink { JournalId = b.JournalId!.Value, Source = ProfessionalReportSource.Settlement, DocumentId = b.VoucherId, Number = b.Voucher!.VoucherNumber, PatternRevisionId = b.Voucher.PatternRevisionId });
        var unsettled = batches.Where(b => b.ReversalJournalId != null && !vouchers.Any(v => v.ReversalJournalEntryId == b.ReversalJournalId))
            .Select(b => new SourceLink { JournalId = b.ReversalJournalId!.Value, Source = ProfessionalReportSource.Settlement, DocumentId = b.VoucherId, Number = b.Voucher!.VoucherNumber, PatternRevisionId = b.Voucher.PatternRevisionId });
        return sales.Concat(purchases).Concat(voucherPosted).Concat(voucherReversed).Concat(inventoryPosted).Concat(inventoryReversed).Concat(settled).Concat(unsettled);
    }

    private IQueryable<JournalLineRow> FilterLines(IQueryable<JournalLineRow> query, LedgerReportScope scope, ProfessionalReportRequest request,
        ProfessionalReportSource[] patternSources)
    {
        // String.Contains translates to a parameterized SQL Unicode substring search. SQL wildcard characters remain literal.
        if (request.DescriptionContains is { } contains)
            query = query.Where(l => l.Description.Contains(contains) || l.Journal!.Description.Contains(contains));
        if (request.DescriptionNotContains is { } excludes)
            query = query.Where(l => !l.Description.Contains(excludes) && !l.Journal!.Description.Contains(excludes));
        if (request.CreatedFrom is { } createdFrom) { var begin = Start(createdFrom); query = query.Where(l => l.Journal!.CreatedAtUtc >= begin); }
        if (request.CreatedTo is { } createdTo) { var finish = End(createdTo); query = query.Where(l => l.Journal!.CreatedAtUtc < finish); }
        if (request.PostingFrom is { } postingFrom)
        {
            var begin = Start(postingFrom);
            query = query.Where(l => l.Journal!.AuditEvents.Any(a => a.Action == (byte)PetraERP.Domain.GeneralLedger.JournalAuditAction.Posted && a.OccurredAtUtc >= begin));
        }
        if (request.PostingTo is { } postingTo)
        {
            var finish = End(postingTo);
            query = query.Where(l => l.Journal!.AuditEvents.Any(a => a.Action == (byte)PetraERP.Domain.GeneralLedger.JournalAuditAction.Posted && a.OccurredAtUtc < finish));
        }
        if (request.FromSequence is { } first) query = query.Where(l => l.Journal!.PostingSequence >= first);
        if (request.ToSequence is { } last) query = query.Where(l => l.Journal!.PostingSequence <= last);
        if (request.OppositeAccountId is { } opposite)
            query = query.Where(l => l.Journal!.Lines.Any(other => other.Id != l.Id && other.AccountId == opposite
                && (l.BaseDebit > 0m && other.BaseCredit > 0m || l.BaseCredit > 0m && other.BaseDebit > 0m)));
        var sources = request.Sources.ToArray();
        var allSources = Enum.GetValues<ProfessionalReportSource>();
        var includesAllSources = sources.Length >= allSources.Length && allSources.All(sources.Contains);
        var links = includesAllSources && patternSources.Length == 0 ? null : Sources(scope);

        if (links is not null)
        {
            if (!includesAllSources)
            {
                var unclassified = sources.Contains(ProfessionalReportSource.UnclassifiedJournal);
                query = query.Where(l => links.Any(s => s.JournalId == l.JournalId && sources.Contains(s.Source))
                    || unclassified && !links.Any(s => s.JournalId == l.JournalId));
            }
            if (patternSources.Length > 0)
            {
                query = query.Where(l => !links.Any(s => s.JournalId == l.JournalId && patternSources.Contains(s.Source)
                    && (s.PatternRevisionId == null || !request.PatternRevisionIds.Contains(s.PatternRevisionId.Value))));
            }
        }
        return query;
    }
}
