using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>Fourth batch: trading / operating / equity statements, cost-centre reports, daily sales and purchases, branch margin, cash expenses and stock exceptions.</summary>
public sealed partial class SqlLedgerQueryService
{
    private static (DateOnly Start, DateOnly End) Range(AnalyticalQuery q) => (q.From ?? DateOnly.MinValue, q.To ?? DateOnly.MaxValue);

    private IQueryable<JournalLineRow> PostedPeriod(LedgerReportScope scope, AnalyticalQuery q)
    {
        var (start, end) = Range(q);
        return ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate >= start && l.Journal.AccountingDate <= end);
    }

    private async Task<decimal> RoleNetAsync(LedgerReportScope scope, AnalyticalQuery q, byte role, bool creditNormal, CancellationToken ct)
    {
        var ids = (await RoleAccountIdsAsync(scope.CompanyId, role, ct).ConfigureAwait(false)).ToList();
        if (ids.Count == 0) return 0m;
        var net = await PostedPeriod(scope, q).Where(l => ids.Contains(l.AccountId)).SumAsync(l => (decimal?)(l.BaseDebit - l.BaseCredit), ct).ConfigureAwait(false) ?? 0m;
        return creditNormal ? -net : net;
    }

    private async Task<AnalyticalReport> TradingAccountAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        if ((await RoleAccountIdsAsync(scope.CompanyId, 3, ct).ConfigureAwait(false)).Count == 0)
            throw new BusinessRuleException("report.no_role_accounts", "Map the branch account roles first (branch account mappings).", "accountId");
        var sales = await RoleNetAsync(scope, q, 3, true, ct).ConfigureAwait(false);
        var salesReturns = await RoleNetAsync(scope, q, 4, false, ct).ConfigureAwait(false);
        var purchases = await RoleNetAsync(scope, q, 5, false, ct).ConfigureAwait(false);
        var purchaseReturns = await RoleNetAsync(scope, q, 6, true, ct).ConfigureAwait(false);
        var cogs = await RoleNetAsync(scope, q, 8, false, ct).ConfigureAwait(false);
        var netSales = sales - salesReturns;
        var gross = netSales - cogs;
        IReadOnlyList<string> R(string label, decimal v) => [label, A(v)];
        return new("FIN-05", "حساب المتاجرة", "Trading account", baseCode, [Col("item", "البند", "Item"), Col("amount", "المبلغ", "Amount", Amt)],
            [R("المبيعات", sales), R("مردودات المبيعات", -salesReturns), R("صافي المبيعات", netSales), R("تكلفة البضاعة المباعة", -cogs), R("مجمل الربح", gross),
             R("للعلم: المشتريات", purchases), R("للعلم: مردودات المشتريات", -purchaseReturns)],
            null, ["تكلفة البضاعة المباعة من حسابات دور COGS المرحَّلة (جرد مستمر)؛ المشتريات تُعرض للعلم ولا تدخل في مجمل الربح.", "تعتمد على ربط أدوار الفرع (مبيعات 3، مردودات 4، مشتريات 5، مردودات 6، تكلفة 8)."]);
    }

    private async Task<AnalyticalReport> OperatingAccountAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var trading = new HashSet<Guid>();
        foreach (byte role in new byte[] { 3, 4, 5, 6, 8 }) trading.UnionWith(await RoleAccountIdsAsync(scope.CompanyId, role, ct).ConfigureAwait(false));
        var rows = await PostedPeriod(scope, q).Join(context.Accounts.AsNoTracking(), l => l.AccountId, a => a.Id, (l, a) => new { a.Id, a.Code, a.NameArabic, a.AccountType, l.BaseDebit, l.BaseCredit })
            .Where(x => x.AccountType == 4 || x.AccountType == 5).GroupBy(x => new { x.Id, x.Code, x.NameArabic, x.AccountType })
            .Select(g => new { g.Key.Id, g.Key.Code, g.Key.NameArabic, g.Key.AccountType, Net = g.Sum(x => x.BaseCredit - x.BaseDebit) }).ToListAsync(ct).ConfigureAwait(false);
        var other = rows.Where(r => !trading.Contains(r.Id)).OrderBy(r => r.AccountType).ThenBy(r => r.Code, StringComparer.Ordinal).ToList();
        var data = other.Select(r => (IReadOnlyList<string>)[r.AccountType == 4 ? "إيراد" : "مصروف", r.Code + " " + r.NameArabic, A(r.AccountType == 4 ? r.Net : -r.Net)]).ToList();
        var net = other.Sum(r => r.Net);
        data.Add(["صافي التشغيل", string.Empty, A(net)]);
        return new("FIN-06", "حساب التشغيل", "Operating account", baseCode, [Col("kind", "النوع", "Kind"), Col("account", "الحساب", "Account"), Col("amount", "المبلغ", "Amount", Amt)],
            data, null, ["إيرادات ومصروفات غير حسابات المتاجرة (أدوار المبيعات/المشتريات/التكلفة). الإيراد موجب والمصروف يظهر بقيمته الموجبة، والصافي = إيرادات − مصروفات."]);
    }

    private async Task<AnalyticalReport> EquityChangesAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (start, end) = Range(q);
        var lines = ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate <= end);
        var rows = await lines.Join(context.Accounts.AsNoTracking(), l => l.AccountId, a => a.Id, (l, a) => new { a.Id, a.Code, a.NameArabic, a.AccountType, l.Journal!.AccountingDate, l.BaseDebit, l.BaseCredit })
            .Where(x => x.AccountType == 3).GroupBy(x => new { x.Id, x.Code, x.NameArabic })
            .Select(g => new { g.Key.Code, g.Key.NameArabic,
                Opening = g.Sum(x => x.AccountingDate < start ? x.BaseCredit - x.BaseDebit : 0m),
                Added = g.Sum(x => x.AccountingDate >= start ? x.BaseCredit : 0m), Drawn = g.Sum(x => x.AccountingDate >= start ? x.BaseDebit : 0m) })
            .ToListAsync(ct).ConfigureAwait(false);
        var income = (await PostedPeriod(scope, q).Join(context.Accounts.AsNoTracking(), l => l.AccountId, a => a.Id, (l, a) => new { a.AccountType, l.BaseDebit, l.BaseCredit })
            .Where(x => x.AccountType == 4 || x.AccountType == 5).SumAsync(x => (decimal?)(x.BaseCredit - x.BaseDebit), ct).ConfigureAwait(false)) ?? 0m;
        var data = rows.OrderBy(r => r.Code, StringComparer.Ordinal).Select(r => (IReadOnlyList<string>)[r.Code + " " + r.NameArabic, A(r.Opening), A(r.Added), A(r.Drawn), A(r.Opening + r.Added - r.Drawn)]).ToList();
        data.Add(["صافي ربح/خسارة الفترة (غير مقفل)", A(0), A(income > 0 ? income : 0), A(income < 0 ? -income : 0), A(income)]);
        var totalClosing = rows.Sum(r => r.Opening + r.Added - r.Drawn) + income;
        return new("FIN-07", "حركة حقوق الملكية", "Statement of changes in equity", baseCode,
            [Col("account", "الحساب", "Account"), Col("opening", "افتتاحي", "Opening", Amt), Col("added", "زيادات", "Additions", Amt), Col("drawn", "تخفيضات", "Reductions", Amt), Col("closing", "ختامي", "Closing", Amt)],
            data, ["المجموع شاملاً ربح الفترة", string.Empty, string.Empty, string.Empty, A(totalClosing)],
            ["حسابات حقوق الملكية المرحَّلة؛ ربح الفترة يُعرض سطراً مستقلاً لأن الإقفال لم يُرحَّل إلى الأرباح المدورة بعد (إن وُجد قيد إقفال فهو يظهر ضمن الحركة)."]);
    }

    private sealed record CenterTotals(Guid? CenterId, Guid BranchId, decimal Revenue, decimal Expense, decimal Debit, decimal Credit, int Lines);

    private async Task<(List<CenterTotals> Rows, Dictionary<Guid, string> Names)> CenterTotalsAsync(LedgerReportScope scope, AnalyticalQuery q, bool byBranch, CancellationToken ct)
    {
        var rows = await PostedPeriod(scope, q).Where(l => l.CostCenterId != null)
            .Join(context.Accounts.AsNoTracking(), l => l.AccountId, a => a.Id, (l, a) => new { l.CostCenterId, l.Journal!.BranchId, a.AccountType, l.BaseDebit, l.BaseCredit })
            .GroupBy(x => new { x.CostCenterId, BranchId = byBranch ? x.BranchId : Guid.Empty })
            .Select(g => new CenterTotals(g.Key.CostCenterId, g.Key.BranchId, g.Sum(x => x.AccountType == 4 ? x.BaseCredit - x.BaseDebit : 0m), g.Sum(x => x.AccountType == 5 ? x.BaseDebit - x.BaseCredit : 0m),
                g.Sum(x => x.BaseDebit), g.Sum(x => x.BaseCredit), g.Count())).ToListAsync(ct).ConfigureAwait(false);
        var names = await context.CostCenters.AsNoTracking().Where(c => c.CompanyId == scope.CompanyId).ToDictionaryAsync(c => c.Id, c => c.Code + " " + c.NameArabic, ct).ConfigureAwait(false);
        return (rows, names);
    }

    private async Task<AnalyticalReport> CostCenterBalanceAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (rows, names) = await CenterTotalsAsync(scope, q, false, ct).ConfigureAwait(false);
        var data = rows.OrderBy(r => names.GetValueOrDefault(r.CenterId ?? Guid.Empty, string.Empty), StringComparer.Ordinal)
            .Select(r => (IReadOnlyList<string>)[names.GetValueOrDefault(r.CenterId ?? Guid.Empty, string.Empty), A(r.Debit), A(r.Credit), A(r.Debit - r.Credit)]).ToList();
        return new("CC-03", "ميزان مراكز الكلفة", "Cost-centre balance", baseCode, [Col("center", "المركز", "Centre"), Col("debit", "مدين", "Debit", Amt), Col("credit", "دائن", "Credit", Amt), Col("net", "الرصيد", "Balance", Amt)],
            data, ["المجموع", A(rows.Sum(r => r.Debit)), A(rows.Sum(r => r.Credit)), A(rows.Sum(r => r.Debit - r.Credit))],
            ["الأسطر المرحَّلة التي لها مركز كلفة فقط؛ الأسطر بلا مركز تظهر في CC-07."]);
    }

    private async Task<AnalyticalReport> CostCenterProfitAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, string id, string ar, string en, bool byBranch, CancellationToken ct)
    {
        var (rows, names) = await CenterTotalsAsync(scope, q, byBranch, ct).ConfigureAwait(false);
        var branches = byBranch ? await context.Branches.AsNoTracking().Where(b => b.CompanyId == scope.CompanyId).ToDictionaryAsync(b => b.Id, b => b.Code + " " + b.NameArabic, ct).ConfigureAwait(false) : [];
        var data = rows.OrderBy(r => names.GetValueOrDefault(r.CenterId ?? Guid.Empty, string.Empty), StringComparer.Ordinal).ThenBy(r => r.BranchId)
            .Select(r => byBranch
                ? (IReadOnlyList<string>)[names.GetValueOrDefault(r.CenterId ?? Guid.Empty, string.Empty), branches.GetValueOrDefault(r.BranchId, string.Empty), A(r.Revenue), A(r.Expense), A(r.Revenue - r.Expense)]
                : [names.GetValueOrDefault(r.CenterId ?? Guid.Empty, string.Empty), A(r.Revenue), A(r.Expense), A(r.Revenue - r.Expense)]).ToList();
        var cols = new List<AnalyticalColumn> { Col("center", "المركز", "Centre") };
        if (byBranch) cols.Add(Col("branch", "الفرع", "Branch"));
        cols.AddRange([Col("rev", "الإيرادات", "Revenue", Amt), Col("exp", "المصاريف", "Expenses", Amt), Col("net", "الصافي", "Net", Amt)]);
        IReadOnlyList<string> totals = byBranch ? ["المجموع", string.Empty, A(rows.Sum(r => r.Revenue)), A(rows.Sum(r => r.Expense)), A(rows.Sum(r => r.Revenue - r.Expense))]
            : ["المجموع", A(rows.Sum(r => r.Revenue)), A(rows.Sum(r => r.Expense)), A(rows.Sum(r => r.Revenue - r.Expense))];
        return new(id, ar, en, baseCode, cols, data, totals, ["إيرادات ومصروفات الأسطر المرحَّلة المرتبطة بمراكز الكلفة؛ لا يوجد توزيع تلقائي للمصاريف المشتركة (انظر CC-05)."]);
    }

    private async Task<AnalyticalReport> UnallocatedExpensesAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var rows = await PostedPeriod(scope, q).Where(l => l.CostCenterId == null)
            .Join(context.Accounts.AsNoTracking(), l => l.AccountId, a => a.Id, (l, a) => new { a.Code, a.NameArabic, a.AccountType, l.BaseDebit, l.BaseCredit })
            .Where(x => x.AccountType == 4 || x.AccountType == 5).GroupBy(x => new { x.Code, x.NameArabic, x.AccountType })
            .Select(g => new { g.Key.Code, g.Key.NameArabic, g.Key.AccountType, Lines = g.Count(), Net = g.Sum(x => x.BaseDebit - x.BaseCredit) }).ToListAsync(ct).ConfigureAwait(false);
        var data = rows.OrderBy(r => r.Code, StringComparer.Ordinal).Select(r => (IReadOnlyList<string>)[r.Code + " " + r.NameArabic, r.AccountType == 4 ? "إيراد" : "مصروف", r.Lines.ToString(CultureInfo.InvariantCulture), A(r.Net)]).ToList();
        return new("CC-07", "مصروفات غير مخصصة", "Unallocated expenses", baseCode, [Col("account", "الحساب", "Account"), Col("kind", "النوع", "Kind"), Col("lines", "عدد الأسطر", "Lines"), Col("net", "الصافي (مدين − دائن)", "Net (Dr − Cr)", Amt)],
            data, ["المجموع", string.Empty, rows.Sum(r => r.Lines).ToString(CultureInfo.InvariantCulture), A(rows.Sum(r => r.Net))], ["أسطر إيراد/مصروف مرحَّلة بلا مركز كلفة."]);
    }

    private async Task<AnalyticalReport> MostActiveCentersAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (rows, names) = await CenterTotalsAsync(scope, q, false, ct).ConfigureAwait(false);
        var top = rows.OrderByDescending(r => r.Lines).ThenBy(r => names.GetValueOrDefault(r.CenterId ?? Guid.Empty, string.Empty), StringComparer.Ordinal).Take(20).ToList();
        var data = top.Select(r => (IReadOnlyList<string>)[names.GetValueOrDefault(r.CenterId ?? Guid.Empty, string.Empty), r.Lines.ToString(CultureInfo.InvariantCulture), A(r.Debit + r.Credit)]).ToList();
        return new("CC-08", "مراكز الأكثر حركة", "Most active centres", baseCode, [Col("center", "المركز", "Centre"), Col("lines", "عدد الأسطر", "Lines"), Col("volume", "حجم الحركة (مدين + دائن)", "Volume (Dr + Cr)", Amt)],
            data, null, ["أعلى 20 مركزاً بعدد الأسطر المرحَّلة."]);
    }

    private async Task<AnalyticalReport> CashExpensesAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var cash = await CashAccountIdsAsync(scope.CompanyId, ct).ConfigureAwait(false);
        if (cash.Count == 0) throw new BusinessRuleException("report.no_cash_accounts", "Map the branch Cash/Bank accounts first (branch account mappings).", "accountId");
        var period = PostedPeriod(scope, q);
        var touching = period.Where(l => cash.Contains(l.AccountId)).Select(l => l.JournalId).Distinct();
        var rows = await period.Where(l => touching.Contains(l.JournalId)).Join(context.Accounts.AsNoTracking(), l => l.AccountId, a => a.Id, (l, a) => new { a.Code, a.NameArabic, a.AccountType, l.BaseDebit, l.BaseCredit })
            .Where(x => x.AccountType == 5).GroupBy(x => new { x.Code, x.NameArabic }).Select(g => new { g.Key.Code, g.Key.NameArabic, Net = g.Sum(x => x.BaseDebit - x.BaseCredit), Lines = g.Count() }).ToListAsync(ct).ConfigureAwait(false);
        var data = rows.OrderByDescending(r => r.Net).ThenBy(r => r.Code, StringComparer.Ordinal).Select(r => (IReadOnlyList<string>)[r.Code + " " + r.NameArabic, r.Lines.ToString(CultureInfo.InvariantCulture), A(r.Net)]).ToList();
        return new("CASH-06", "مصاريف نقدية", "Cash expenses", baseCode, [Col("account", "حساب المصروف", "Expense account"), Col("lines", "عدد الأسطر", "Lines"), Col("net", "المبلغ", "Amount", Amt)],
            data, ["المجموع", rows.Sum(r => r.Lines).ToString(CultureInfo.InvariantCulture), A(rows.Sum(r => r.Net))], ["أسطر المصروف في قيود تمس حسابات الصندوق/البنك المربوطة بالفروع."]);
    }

    private async Task<AnalyticalReport> DailyRoleAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, string id, string ar, string en, byte mainRole, byte returnRole, bool creditNormal, CancellationToken ct)
    {
        var main = await RoleAccountIdsAsync(scope.CompanyId, mainRole, ct).ConfigureAwait(false);
        var returns = await RoleAccountIdsAsync(scope.CompanyId, returnRole, ct).ConfigureAwait(false);
        if (main.Count == 0) throw new BusinessRuleException("report.no_role_accounts", "Map the branch account roles first (branch account mappings).", "accountId");
        var all = main.Union(returns).ToList();
        var lines = await PostedPeriod(scope, q).Where(l => all.Contains(l.AccountId)).Select(l => new { l.Journal!.AccountingDate, l.AccountId, l.BaseDebit, l.BaseCredit }).ToListAsync(ct).ConfigureAwait(false);
        decimal Net(decimal d, decimal c) => creditNormal ? c - d : d - c;
        var data = lines.GroupBy(l => l.AccountingDate).OrderBy(g => g.Key).Select(g =>
        {
            var m = g.Where(l => main.Contains(l.AccountId)).Sum(l => Net(l.BaseDebit, l.BaseCredit));
            var r = -g.Where(l => returns.Contains(l.AccountId)).Sum(l => Net(l.BaseDebit, l.BaseCredit));
            return (IReadOnlyList<string>)[D(g.Key), A(m), A(r), A(m - r)];
        }).ToList();
        var tm = lines.Where(l => main.Contains(l.AccountId)).Sum(l => Net(l.BaseDebit, l.BaseCredit));
        var tr = -lines.Where(l => returns.Contains(l.AccountId)).Sum(l => Net(l.BaseDebit, l.BaseCredit));
        return new(id, ar, en, baseCode, [Col("day", "اليوم", "Day", Dt), Col("main", creditNormal ? "المبيعات" : "المشتريات", creditNormal ? "Sales" : "Purchases", Amt), Col("ret", "المردودات", "Returns", Amt), Col("net", "الصافي", "Net", Amt)],
            data, ["المجموع", A(tm), A(tr), A(tm - tr)], ["من حسابات الأدوار المربوطة بالفروع في القيود المرحَّلة."]);
    }

    private async Task<AnalyticalReport> ReturnsOnlyAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, string id, string ar, string en, byte mainRole, byte returnRole, bool creditNormal, CancellationToken ct)
    {
        var main = await RoleAccountIdsAsync(scope.CompanyId, mainRole, ct).ConfigureAwait(false);
        var returns = await RoleAccountIdsAsync(scope.CompanyId, returnRole, ct).ConfigureAwait(false);
        if (returns.Count == 0) throw new BusinessRuleException("report.no_role_accounts", "Map the branch account roles first (branch account mappings).", "accountId");
        var lines = await PostedPeriod(scope, q).Where(l => main.Contains(l.AccountId) || returns.Contains(l.AccountId)).Select(l => new { l.Journal!.AccountingDate, l.AccountId, l.BaseDebit, l.BaseCredit }).ToListAsync(ct).ConfigureAwait(false);
        decimal Net(decimal d, decimal c) => creditNormal ? c - d : d - c;
        var data = lines.GroupBy(l => (l.AccountingDate.Year, l.AccountingDate.Month)).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).Select(g =>
        {
            var m = g.Where(l => main.Contains(l.AccountId)).Sum(l => Net(l.BaseDebit, l.BaseCredit));
            var r = -g.Where(l => returns.Contains(l.AccountId)).Sum(l => Net(l.BaseDebit, l.BaseCredit));
            return (IReadOnlyList<string>)[string.Create(CultureInfo.InvariantCulture, $"{g.Key.Year}-{g.Key.Month:00}"), A(r), m == 0 ? "0.00" : (r * 100m / m).ToString("0.00", CultureInfo.InvariantCulture)];
        }).ToList();
        return new(id, ar, en, baseCode, [Col("month", "الشهر", "Month"), Col("ret", "المردودات", "Returns", Amt), Col("pct", "٪ من الأساس", "% of base")],
            data, null, ["النسبة = المردودات ÷ الإجمالي الأساسي للشهر. الخصومات تُسجَّل ضمن حساب المبيعات نفسه ولا يوجد حساب خصم مستقل لعرضها هنا."]);
    }

    private async Task<AnalyticalReport> BranchMarginAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var sales = await RoleAccountIdsAsync(scope.CompanyId, 3, ct).ConfigureAwait(false);
        var returns = await RoleAccountIdsAsync(scope.CompanyId, 4, ct).ConfigureAwait(false);
        var cogs = await RoleAccountIdsAsync(scope.CompanyId, 8, ct).ConfigureAwait(false);
        if (sales.Count == 0) throw new BusinessRuleException("report.no_role_accounts", "Map the branch account roles first (branch account mappings).", "accountId");
        var all = sales.Union(returns).Union(cogs).ToList();
        var lines = await PostedPeriod(scope, q).Where(l => all.Contains(l.AccountId)).Select(l => new { l.Journal!.BranchId, l.AccountId, l.BaseDebit, l.BaseCredit }).ToListAsync(ct).ConfigureAwait(false);
        var names = await context.Branches.AsNoTracking().Where(b => b.CompanyId == scope.CompanyId).ToDictionaryAsync(b => b.Id, b => b.Code + " " + b.NameArabic, ct).ConfigureAwait(false);
        var data = lines.GroupBy(l => l.BranchId).OrderBy(g => names.GetValueOrDefault(g.Key, string.Empty), StringComparer.Ordinal).Select(g =>
        {
            var net = g.Where(l => sales.Contains(l.AccountId)).Sum(l => l.BaseCredit - l.BaseDebit) - g.Where(l => returns.Contains(l.AccountId)).Sum(l => l.BaseDebit - l.BaseCredit);
            var cost = g.Where(l => cogs.Contains(l.AccountId)).Sum(l => l.BaseDebit - l.BaseCredit);
            return (IReadOnlyList<string>)[names.GetValueOrDefault(g.Key, g.Key.ToString()), A(net), A(cost), A(net - cost), net == 0 ? "0.00" : ((net - cost) * 100m / net).ToString("0.00", CultureInfo.InvariantCulture)];
        }).ToList();
        return new("SALES-10", "هامش ربح فرع", "Branch margin", baseCode, [Col("branch", "الفرع", "Branch"), Col("sales", "صافي المبيعات", "Net sales", Amt), Col("cogs", "التكلفة", "Cost", Amt), Col("margin", "الهامش", "Margin", Amt), Col("pct", "٪", "%")],
            data, null, ["صافي المبيعات (مبيعات − مردودات) مقابل حسابات تكلفة البضاعة المباعة المرحَّلة؛ يعكس الواقع المحاسبي بتكلفة وقت الترحيل."]);
    }

    private async Task<AnalyticalReport> BelowMinimumAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var cutoff = q.To is { } day ? (DateTimeOffset?)new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
        var movements = ScopedMovements(scope);
        if (cutoff is { } limit) movements = movements.Where(x => x.MovementDateUtc < limit);
        var balances = (await movements.GroupBy(x => x.ItemId).Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.BaseQuantity) }).ToListAsync(ct).ConfigureAwait(false)).ToDictionary(x => x.ItemId, x => x.Qty);
        var items = await context.Items.AsNoTracking().Where(i => i.CompanyId == scope.CompanyId && i.IsActive && (i.MinStock != null || i.ReorderPoint != null)).ToListAsync(ct).ConfigureAwait(false);
        string N(decimal v) => v.ToString("0.0000", CultureInfo.InvariantCulture);
        var data = items.Select(i => new { Item = i, Qty = balances.GetValueOrDefault(i.Id) })
            .Where(x => (x.Item.MinStock is { } min && x.Qty < min) || (x.Item.ReorderPoint is { } reorder && x.Qty <= reorder))
            .OrderBy(x => x.Item.Code, StringComparer.Ordinal)
            .Select(x => (IReadOnlyList<string>)[x.Item.Code + " " + x.Item.NameArabic, N(x.Qty), x.Item.MinStock is { } m ? N(m) : string.Empty, x.Item.ReorderPoint is { } r ? N(r) : string.Empty]).ToList();
        return new("INV-10", "مواد تحت الحد", "Items below minimum", baseCode, [Col("item", "المادة", "Item"), Col("qty", "الرصيد (كل المستودعات)", "Balance (all warehouses)", AnalyticalColumnKind.Quantity), Col("min", "الحد الأدنى", "Minimum", AnalyticalColumnKind.Quantity), Col("reorder", "نقطة إعادة الطلب", "Reorder point", AnalyticalColumnKind.Quantity)],
            data, null, ["الرصيد مجموع الكمية الأساسية لكل حركات المادة حتى التاريخ المحدد في نطاق الفرع المختار؛ تظهر المادة إن كان الرصيد دون الحد الأدنى أو عند/دون نقطة إعادة الطلب."]);
    }

    private async Task<AnalyticalReport> PriceChangesAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var (start, end) = Range(q);
        var from = new DateTimeOffset(start == DateOnly.MinValue ? DateTime.MinValue.AddDays(1) : start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = new DateTimeOffset(end == DateOnly.MaxValue ? DateTime.MaxValue.AddDays(-1) : end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rows = await context.ItemPrices.AsNoTracking().Where(p => p.CompanyId == scope.CompanyId && p.CreatedAtUtc >= from && p.CreatedAtUtc < to)
            .Join(context.Items.AsNoTracking(), p => p.ItemId, i => i.Id, (p, i) => new { i.Code, i.NameArabic, p.PriceListCode, p.Price, p.CurrencyCode, p.ValidFrom, p.CreatedAtUtc })
            .OrderBy(x => x.Code).ThenBy(x => x.PriceListCode).ThenBy(x => x.CreatedAtUtc).Take(DetailRowLimit).ToListAsync(ct).ConfigureAwait(false);
        var data = rows.Select(x => (IReadOnlyList<string>)[x.Code + " " + x.NameArabic, x.PriceListCode, x.CurrencyCode, A(x.Price), x.ValidFrom is { } v ? D(v) : string.Empty, D(DateOnly.FromDateTime(x.CreatedAtUtc.UtcDateTime))]).ToList();
        return new("INV-16", "تغير أسعار المواد", "Item price changes", baseCode, [Col("item", "المادة", "Item"), Col("list", "قائمة الأسعار", "Price list"), Col("cur", "العملة", "Currency"), Col("price", "السعر", "Price", Amt), Col("from", "ساري من", "Valid from", Dt), Col("created", "سُجِّل في", "Recorded", Dt)],
            data, null, ["كل سطر سعر مسجَّل في الفترة بترتيب التسجيل لكل مادة وقائمة؛ المقارنة بين السطور المتتالية تُظهر التغير."]);
    }
}
