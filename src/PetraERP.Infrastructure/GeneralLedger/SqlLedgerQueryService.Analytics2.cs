using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>Second batch of analytical reports: branch results, role-based sales/purchase trends, unposted entries, FX rate movement and stock exceptions.</summary>
public sealed partial class SqlLedgerQueryService
{
    private async Task<AnalyticalReport> BranchResultsAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var start = q.From ?? DateOnly.MinValue; var to = q.To ?? DateOnly.MaxValue;
        var rows = await ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate >= start && l.Journal.AccountingDate <= to)
            .Join(context.Accounts.AsNoTracking(), l => l.AccountId, a => a.Id, (l, a) => new { l.Journal!.BranchId, a.AccountType, l.BaseDebit, l.BaseCredit })
            .Where(x => x.AccountType == 4 || x.AccountType == 5)
            .GroupBy(x => x.BranchId)
            .Select(g => new { BranchId = g.Key, Revenue = g.Sum(x => x.AccountType == 4 ? x.BaseCredit - x.BaseDebit : 0m), Expense = g.Sum(x => x.AccountType == 5 ? x.BaseDebit - x.BaseCredit : 0m) })
            .ToListAsync(ct).ConfigureAwait(false);
        var names = await context.Branches.AsNoTracking().Where(b => b.CompanyId == scope.CompanyId).ToDictionaryAsync(b => b.Id, b => b.Code + " " + b.NameArabic, ct).ConfigureAwait(false);
        var data = rows.OrderBy(r => names.GetValueOrDefault(r.BranchId, string.Empty), StringComparer.Ordinal)
            .Select(r => (IReadOnlyList<string>)[names.GetValueOrDefault(r.BranchId, r.BranchId.ToString()), A(r.Revenue), A(r.Expense), A(r.Revenue - r.Expense)]).ToList();
        return new("FIN-09", "نتائج الفروع", "Branch results", baseCode,
            [Col("branch", "الفرع", "Branch"), Col("rev", "الإيرادات", "Revenue", Amt), Col("exp", "المصاريف", "Expenses", Amt), Col("net", "الصافي", "Net", Amt)],
            data, ["المجموع", A(rows.Sum(r => r.Revenue)), A(rows.Sum(r => r.Expense)), A(rows.Sum(r => r.Revenue - r.Expense))],
            ["الأرقام من القيود المرحّلة لحسابات الإيرادات والمصاريف فقط؛ لا يوجد توزيع للمصاريف المشتركة بين الفروع."]);
    }

    private async Task<HashSet<Guid>> RoleAccountIdsAsync(Guid companyId, byte role, CancellationToken ct) =>
        (await context.BranchAccountLinks.AsNoTracking().Where(l => l.CompanyId == companyId && l.Role == role).Select(l => l.AccountId).Distinct().ToListAsync(ct).ConfigureAwait(false)).ToHashSet();

    private async Task<AnalyticalReport> MonthlyRoleAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, string id, string ar, string en, byte mainRole, byte returnRole, bool creditNormal, CancellationToken ct)
    {
        var main = await RoleAccountIdsAsync(scope.CompanyId, mainRole, ct).ConfigureAwait(false);
        var returns = await RoleAccountIdsAsync(scope.CompanyId, returnRole, ct).ConfigureAwait(false);
        if (main.Count == 0) throw new BusinessRuleException("report.no_role_accounts", "Map the branch account roles first (branch account mappings).", "accountId");
        var start = q.From ?? new DateOnly(DateTime.UtcNow.Year, 1, 1); var to = q.To ?? start.AddYears(1).AddDays(-1);
        var lines = await ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate >= start && l.Journal.AccountingDate <= to && (main.Contains(l.AccountId) || returns.Contains(l.AccountId)))
            .Select(l => new { l.Journal!.AccountingDate, l.AccountId, l.BaseDebit, l.BaseCredit }).ToListAsync(ct).ConfigureAwait(false);
        decimal Net(decimal d, decimal c) => creditNormal ? c - d : d - c;
        var data = lines.GroupBy(l => (l.AccountingDate.Year, l.AccountingDate.Month)).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).Select(g =>
        {
            var m = g.Where(l => main.Contains(l.AccountId)).Sum(l => Net(l.BaseDebit, l.BaseCredit));
            var r = -g.Where(l => returns.Contains(l.AccountId)).Sum(l => Net(l.BaseDebit, l.BaseCredit));
            return (IReadOnlyList<string>)[string.Create(CultureInfo.InvariantCulture, $"{g.Key.Year}-{g.Key.Month:00}"), A(m), A(r), A(m - r)];
        }).ToList();
        var tm = lines.Where(l => main.Contains(l.AccountId)).Sum(l => Net(l.BaseDebit, l.BaseCredit));
        var tr = -lines.Where(l => returns.Contains(l.AccountId)).Sum(l => Net(l.BaseDebit, l.BaseCredit));
        return new(id, ar, en, baseCode,
            [Col("month", "الشهر", "Month"), Col("main", creditNormal ? "المبيعات" : "المشتريات", creditNormal ? "Sales" : "Purchases", Amt), Col("ret", "المردودات", "Returns", Amt), Col("net", "الصافي", "Net", Amt)],
            data, ["المجموع", A(tm), A(tr), A(tm - tr)], ["الحسابات المستخدمة هي المربوطة بدور الفرع (مبيعات/مردودات أو مشتريات/مردودات) كما في ربط حسابات الفروع."]);
    }

    private async Task<AnalyticalReport> BranchRoleAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var sales = await RoleAccountIdsAsync(scope.CompanyId, 3, ct).ConfigureAwait(false);
        var returns = await RoleAccountIdsAsync(scope.CompanyId, 4, ct).ConfigureAwait(false);
        if (sales.Count == 0) throw new BusinessRuleException("report.no_role_accounts", "Map the branch account roles first (branch account mappings).", "accountId");
        var start = q.From ?? DateOnly.MinValue; var to = q.To ?? DateOnly.MaxValue;
        var lines = await ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate >= start && l.Journal.AccountingDate <= to && (sales.Contains(l.AccountId) || returns.Contains(l.AccountId)))
            .Select(l => new { l.Journal!.BranchId, l.AccountId, l.BaseDebit, l.BaseCredit }).ToListAsync(ct).ConfigureAwait(false);
        var names = await context.Branches.AsNoTracking().Where(b => b.CompanyId == scope.CompanyId).ToDictionaryAsync(b => b.Id, b => b.Code + " " + b.NameArabic, ct).ConfigureAwait(false);
        var data = lines.GroupBy(l => l.BranchId).OrderBy(g => names.GetValueOrDefault(g.Key, string.Empty), StringComparer.Ordinal).Select(g =>
        {
            var m = g.Where(l => sales.Contains(l.AccountId)).Sum(l => l.BaseCredit - l.BaseDebit);
            var r = g.Where(l => returns.Contains(l.AccountId)).Sum(l => l.BaseDebit - l.BaseCredit);
            return (IReadOnlyList<string>)[names.GetValueOrDefault(g.Key, g.Key.ToString()), A(m), A(r), A(m - r)];
        }).ToList();
        var tm = lines.Where(l => sales.Contains(l.AccountId)).Sum(l => l.BaseCredit - l.BaseDebit);
        var tr = lines.Where(l => returns.Contains(l.AccountId)).Sum(l => l.BaseDebit - l.BaseCredit);
        return new("SALES-03", "مبيعات بالفرع", "Sales by branch", baseCode,
            [Col("branch", "الفرع", "Branch"), Col("sales", "المبيعات", "Sales", Amt), Col("ret", "المردودات", "Returns", Amt), Col("net", "الصافي", "Net", Amt)],
            data, ["المجموع", A(tm), A(tr), A(tm - tr)], []);
    }

    private async Task<AnalyticalReport> UnpostedEntriesAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var start = q.From ?? DateOnly.MinValue; var to = q.To ?? DateOnly.MaxValue;
        var states = new byte[] { 1, 2, 3 };
        var rows = await ScopedLines(scope).Where(l => states.Contains(l.Journal!.State) && l.Journal.AccountingDate >= start && l.Journal.AccountingDate <= to)
            .GroupBy(l => new { l.JournalId, l.Journal!.AccountingDate, l.Journal.State, l.Journal.BranchId })
            .Select(g => new { g.Key.JournalId, g.Key.AccountingDate, g.Key.State, g.Key.BranchId, Debit = g.Sum(l => l.BaseDebit), Credit = g.Sum(l => l.BaseCredit) })
            .OrderBy(x => x.AccountingDate).ThenBy(x => x.JournalId).Take(2000).ToListAsync(ct).ConfigureAwait(false);
        static string State(byte s) => s switch { 1 => "مسودة", 2 => "مدقق", 3 => "معتمد", _ => s.ToString(CultureInfo.InvariantCulture) };
        var data = rows.Select(r => (IReadOnlyList<string>)[D(r.AccountingDate), State(r.State), r.JournalId.ToString("D"), A(r.Debit), A(r.Credit)]).ToList();
        return new("GL-12", "قيود غير مرحّلة", "Unposted entries", baseCode,
            [Col("date", "التاريخ", "Date", Dt), Col("state", "الحالة", "State"), Col("id", "معرّف القيد", "Journal id"), Col("debit", "مدين", "Debit", Amt), Col("credit", "دائن", "Credit", Amt)],
            data, null, rows.Count == 2000 ? ["عُرضت أول 2000 قيد فقط؛ ضيّق المدة."] : []);
    }

    private async Task<AnalyticalReport> RateMovementAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var start = q.From ?? DateOnly.MinValue; var to = q.To ?? DateOnly.MaxValue;
        var rates = context.ExchangeRates.AsNoTracking().Where(r => r.CompanyId == scope.CompanyId && r.EffectiveDate >= start && r.EffectiveDate <= to);
        if (!string.IsNullOrWhiteSpace(q.CurrencyCode)) { var c = q.CurrencyCode.Trim().ToUpperInvariant(); rates = rates.Where(r => r.CurrencyCode == c); }
        var list = await rates.OrderBy(r => r.CurrencyCode).ThenBy(r => r.EffectiveDate).ThenBy(r => r.CreatedAtUtc).Take(5000).ToListAsync(ct).ConfigureAwait(false);
        var data = new List<IReadOnlyList<string>>();
        foreach (var g in list.GroupBy(r => r.CurrencyCode))
        {
            decimal? prev = null;
            foreach (var r in g)
            {
                var change = prev is { } p && p != 0 ? (r.Rate - p) / p * 100m : (decimal?)null;
                data.Add([g.Key, D(r.EffectiveDate), r.Rate.ToString("0.00000000", CultureInfo.InvariantCulture), change is { } c ? c.ToString("0.0000", CultureInfo.InvariantCulture) : string.Empty, r.Source]);
                prev = r.Rate;
            }
        }

        return new("FX-09", "حركة أسعار الصرف", "Exchange rate movement", baseCode,
            [Col("cur", "العملة", "Currency"), Col("date", "التاريخ", "Date", Dt), Col("rate", "السعر", "Rate", Amt), Col("chg", "التغير %", "Change %", Amt), Col("src", "المصدر", "Source")],
            data, null, [$"الأسعار مقابل عملة الأساس {baseCode}؛ التغير نسبة إلى السعر السابق للعملة نفسها."]);
    }

    private IQueryable<Inventory.StockMovementRow> ScopedMovements(LedgerReportScope scope)
    {
        var m = context.StockMovements.AsNoTracking().Where(x => x.CompanyId == scope.CompanyId);
        return scope.Consolidated ? m : m.Where(x => x.BranchId == scope.BranchId);
    }

    private async Task<AnalyticalReport> NegativeStockAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var rows = await ScopedMovements(scope).Where(x => !q.To.HasValue || x.MovementDateUtc < new DateTimeOffset(q.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
            .GroupBy(x => new { x.WarehouseId, x.ItemId }).Select(g => new { g.Key.WarehouseId, g.Key.ItemId, Qty = g.Sum(x => x.BaseQuantity) })
            .Where(x => x.Qty < 0).ToListAsync(ct).ConfigureAwait(false);
        var items = await context.Items.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.NameArabic, ct).ConfigureAwait(false);
        var whs = await context.Warehouses.AsNoTracking().Where(w => w.CompanyId == scope.CompanyId).ToDictionaryAsync(w => w.Id, w => w.Code + " " + w.NameArabic, ct).ConfigureAwait(false);
        var data = rows.OrderBy(r => r.Qty).Select(r => (IReadOnlyList<string>)[whs.GetValueOrDefault(r.WarehouseId, r.WarehouseId.ToString()), items.GetValueOrDefault(r.ItemId, r.ItemId.ToString()), r.Qty.ToString("0.0000", CultureInfo.InvariantCulture)]).ToList();
        return new("INV-11", "مخزون سالب", "Negative stock", baseCode,
            [Col("wh", "المستودع", "Warehouse"), Col("item", "المادة", "Item"), Col("qty", "الكمية الأساسية", "Base quantity", AnalyticalColumnKind.Quantity)], data, null,
            ["الرصيد = مجموع حركات المخزون المسجلة حتى التاريخ المحدد."]);
    }

    private async Task<AnalyticalReport> SlowMovingAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var to = q.To ?? DateOnly.FromDateTime(DateTime.UtcNow); var since = q.From ?? to.AddDays(-90);
        var sinceUtc = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var balances = await ScopedMovements(scope).Where(x => x.MovementDateUtc < toUtc).GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.BaseQuantity), Last = g.Max(x => x.MovementDateUtc) }).Where(x => x.Qty > 0 && x.Last < sinceUtc).ToListAsync(ct).ConfigureAwait(false);
        var items = await context.Items.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.NameArabic, ct).ConfigureAwait(false);
        var data = balances.OrderBy(b => b.Last).Select(b => (IReadOnlyList<string>)[items.GetValueOrDefault(b.ItemId, b.ItemId.ToString()), b.Qty.ToString("0.0000", CultureInfo.InvariantCulture), D(DateOnly.FromDateTime(b.Last.UtcDateTime))]).ToList();
        return new("INV-13", "مواد راكدة", "Slow-moving items", baseCode,
            [Col("item", "المادة", "Item"), Col("qty", "الكمية", "Quantity", AnalyticalColumnKind.Quantity), Col("last", "آخر حركة", "Last movement", Dt)], data, null,
            [$"مواد برصيد موجب بلا حركة منذ {D(since)} حتى {D(to)}."]);
    }

    /// <summary>Open FIFO layers (remaining quantity &gt; 0) as they stand now, oldest first within item and warehouse.</summary>
    private async Task<AnalyticalReport> FifoLayersAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        // As-of rebuild: layers received on/before the date, minus the append-only allocations (consumption positive, reversal restoration negative)
        // whose movements are dated on/before the date. Without a date the report is the current state.
        var asOf = q.To;
        var cutoff = asOf is { } day ? new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : (DateTimeOffset?)null;
        var layers = context.InventoryCostLayers.AsNoTracking().Where(l => l.CompanyId == scope.CompanyId);
        if (!scope.Consolidated) layers = layers.Where(l => l.BranchId == scope.BranchId);
        if (cutoff is { } limit) layers = layers.Where(l => l.ReceivedAtUtc < limit);
        else layers = layers.Where(l => l.QuantityRemaining > 0);
        var rows = await layers.OrderBy(l => l.ItemId).ThenBy(l => l.WarehouseId).ThenBy(l => l.SourceSequence).ToListAsync(ct).ConfigureAwait(false);
        var consumed = new Dictionary<Guid, (decimal Quantity, decimal Cost)>();
        if (cutoff is { } until && rows.Count > 0)
        {
            var ids = rows.Select(r => r.Id).ToList();
            var allocations = await (from a in context.InventoryCostAllocations.AsNoTracking()
                                     join m in context.StockMovements.AsNoTracking() on a.MovementId equals m.Id
                                     where a.CompanyId == scope.CompanyId && ids.Contains(a.LayerId) && m.MovementDateUtc < until
                                     select new { a.LayerId, a.Quantity, a.Cost }).ToListAsync(ct).ConfigureAwait(false);
            consumed = allocations.GroupBy(x => x.LayerId).ToDictionary(g => g.Key, g => (g.Sum(x => x.Quantity), g.Sum(x => x.Cost)));
        }
        var view = rows.Select(r =>
        {
            if (cutoff is null) return (Layer: r, Qty: r.QuantityRemaining, Value: r.ValueRemaining);
            var (quantity, cost) = consumed.GetValueOrDefault(r.Id);
            return (Layer: r, Qty: r.QuantityReceived - quantity, Value: r.ValueReceived - cost);
        }).Where(x => x.Qty > 0m).ToList();
        var items = await context.Items.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.NameArabic, ct).ConfigureAwait(false);
        var whs = await context.Warehouses.AsNoTracking().Where(w => w.CompanyId == scope.CompanyId).ToDictionaryAsync(w => w.Id, w => w.Code + " " + w.NameArabic, ct).ConfigureAwait(false);
        string N(decimal v) => v.ToString("0.0000", CultureInfo.InvariantCulture);
        var data = view.OrderBy(x => items.GetValueOrDefault(x.Layer.ItemId, string.Empty), StringComparer.Ordinal).ThenBy(x => x.Layer.WarehouseId).ThenBy(x => x.Layer.SourceSequence)
            .Select(x => (IReadOnlyList<string>)[items.GetValueOrDefault(x.Layer.ItemId, x.Layer.ItemId.ToString()), whs.GetValueOrDefault(x.Layer.WarehouseId, x.Layer.WarehouseId.ToString()),
                D(DateOnly.FromDateTime(x.Layer.ReceivedAtUtc.UtcDateTime)), N(x.Layer.QuantityReceived), N(x.Qty), N(x.Value),
                N(x.Qty == 0m ? 0m : x.Value / x.Qty)]).ToList();
        var total = view.Sum(x => x.Value);
        return new("INV-07", "طبقات FIFO المفتوحة", "Open FIFO layers", baseCode,
            [Col("item", "المادة", "Item"), Col("wh", "المستودع", "Warehouse"), Col("received", "تاريخ الاستلام", "Received", Dt),
             Col("qtyIn", "الكمية المستلمة", "Quantity received", AnalyticalColumnKind.Quantity), Col("qty", "المتبقي", "Remaining", AnalyticalColumnKind.Quantity),
             Col("value", "القيمة المتبقية", "Remaining value", Amt), Col("unit", "تكلفة الوحدة", "Unit cost", Amt)],
            data, [string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, N(total), string.Empty],
            [asOf is { } d ? string.Create(CultureInfo.InvariantCulture, $"الطبقات كما في {d:yyyy-MM-dd}: مُعاد بناؤها من سجل التخصيصات (الاستهلاك والاسترداد) حسب تاريخ الحركة، لا من الطبقات الحالية.")
                : "الطبقات بحالتها الحالية (لم يُحدَّد تاريخ)؛ الترتيب بتسلسل الاستلام المرحَّل وهو ترتيب الاستهلاك."]);
    }
}
