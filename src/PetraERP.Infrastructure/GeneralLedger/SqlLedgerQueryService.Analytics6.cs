using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.Reports;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>Sixth batch: due open items, advances, monthly collections, cheque movements, voucher list and inventory turnover.</summary>
public sealed partial class SqlLedgerQueryService
{
    private async Task<AnalyticalReport> DueOpenItemsAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var asOf = q.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var asOfUtc = new DateTimeOffset(asOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var items = context.OpenItems.AsNoTracking().Where(o => o.CompanyId == scope.CompanyId && o.OutstandingAmount > 0m && o.State != 4 && o.DueDateUtc != null);
        if (!scope.Consolidated) items = items.Where(o => o.BranchId == scope.BranchId);
        var rows = await items.Join(context.Parties.AsNoTracking(), o => o.PartyId, p => p.Id, (o, p) => new { o.PartyType, o.OriginDocumentNumber, o.DueDateUtc, o.CurrencyCode, o.OutstandingAmount, o.CurrentBaseCarryingAmount, p.Code, p.NameArabic })
            .OrderBy(x => x.DueDateUtc).Take(DetailRowLimit).ToListAsync(ct).ConfigureAwait(false);
        var data = rows.Select(x =>
        {
            var due = x.DueDateUtc!.Value;
            var days = (int)Math.Floor((asOfUtc - due).TotalDays);
            return (IReadOnlyList<string>)[x.PartyType == 1 ? "زبون" : "مورد", x.Code + " " + x.NameArabic, x.OriginDocumentNumber ?? "—", D(DateOnly.FromDateTime(due.UtcDateTime)),
                days > 0 ? days.ToString(CultureInfo.InvariantCulture) : "0", x.CurrencyCode, A(x.OutstandingAmount), A(x.CurrentBaseCarryingAmount)];
        }).ToList();
        return new("ARAP-05", "فواتير مستحقة", "Due invoices", baseCode,
            [Col("type", "النوع", "Type"), Col("party", "الطرف", "Party"), Col("doc", "المستند", "Document"), Col("due", "الاستحقاق", "Due", Dt), Col("late", "أيام التأخر", "Days late"), Col("cur", "العملة", "Currency"), Col("out", "المتبقي", "Outstanding", Amt), Col("base", "بالأساسية", "Base", Amt)],
            data, ["المجموع بالعملة الأساسية", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, A(rows.Sum(x => x.CurrentBaseCarryingAmount))],
            [string.Create(CultureInfo.InvariantCulture, $"البنود المفتوحة ذات تاريخ استحقاق حتى {asOf:yyyy-MM-dd} (أيام التأخر محسوبة إلى هذا التاريخ)؛ المتبقي بعملة البند والمجموع بالأساسية.")]);
    }

    private async Task<AnalyticalReport> AdvancesAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var rows = await UnallocatedVoucherRowsAsync(scope, q, ct).ConfigureAwait(false);
        var parties = await context.Parties.AsNoTracking().Where(p => p.CompanyId == scope.CompanyId).ToDictionaryAsync(p => p.Id, p => p.Code + " " + p.NameArabic, ct).ConfigureAwait(false);
        var data = rows.GroupBy(r => (r.PartyId, r.Kind, r.CurrencyCode)).OrderBy(g => parties.GetValueOrDefault(g.Key.PartyId ?? Guid.Empty, string.Empty), StringComparer.Ordinal)
            .Select(g => (IReadOnlyList<string>)[parties.GetValueOrDefault(g.Key.PartyId ?? Guid.Empty, string.Empty), g.Key.Kind == 1 ? "سلفة زبون (قبض)" : "دفعة مقدمة لمورد (دفع)", g.Key.CurrencyCode, g.Count().ToString(CultureInfo.InvariantCulture), A(g.Sum(r => r.Open))]).ToList();
        return new("ARAP-07", "سلف ومدفوعات مقدمة", "Advances and prepayments", baseCode,
            [Col("party", "الطرف", "Party"), Col("kind", "النوع", "Kind"), Col("cur", "العملة", "Currency"), Col("count", "عدد السندات", "Vouchers"), Col("open", "الرصيد غير المخصص", "Unallocated", Amt)],
            data, null, ["تجميع لسندات ARAP-06 حسب الطرف والنوع والعملة؛ لا يُجمع عبر العملات."]);
    }

    private async Task<AnalyticalReport> MonthlyCollectionsAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (from, to) = UtcRange(q);
        var vouchers = context.Vouchers.AsNoTracking().Where(v => v.CompanyId == scope.CompanyId && v.Status == 2 && v.PartyId != null && v.VoucherDateUtc >= from && v.VoucherDateUtc < to);
        if (!scope.Consolidated) vouchers = vouchers.Where(v => v.BranchId == scope.BranchId);
        var rows = await vouchers.Select(v => new { v.Kind, v.VoucherDateUtc, v.CurrencyCode, v.Amount }).ToListAsync(ct).ConfigureAwait(false);
        var data = rows.GroupBy(r => (r.VoucherDateUtc.UtcDateTime.Year, r.VoucherDateUtc.UtcDateTime.Month, r.CurrencyCode)).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).ThenBy(g => g.Key.CurrencyCode, StringComparer.Ordinal)
            .Select(g =>
            {
                var rec = g.Where(r => r.Kind == 1).Sum(r => r.Amount);
                var pay = g.Where(r => r.Kind == 2).Sum(r => r.Amount);
                return (IReadOnlyList<string>)[string.Create(CultureInfo.InvariantCulture, $"{g.Key.Year}-{g.Key.Month:00}"), g.Key.CurrencyCode, A(rec), A(pay), A(rec - pay)];
            }).ToList();
        return new("ARAP-08", "ملخص تحصيل شهري", "Monthly collection summary", baseCode,
            [Col("month", "الشهر", "Month"), Col("cur", "العملة", "Currency"), Col("rec", "المقبوضات", "Receipts", Amt), Col("pay", "المدفوعات", "Payments", Amt), Col("net", "الصافي", "Net", Amt)],
            data, null, ["سندات القبض والدفع المرحَّلة المرتبطة بطرف، حسب شهر السند وعملته؛ لا تحويل بين العملات."]);
    }

    private async Task<AnalyticalReport> ChequeMovementsAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (start, end) = Range(q);
        var papers = context.CommercialPapers.AsNoTracking().Where(p => p.CompanyId == scope.CompanyId && p.StatusDate >= start && p.StatusDate <= end);
        if (!scope.Consolidated) papers = papers.Where(p => p.BranchId == scope.BranchId);
        var rows = await papers.OrderBy(p => p.StatusDate).ThenBy(p => p.PaperNumber).Take(DetailRowLimit)
            .Select(p => new { p.PaperKind, p.PaperNumber, p.BankName, p.DueDate, p.StatusDate, p.Status, p.CurrencyCode, p.Amount, p.BaseAmount }).ToListAsync(ct).ConfigureAwait(false);
        var data = rows.Select(p => (IReadOnlyList<string>)[p.PaperNumber, p.PaperKind.ToString(CultureInfo.InvariantCulture), p.BankName, D(p.DueDate), D(p.StatusDate), p.Status.ToString(CultureInfo.InvariantCulture), p.CurrencyCode, A(p.Amount), A(p.BaseAmount)]).ToList();
        return new("CASH-05", "حركات شيكات", "Cheque movements", baseCode,
            [Col("no", "الرقم", "Number"), Col("kind", "النوع (رمز)", "Kind (code)"), Col("bank", "المصرف", "Bank"), Col("due", "الاستحقاق", "Due", Dt), Col("sdate", "تاريخ الحالة", "Status date", Dt), Col("status", "الحالة (رمز)", "Status (code)"), Col("cur", "العملة", "Currency"), Col("amount", "المبلغ", "Amount", Amt), Col("base", "بالأساسية", "Base", Amt)],
            data, ["المجموع بالأساسية", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, A(rows.Sum(p => p.BaseAmount))],
            ["الأوراق التجارية التي تغيّرت حالتها في الفترة (حسب تاريخ الحالة الحالية، لا سجل كل التحولات). النوع والحالة بأرقام التعداد المخزَّنة."]);
    }

    private async Task<AnalyticalReport> VoucherListAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (from, to) = UtcRange(q);
        var vouchers = context.Vouchers.AsNoTracking().Where(v => v.CompanyId == scope.CompanyId && v.VoucherDateUtc >= from && v.VoucherDateUtc < to);
        if (!scope.Consolidated) vouchers = vouchers.Where(v => v.BranchId == scope.BranchId);
        var rows = await vouchers.OrderBy(v => v.VoucherDateUtc).ThenBy(v => v.VoucherNumber).Take(DetailRowLimit)
            .Select(v => new { v.VoucherNumber, v.Kind, v.Status, v.VoucherDateUtc, v.CurrencyCode, v.Amount, v.PartyId }).ToListAsync(ct).ConfigureAwait(false);
        var parties = await context.Parties.AsNoTracking().Where(p => p.CompanyId == scope.CompanyId).ToDictionaryAsync(p => p.Id, p => p.Code + " " + p.NameArabic, ct).ConfigureAwait(false);
        static string State(byte s) => s switch { 0 => "مسودة", 1 => "معتمد", 2 => "مرحّل", 3 => "معكوس", _ => "—" };
        var data = rows.Select(v => (IReadOnlyList<string>)[v.VoucherNumber ?? "—", v.Kind == 1 ? "قبض" : "دفع", State(v.Status), D(DateOnly.FromDateTime(v.VoucherDateUtc.UtcDateTime)), parties.GetValueOrDefault(v.PartyId ?? Guid.Empty, string.Empty), v.CurrencyCode, A(v.Amount)]).ToList();
        return new("CASH-07", "كشف سندات قبض/دفع", "Receipt / payment voucher list", baseCode,
            [Col("no", "الرقم", "Number"), Col("kind", "النوع", "Kind"), Col("state", "الحالة", "State"), Col("date", "التاريخ", "Date", Dt), Col("party", "الطرف", "Party"), Col("cur", "العملة", "Currency"), Col("amount", "المبلغ", "Amount", Amt)],
            data, null, ["كل السندات بكل حالاتها في الفترة؛ المبالغ بعملة السند ولا تُجمع."]);
    }

    private async Task<AnalyticalReport> InventoryTurnoverAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (from, to) = UtcRange(q);
        var movements = ScopedMovements(scope).Where(x => x.MovementDateUtc < to);
        var agg = await movements.GroupBy(x => x.ItemId).Select(g => new
        {
            ItemId = g.Key,
            Opening = g.Sum(x => x.MovementDateUtc < from ? x.BaseQuantity : 0m),
            Closing = g.Sum(x => x.BaseQuantity),
            Out = g.Sum(x => x.MovementDateUtc >= from && x.BaseQuantity < 0m ? -x.BaseQuantity : 0m),
        }).Where(x => x.Out > 0m).ToListAsync(ct).ConfigureAwait(false);
        var items = await context.Items.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.NameArabic, ct).ConfigureAwait(false);
        string N(decimal v) => v.ToString("0.0000", CultureInfo.InvariantCulture);
        var data = agg.OrderByDescending(x => x.Out).Select(x =>
        {
            var avg = (x.Opening + x.Closing) / 2m;
            return (IReadOnlyList<string>)[items.GetValueOrDefault(x.ItemId, x.ItemId.ToString()), N(x.Opening), N(x.Closing), N(x.Out), avg > 0m ? N(x.Out / avg) : "—"];
        }).ToList();
        return new("INV-14", "دوران المخزون", "Inventory turnover", baseCode,
            [Col("item", "المادة", "Item"), Col("open", "رصيد أول", "Opening", AnalyticalColumnKind.Quantity), Col("close", "رصيد آخر", "Closing", AnalyticalColumnKind.Quantity), Col("out", "المصروف", "Issued", AnalyticalColumnKind.Quantity), Col("turn", "معدل الدوران", "Turnover")],
            data, null, ["دوران كمّي = الكمية الأساسية الخارجة ÷ متوسط (رصيد أول + رصيد آخر)/2؛ لا يستعمل القيمة. «—» حين المتوسط صفر أو سالب."]);
    }
}
