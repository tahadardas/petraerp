using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.Reports;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>Fifth batch: pending documents, supplier price analysis, unallocated payments, settlement movements and reversed entries.</summary>
public sealed partial class SqlLedgerQueryService
{
    private static (DateTimeOffset From, DateTimeOffset To) UtcRange(AnalyticalQuery q)
    {
        var (start, end) = Range(q);
        var from = start == DateOnly.MinValue ? DateTimeOffset.MinValue : new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = end == DateOnly.MaxValue ? DateTimeOffset.MaxValue : new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return (from, to);
    }

    private async Task<AnalyticalReport> PendingSalesInvoicesAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (from, to) = UtcRange(q);
        var rows = context.SalesInvoices.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId && i.ReturnOfInvoiceId == null && i.Status <= 1 && i.InvoiceDateUtc >= from && i.InvoiceDateUtc < to);
        if (!scope.Consolidated) rows = rows.Where(i => i.BranchId == scope.BranchId);
        var list = await rows.Join(context.Parties.AsNoTracking(), i => i.CustomerId, p => p.Id, (i, p) => new { i.InvoiceNumber, i.InvoiceDateUtc, i.Status, i.CurrencyCode, i.GrandTotal, p.Code, p.NameArabic })
            .OrderBy(x => x.InvoiceDateUtc).Take(DetailRowLimit).ToListAsync(ct).ConfigureAwait(false);
        var data = list.Select(x => (IReadOnlyList<string>)[x.InvoiceNumber ?? "—", D(DateOnly.FromDateTime(x.InvoiceDateUtc.UtcDateTime)), x.Code + " " + x.NameArabic, x.Status == 0 ? "مسودة" : "معتمدة غير مرحّلة", x.CurrencyCode, A(x.GrandTotal)]).ToList();
        return new("SALES-11", "فواتير مبيعات معلقة", "Pending sales invoices", baseCode,
            [Col("no", "الرقم", "Number"), Col("date", "التاريخ", "Date", Dt), Col("party", "الزبون", "Customer"), Col("state", "الحالة", "Status"), Col("cur", "العملة", "Currency"), Col("total", "الإجمالي", "Total", Amt)],
            data, null, ["المسودات والمعتمدة غير المرحَّلة؛ لا قيود محاسبية لها بعد. المبالغ بعملة المستند ولا تُجمع عبر العملات."]);
    }

    private async Task<AnalyticalReport> PendingPurchaseInvoicesAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (from, to) = UtcRange(q);
        var rows = context.PurchaseInvoices.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId && i.ReturnOfInvoiceId == null && i.Status <= 1 && i.InvoiceDateUtc >= from && i.InvoiceDateUtc < to);
        if (!scope.Consolidated) rows = rows.Where(i => i.BranchId == scope.BranchId);
        var list = await rows.Join(context.Parties.AsNoTracking(), i => i.SupplierId, p => p.Id, (i, p) => new { i.InvoiceNumber, i.SupplierReference, i.InvoiceDateUtc, i.Status, i.CurrencyCode, i.GrandTotal, p.Code, p.NameArabic })
            .OrderBy(x => x.InvoiceDateUtc).Take(DetailRowLimit).ToListAsync(ct).ConfigureAwait(false);
        var data = list.Select(x => (IReadOnlyList<string>)[x.InvoiceNumber ?? "—", x.SupplierReference ?? string.Empty, D(DateOnly.FromDateTime(x.InvoiceDateUtc.UtcDateTime)), x.Code + " " + x.NameArabic, x.Status == 0 ? "مسودة" : "معتمدة/غير مرحّلة", x.CurrencyCode, A(x.GrandTotal)]).ToList();
        return new("BUY-07", "فواتير مشتريات معلقة", "Pending purchase invoices", baseCode,
            [Col("no", "الرقم", "Number"), Col("ref", "مرجع المورد", "Supplier ref"), Col("date", "التاريخ", "Date", Dt), Col("party", "المورد", "Supplier"), Col("state", "الحالة", "Status"), Col("cur", "العملة", "Currency"), Col("total", "الإجمالي", "Total", Amt)],
            data, null, ["فواتير لم تُرحَّل بعد (حالة 0 أو 1). المبالغ بعملة المستند."]);
    }

    private async Task<AnalyticalReport> SupplierPriceAnalysisAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (from, to) = UtcRange(q);
        var invoices = context.PurchaseInvoices.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId && i.Status == 2 && i.ReturnOfInvoiceId == null && i.InvoiceDateUtc >= from && i.InvoiceDateUtc < to);
        if (!scope.Consolidated) invoices = invoices.Where(i => i.BranchId == scope.BranchId);
        var rows = await invoices.Join(context.PurchaseInvoiceLines.AsNoTracking(), i => i.Id, l => l.PurchaseInvoiceId, (i, l) => new { i.SupplierId, i.CurrencyCode, l.ItemId, l.UnitCost, l.Quantity })
            .GroupBy(x => new { x.ItemId, x.SupplierId, x.CurrencyCode })
            .Select(g => new { g.Key.ItemId, g.Key.SupplierId, g.Key.CurrencyCode, Min = g.Min(x => x.UnitCost), Max = g.Max(x => x.UnitCost), Qty = g.Sum(x => x.Quantity), Value = g.Sum(x => x.UnitCost * x.Quantity), Lines = g.Count() })
            .Take(DetailRowLimit).ToListAsync(ct).ConfigureAwait(false);
        var items = await context.Items.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.NameArabic, ct).ConfigureAwait(false);
        var parties = await context.Parties.AsNoTracking().Where(p => p.CompanyId == scope.CompanyId).ToDictionaryAsync(p => p.Id, p => p.Code + " " + p.NameArabic, ct).ConfigureAwait(false);
        string N(decimal v) => v.ToString("0.0000", CultureInfo.InvariantCulture);
        var data = rows.OrderBy(r => items.GetValueOrDefault(r.ItemId, string.Empty), StringComparer.Ordinal).ThenBy(r => r.Min)
            .Select(r => (IReadOnlyList<string>)[items.GetValueOrDefault(r.ItemId, string.Empty), parties.GetValueOrDefault(r.SupplierId, string.Empty), r.CurrencyCode, N(r.Min), N(r.Max), N(r.Qty == 0 ? 0m : r.Value / r.Qty), N(r.Qty), r.Lines.ToString(CultureInfo.InvariantCulture)]).ToList();
        return new("BUY-08", "تحليل أسعار موردين", "Supplier price analysis", baseCode,
            [Col("item", "المادة", "Item"), Col("supplier", "المورد", "Supplier"), Col("cur", "العملة", "Currency"), Col("min", "أدنى سعر", "Min cost"), Col("max", "أعلى سعر", "Max cost"), Col("avg", "المتوسط المرجّح", "Weighted avg"), Col("qty", "الكمية", "Quantity"), Col("lines", "عدد الأسطر", "Lines")],
            data, null, ["فواتير شراء مرحَّلة (دون المرتجعات)، مجمَّعة حسب المادة والمورد وعملة المستند؛ الأسعار بعملة المستند دون تحويل."]);
    }

    private sealed record UnallocatedVoucher(string? VoucherNumber, byte Kind, DateTimeOffset VoucherDateUtc, string CurrencyCode, decimal Amount, Guid? PartyId, decimal Allocated)
    {
        public decimal Open => Amount - Allocated;
    }

    private async Task<List<UnallocatedVoucher>> UnallocatedVoucherRowsAsync(LedgerReportScope scope, AnalyticalQuery q, CancellationToken ct)
    {
        var (from, to) = UtcRange(q);
        var vouchers = context.Vouchers.AsNoTracking().Where(v => v.CompanyId == scope.CompanyId && v.Status == 2 && v.PartyId != null && v.VoucherDateUtc >= from && v.VoucherDateUtc < to);
        if (!scope.Consolidated) vouchers = vouchers.Where(v => v.BranchId == scope.BranchId);
        var rows = await vouchers.Select(v => new UnallocatedVoucher(v.VoucherNumber, v.Kind, v.VoucherDateUtc, v.CurrencyCode, v.Amount, v.PartyId,
            (context.VoucherAllocations.Where(a => a.VoucherId == v.Id).Sum(a => (decimal?)a.Amount) ?? 0m)
            + (context.VoucherLines.Where(l => l.VoucherId == v.Id && l.PartyId != null).Sum(l => (decimal?)l.Amount) ?? 0m)
            + (context.SettlementApplications.Where(a => a.VoucherId == v.Id && a.Batch!.ReversedBy == null).Sum(a => (decimal?)a.SettlementAmount) ?? 0m))).ToListAsync(ct).ConfigureAwait(false);
        return rows.Where(r => r.Open > 0m).OrderBy(r => r.VoucherDateUtc).Take(DetailRowLimit).ToList();
    }

    private async Task<AnalyticalReport> UnallocatedVouchersAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var rows = await UnallocatedVoucherRowsAsync(scope, q, ct).ConfigureAwait(false);
        var parties = await context.Parties.AsNoTracking().Where(p => p.CompanyId == scope.CompanyId).ToDictionaryAsync(p => p.Id, p => p.Code + " " + p.NameArabic, ct).ConfigureAwait(false);
        var data = rows.Select(x => (IReadOnlyList<string>)[x.VoucherNumber ?? "—", x.Kind == 1 ? "سند قبض" : "سند دفع", D(DateOnly.FromDateTime(x.VoucherDateUtc.UtcDateTime)), parties.GetValueOrDefault(x.PartyId ?? Guid.Empty, string.Empty), x.CurrencyCode, A(x.Amount), A(x.Allocated), A(x.Open)]).ToList();
        return new("ARAP-06", "دفعات غير مخصصة", "Unallocated payments", baseCode,
            [Col("no", "الرقم", "Number"), Col("kind", "النوع", "Kind"), Col("date", "التاريخ", "Date", Dt), Col("party", "الطرف", "Party"), Col("cur", "العملة", "Currency"), Col("amount", "المبلغ", "Amount", Amt), Col("alloc", "المخصص", "Allocated", Amt), Col("open", "غير المخصص", "Unallocated", Amt)],
            data, null, ["سندات مرحَّلة لطرف: المخصص = تخصيصات الفواتير + أسطر التوزيع على أطراف + تطبيقات التسوية غير المعكوسة؛ ما بقي يُعرض كغير مخصص (سلفة محتملة). المبالغ بعملة السند."]);
    }

    private async Task<AnalyticalReport> SettlementMovementsAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (from, to) = UtcRange(q);
        var batches = context.SettlementBatches.AsNoTracking().Where(b => b.CompanyId == scope.CompanyId && b.DateUtc >= from && b.DateUtc < to);
        if (!scope.Consolidated) batches = batches.Where(b => b.BranchId == scope.BranchId);
        var rows = await batches.Select(b => new
        {
            b.DateUtc, b.Reason, b.IsInitial, b.ReversedBy, b.ReversedAtUtc, b.ReversalReason,
            Voucher = b.Voucher!.VoucherNumber, Total = b.Applications.Sum(a => (decimal?)a.SettlementAmount) ?? 0m, Count = b.Applications.Count,
        }).OrderBy(b => b.DateUtc).Take(DetailRowLimit).ToListAsync(ct).ConfigureAwait(false);
        var data = rows.Select(r => (IReadOnlyList<string>)[D(DateOnly.FromDateTime(r.DateUtc.UtcDateTime)), r.Voucher ?? "—", r.IsInitial ? "أولية" : "إعادة تسوية", r.ReversedBy is null ? "قائمة" : "مفكوكة", r.Count.ToString(CultureInfo.InvariantCulture), A(r.Total), r.Reason, r.ReversalReason ?? string.Empty]).ToList();
        return new("ARAP-11", "حركات ربط/فك تسوية", "Settlement link / unlink movements", baseCode,
            [Col("date", "التاريخ", "Date", Dt), Col("voucher", "السند", "Voucher"), Col("kind", "النوع", "Kind"), Col("state", "الحالة", "State"), Col("apps", "عدد التطبيقات", "Applications"), Col("total", "المجموع", "Total", Amt), Col("reason", "السبب", "Reason"), Col("unreason", "سبب الفك", "Unlink reason")],
            data, null, ["كل دفعة تسوية مسجّلة وحالتها؛ الفك يعكس القيد ولا يحذف السجل. المجموع بعملة التسوية."]);
    }

    private async Task<AnalyticalReport> ReversedEntriesAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (start, end) = Range(q);
        var entries = context.JournalEntries.AsNoTracking().Where(e => e.CompanyId == scope.CompanyId && e.AccountingDate >= start && e.AccountingDate <= end && (e.State == 5 || e.ReversesEntryId != null || e.CorrectsEntryId != null));
        if (!scope.Consolidated) entries = entries.Where(e => e.BranchId == scope.BranchId);
        var rows = await entries.OrderBy(e => e.AccountingDate).ThenBy(e => e.PostingSequence).Take(DetailRowLimit)
            .Select(e => new { e.AccountingDate, e.PostingSequence, e.Description, e.State, e.ReversesEntryId, e.ReversedByEntryId, e.CorrectsEntryId }).ToListAsync(ct).ConfigureAwait(false);
        string Kind(byte state, Guid? reverses, Guid? corrects) => reverses is not null ? "قيد عكسي" : corrects is not null ? "قيد تصحيح" : state == 5 ? "قيد معكوس" : "—";
        var data = rows.Select(r => (IReadOnlyList<string>)[D(r.AccountingDate), r.PostingSequence?.ToString(CultureInfo.InvariantCulture) ?? "—", Kind(r.State, r.ReversesEntryId, r.CorrectsEntryId), r.Description]).ToList();
        return new("GL-11", "قيود معدّلة/مرفوضة", "Modified / reversed entries", baseCode,
            [Col("date", "التاريخ", "Date", Dt), Col("seq", "التسلسل", "Sequence"), Col("kind", "النوع", "Kind"), Col("desc", "البيان", "Description")],
            data, null, ["القيود المعكوسة وقيود العكس والتصحيح فقط. الرفض قبل الترحيل لا يُخزَّن كحالة في دفتر الأستاذ، لذلك لا تظهر المرفوضات هنا."]);
    }
}
