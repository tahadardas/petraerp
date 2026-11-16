using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Domain.GeneralLedger;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>Third batch: dual-currency balances, realized FX, transfers, COGS, reversals, sales by currency and the branch mapping snapshot.</summary>
public sealed partial class SqlLedgerQueryService
{
    private IQueryable<JournalEntryRow> ScopedEntries(LedgerReportScope scope)
    {
        var entries = context.JournalEntries.AsNoTracking().Where(e => e.CompanyId == scope.CompanyId);
        return scope.Consolidated ? entries : entries.Where(e => e.BranchId == scope.BranchId);
    }

    private static readonly string[] BranchRoleNames =
    [
        "", "الصندوق", "البنك", "المبيعات", "مردودات المبيعات", "المشتريات", "مردودات المشتريات", "المخزون", "تكلفة البضاعة المباعة", "الذمم المدينة", "الذمم الدائنة", "جاري بين الفروع",
    ];

    /// <summary>Foreign-currency balances per account with the base-currency value they were booked at.</summary>
    private async Task<AnalyticalReport> DualBalancesAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var to = q.To ?? DateOnly.MaxValue;
        var lines = ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate <= to && l.TransactionCurrencyCode != baseCode);
        if (!string.IsNullOrWhiteSpace(q.CurrencyCode)) { var c = q.CurrencyCode.Trim().ToUpperInvariant(); lines = lines.Where(l => l.TransactionCurrencyCode == c); }
        if (q.AccountId is { } account) lines = lines.Where(l => l.AccountId == account);
        var rows = await lines.GroupBy(l => new { l.AccountId, l.TransactionCurrencyCode })
            .Select(g => new { g.Key.AccountId, Currency = g.Key.TransactionCurrencyCode, Foreign = g.Sum(l => l.TransactionDebit - l.TransactionCredit), Base = g.Sum(l => l.BaseDebit - l.BaseCredit) })
            .Where(x => x.Foreign != 0m || x.Base != 0m).ToListAsync(ct).ConfigureAwait(false);
        var names = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId).ToDictionaryAsync(a => a.Id, a => a.Code + " " + a.NameArabic, ct).ConfigureAwait(false);
        var data = rows.OrderBy(r => names.GetValueOrDefault(r.AccountId, string.Empty), StringComparer.Ordinal).ThenBy(r => r.Currency, StringComparer.Ordinal)
            .Select(r => (IReadOnlyList<string>)[names.GetValueOrDefault(r.AccountId, r.AccountId.ToString()), r.Currency, A(r.Foreign), A(r.Base), r.Foreign == 0m ? string.Empty : A(Math.Abs(r.Base / r.Foreign))]).ToList();
        return new("FX-06", "أرصدة بعملتين", "Dual-currency balances", baseCode,
            [Col("account", "الحساب", "Account"), Col("currency", "العملة", "Currency"), Col("foreign", "الرصيد بالعملة", "Foreign balance", Amt), Col("base", $"المقابل بالأساس", "Base equivalent", Amt), Col("rate", "متوسط السعر", "Average rate", Amt)],
            data, null, ["الرصيد مدين موجب؛ المقابل الأساسي هو مجموع ما قُيِّد فعلاً (دون إعادة تقييم). إعادة التقييم غير المحققة عبر مسار إعادة التقييم."]);
    }

    /// <summary>FX gain / loss account ids configured on the company's document patterns (stable account-role classification, never the narration text).</summary>
    private async Task<(HashSet<Guid> Gain, HashSet<Guid> Loss)> FxRoleAccountIdsAsync(Guid companyId, CancellationToken ct)
    {
        var json = await context.PatternRevisions.AsNoTracking().Where(r => r.Pattern!.CompanyId == companyId).Select(r => r.ConfigurationJson).ToListAsync(ct).ConfigureAwait(false);
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        HashSet<Guid> gain = [], loss = [];
        foreach (var item in json)
        {
            PetraERP.Domain.Patterns.PatternConfiguration? configuration;
            try { configuration = System.Text.Json.JsonSerializer.Deserialize<PetraERP.Domain.Patterns.PatternConfiguration>(item, options); }
            catch (System.Text.Json.JsonException) { continue; }
            foreach (var mapping in configuration?.AccountMappings ?? [])
            {
                var role = PetraERP.Domain.Patterns.PatternAccountRoles.Normalize(mapping.Role);
                if (string.Equals(role, PetraERP.Domain.Patterns.PatternAccountRoles.FxGain, StringComparison.Ordinal)) gain.Add(mapping.AccountId);
                else if (string.Equals(role, PetraERP.Domain.Patterns.PatternAccountRoles.FxLoss, StringComparison.Ordinal)) loss.Add(mapping.AccountId);
            }
        }
        return (gain, loss);
    }

    private async Task<AnalyticalReport> RealizedFxAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var start = q.From ?? DateOnly.MinValue; var to = q.To ?? DateOnly.MaxValue;
        var (gainIds, lossIds) = await FxRoleAccountIdsAsync(scope.CompanyId, ct).ConfigureAwait(false);
        if (gainIds.Count == 0 && lossIds.Count == 0) throw new BusinessRuleException("report.no_fx_accounts", "Map the FxGain / FxLoss account roles on the voucher patterns first.", "accountId");
        var all = gainIds.Union(lossIds).ToList();
        var lines = await ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate >= start && l.Journal.AccountingDate <= to && all.Contains(l.AccountId))
            .Select(l => new { l.Journal!.AccountingDate, l.AccountId, l.BaseDebit, l.BaseCredit }).ToListAsync(ct).ConfigureAwait(false);
        var data = lines.GroupBy(l => (l.AccountingDate.Year, l.AccountingDate.Month)).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).Select(g =>
        {
            var gain = g.Where(l => gainIds.Contains(l.AccountId)).Sum(l => l.BaseCredit - l.BaseDebit);
            var loss = g.Where(l => lossIds.Contains(l.AccountId)).Sum(l => l.BaseDebit - l.BaseCredit);
            return (IReadOnlyList<string>)[string.Create(CultureInfo.InvariantCulture, $"{g.Key.Year}-{g.Key.Month:00}"), A(gain), A(loss), A(gain - loss)];
        }).ToList();
        var tg = lines.Where(l => gainIds.Contains(l.AccountId)).Sum(l => l.BaseCredit - l.BaseDebit);
        var tl = lines.Where(l => lossIds.Contains(l.AccountId)).Sum(l => l.BaseDebit - l.BaseCredit);
        return new("FX-07", "فروقات صرف محققة", "Realized exchange differences", baseCode,
            [Col("month", "الشهر", "Month"), Col("gain", "أرباح", "Gains", Amt), Col("loss", "خسائر", "Losses", Amt), Col("net", "الصافي", "Net", Amt)],
            data, ["المجموع", A(tg), A(tl), A(tg - tl)], ["تُصنَّف بحسب حسابات دور FxGain/FxLoss المهيأة في أنماط السندات (تصنيف ثابت لا يعتمد على نص البيان). تُحسب الحركة المرحَّلة على هذه الحسابات؛ إعادة التقييم غير المحققة تُرحَّل إلى حسابات منفصلة ولا تدخل هنا."]);
    }

    private async Task<AnalyticalReport> TransfersAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var movements = ScopedMovements(scope).Where(x => x.MovementKind == 6 || x.MovementKind == 7);
        if (q.From is { } f) { var fu = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); movements = movements.Where(x => x.MovementDateUtc >= fu); }
        if (q.To is { } t) { var tu = new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); movements = movements.Where(x => x.MovementDateUtc < tu); }
        var movementCount = await movements.CountAsync(ct).ConfigureAwait(false);
        var rows = await movements.OrderBy(x => x.MovementDateUtc).ThenBy(x => x.RecordedSequence).Take(DetailRowLimit)
            .Select(x => new { x.MovementDateUtc, x.DocumentNumber, x.WarehouseId, x.ItemId, x.MovementKind, x.BaseQuantity, x.TotalCost }).ToListAsync(ct).ConfigureAwait(false);
        var items = await context.Items.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.NameArabic, ct).ConfigureAwait(false);
        var whs = await context.Warehouses.AsNoTracking().Where(w => w.CompanyId == scope.CompanyId).ToDictionaryAsync(w => w.Id, w => w.Code + " " + w.NameArabic, ct).ConfigureAwait(false);
        var data = rows.Select(r => (IReadOnlyList<string>)[D(DateOnly.FromDateTime(r.MovementDateUtc.UtcDateTime)), r.DocumentNumber ?? string.Empty, r.MovementKind == 7 ? "صادر" : "وارد",
            whs.GetValueOrDefault(r.WarehouseId, r.WarehouseId.ToString()), items.GetValueOrDefault(r.ItemId, r.ItemId.ToString()), A(Math.Abs(r.BaseQuantity)), A(r.TotalCost)]).ToList();
        return new("INV-09", "تحويلات المخزون", "Stock transfers", baseCode,
            [Col("date", "التاريخ", "Date", Dt), Col("doc", "المستند", "Document"), Col("dir", "الاتجاه", "Direction"), Col("wh", "المستودع", "Warehouse"), Col("item", "المادة", "Item"),
             Col("qty", "الكمية الأساسية", "Base quantity", AnalyticalColumnKind.Quantity), Col("cost", "التكلفة", "Cost", Amt)],
            data, null, movementCount > rows.Count
                ? ["كل تحويل يظهر سطرين: صادر من مستودع المصدر ووارد إلى مستودع الوجهة.", string.Create(CultureInfo.InvariantCulture, $"عُرض أول {rows.Count} حركة من {movementCount}؛ ضيّق المدى.")]
                : ["كل تحويل يظهر سطرين: صادر من مستودع المصدر ووارد إلى مستودع الوجهة."]);
    }

    private async Task<AnalyticalReport> CostOfGoodsSoldAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var movements = ScopedMovements(scope).Where(x => x.MovementKind == 4 || x.MovementKind == 5);
        if (q.From is { } f) { var fu = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); movements = movements.Where(x => x.MovementDateUtc >= fu); }
        if (q.To is { } t) { var tu = new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); movements = movements.Where(x => x.MovementDateUtc < tu); }
        var rows = await movements.GroupBy(x => x.ItemId).Select(g => new
        {
            ItemId = g.Key,
            SoldQty = g.Sum(x => x.MovementKind == 4 ? -x.BaseQuantity : 0m), SoldCost = g.Sum(x => x.MovementKind == 4 ? x.TotalCost : 0m),
            ReturnQty = g.Sum(x => x.MovementKind == 5 ? x.BaseQuantity : 0m), ReturnCost = g.Sum(x => x.MovementKind == 5 ? x.TotalCost : 0m),
        }).ToListAsync(ct).ConfigureAwait(false);
        var items = await context.Items.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.NameArabic, ct).ConfigureAwait(false);
        var data = rows.OrderBy(r => items.GetValueOrDefault(r.ItemId, string.Empty), StringComparer.Ordinal)
            .Select(r => (IReadOnlyList<string>)[items.GetValueOrDefault(r.ItemId, r.ItemId.ToString()), A(r.SoldQty), A(r.ReturnQty), A(r.SoldQty - r.ReturnQty), A(r.SoldCost - r.ReturnCost)]).ToList();
        return new("INV-12", "تكلفة البضاعة المباعة", "Cost of goods sold", baseCode,
            [Col("item", "المادة", "Item"), Col("sold", "المباع", "Sold", AnalyticalColumnKind.Quantity), Col("ret", "المرتجع", "Returned", AnalyticalColumnKind.Quantity),
             Col("net", "الصافي", "Net quantity", AnalyticalColumnKind.Quantity), Col("cost", "التكلفة الصافية", "Net cost", Amt)],
            data, ["المجموع", A(rows.Sum(r => r.SoldQty)), A(rows.Sum(r => r.ReturnQty)), A(rows.Sum(r => r.SoldQty - r.ReturnQty)), A(rows.Sum(r => r.SoldCost - r.ReturnCost))],
            ["التكلفة من حركات المخزون المسجلة (تسليم بيع ناقص مرتجع بيع) بتكلفة FIFO/المتوسط كما سُجلت."]);
    }

    private async Task<AnalyticalReport> ReversalsAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var start = q.From ?? DateOnly.MinValue; var to = q.To ?? DateOnly.MaxValue;
        var entries = ScopedEntries(scope).Where(e => e.AccountingDate >= start && e.AccountingDate <= to && (e.State == (byte)JournalEntryState.Reversed || e.ReversesEntryId != null));
        var entryCount = await entries.CountAsync(ct).ConfigureAwait(false);
        var rows = await entries.OrderBy(e => e.AccountingDate).ThenBy(e => e.PostingSequence).Take(DetailRowLimit)
            .Select(e => new { e.AccountingDate, e.PostingSequence, e.Description, e.State, e.ReversesEntryId, e.ReversedByEntryId }).ToListAsync(ct).ConfigureAwait(false);
        var data = rows.Select(r => (IReadOnlyList<string>)[D(r.AccountingDate), r.PostingSequence?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, r.Description,
            r.ReversesEntryId is not null ? "قيد عكس" : "قيد معكوس", (r.ReversesEntryId ?? r.ReversedByEntryId)?.ToString() ?? string.Empty]).ToList();
        return new("GL-10", "التسويات والعكس", "Adjustments and reversals", baseCode,
            [Col("date", "التاريخ", "Date", Dt), Col("seq", "تسلسل الترحيل", "Posting sequence"), Col("desc", "البيان", "Description"), Col("kind", "النوع", "Kind"), Col("link", "القيد المرتبط", "Linked entry")],
            data, null, entryCount > rows.Count
                ? ["القيود المعكوسة وقيود العكس فقط؛ لا يُحذف أي قيد مرحَّل.", string.Create(CultureInfo.InvariantCulture, $"عُرض أول {rows.Count} قيد من {entryCount}؛ ضيّق المدى.")]
                : ["القيود المعكوسة وقيود العكس فقط؛ لا يُحذف أي قيد مرحَّل."]);
    }

    private async Task<AnalyticalReport> SalesByCurrencyAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var sales = await RoleAccountIdsAsync(scope.CompanyId, 3, ct).ConfigureAwait(false);
        var returns = await RoleAccountIdsAsync(scope.CompanyId, 4, ct).ConfigureAwait(false);
        if (sales.Count == 0) throw new BusinessRuleException("report.no_role_accounts", "Map the branch account roles first (branch account mappings).", "accountId");
        var start = q.From ?? DateOnly.MinValue; var to = q.To ?? DateOnly.MaxValue;
        var lines = await ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate >= start && l.Journal.AccountingDate <= to && (sales.Contains(l.AccountId) || returns.Contains(l.AccountId)))
            .Select(l => new { l.AccountId, l.TransactionCurrencyCode, l.TransactionDebit, l.TransactionCredit, l.BaseDebit, l.BaseCredit }).ToListAsync(ct).ConfigureAwait(false);
        var data = lines.GroupBy(l => l.TransactionCurrencyCode).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var tx = g.Sum(l => sales.Contains(l.AccountId) ? l.TransactionCredit - l.TransactionDebit : l.TransactionCredit - l.TransactionDebit);
            var bs = g.Sum(l => l.BaseCredit - l.BaseDebit);
            return (IReadOnlyList<string>)[g.Key, A(tx), A(bs)];
        }).ToList();
        return new("SALES-12", "مبيعات حسب العملة", "Sales by currency", baseCode,
            [Col("currency", "العملة", "Currency"), Col("tx", "الصافي بالعملة", "Net in currency", Amt), Col("base", "الصافي بالأساس", "Net in base", Amt)],
            data, ["المجموع", string.Empty, A(lines.Sum(l => l.BaseCredit - l.BaseDebit))], ["صافي المبيعات بعد المردودات (حسابات المبيعات ومردوداتها كما في ربط الفروع)؛ لا يُجمع الصافي بعملات مختلفة."]);
    }

    /// <summary>
    /// Branch account mapping in force on a date, with unmapped roles shown explicitly. Mappings are effective-dated and never rewritten, so the
    /// same date always reproduces the same snapshot (the mapping a document dated then was posted with).
    /// </summary>
    private async Task<AnalyticalReport> BranchMappingSnapshotAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var day = q.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var branches = context.Branches.AsNoTracking().Where(b => b.CompanyId == scope.CompanyId && b.IsActive);
        if (!scope.Consolidated) branches = branches.Where(b => b.Id == scope.BranchId);
        var branchList = await branches.OrderBy(b => b.Code).Select(b => new { b.Id, b.Code, b.NameArabic }).ToListAsync(ct).ConfigureAwait(false);
        var links = await (from l in context.BranchAccountLinks.AsNoTracking()
                           join a in context.Accounts.AsNoTracking() on l.AccountId equals a.Id
                           where l.CompanyId == scope.CompanyId && l.EffectiveFrom <= day && (l.EffectiveTo == null || l.EffectiveTo >= day)
                           select new { l.BranchId, l.Role, a.Code, a.NameArabic, l.EffectiveFrom }).ToListAsync(ct).ConfigureAwait(false);
        var data = new List<IReadOnlyList<string>>();
        var missing = 0;
        foreach (var b in branchList)
            for (byte role = 1; role < BranchRoleNames.Length; role++)
            {
                var link = links.Where(l => l.BranchId == b.Id && l.Role == role).OrderByDescending(l => l.EffectiveFrom).FirstOrDefault();
                if (link is null) missing++;
                data.Add([b.Code + " " + b.NameArabic, BranchRoleNames[role], link is null ? string.Empty : link.Code + " " + link.NameArabic, link is null ? string.Empty : D(link.EffectiveFrom), link is null ? "غير مربوط" : "مربوط"]);
            }
        return new("GL-13", "خريطة حسابات الفروع بتاريخ", "Branch account mapping on a date", baseCode,
            [Col("branch", "الفرع", "Branch"), Col("role", "الدور", "Role"), Col("account", "الحساب", "Account"), Col("from", "ساري منذ", "Effective from", Dt), Col("status", "الحالة", "Status")],
            data, null, [$"لقطة بتاريخ {D(day)} (حقل «إلى»)؛ الأدوار غير المربوطة: {missing}.", "الربط مؤرَّخ ولا يُعدَّل بأثر رجعي، فاللقطة قابلة لإعادة الإنتاج لأي تاريخ."]);
    }
}
