using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>
/// Analytical report suite (FX-04/05, GL-08, FIN-03/04/08). All figures derive from posted (Posted/Reversed) journal lines in the caller's branch scope.
/// Amounts are formatted with four invariant decimals so desktop, print and export show identical values.
/// </summary>
public sealed partial class SqlLedgerQueryService : IAnalyticalReportService
{
    private static readonly string[] AnalyticalIds = ["FX-04", "FX-05", "GL-08", "FIN-03", "FIN-04", "FIN-08", "FIN-09", "SALES-02", "SALES-03", "BUY-02", "GL-12", "FX-09", "INV-11", "INV-13", "INV-07", "FIN-05", "FIN-06", "FIN-07", "CC-03", "CC-04", "CC-06", "CC-07", "CC-08", "CASH-06", "SALES-01", "BUY-01", "BUY-05", "SALES-07", "SALES-10", "INV-10", "INV-16", "SALES-11", "BUY-07", "BUY-08", "ARAP-06", "ARAP-11", "GL-11", "ARAP-05", "ARAP-07", "ARAP-08", "CASH-05", "CASH-07", "INV-14", "FX-06", "FX-07", "INV-09", "INV-12", "GL-10", "SALES-12", "GL-13"];

    public IReadOnlyList<string> SupportedReportIds => AnalyticalIds;

    private static string A(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
    private static string D(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static AnalyticalColumn Col(string key, string ar, string en, AnalyticalColumnKind kind = AnalyticalColumnKind.Text) => new(key, ar, en, kind);
    private const AnalyticalColumnKind Amt = AnalyticalColumnKind.Amount;
    private const AnalyticalColumnKind Dt = AnalyticalColumnKind.Date;

    public async Task<AnalyticalReport> RunAsync(LedgerReportScope scope, string reportId, AnalyticalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        EnsureRange(query.From, query.To);
        await EnsureScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var baseCode = await BaseCurrencyAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false);
        return reportId switch
        {
            "FX-04" => await CurrencyLedgerAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "FX-05" => await CurrencyTrialBalanceAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "GL-08" => await DormantAccountsAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "FIN-03" => await CashFlowDirectAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "FIN-04" => await CashFlowIndirectAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "FIN-08" => await MonthlyRevenueExpenseAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "FIN-09" => await BranchResultsAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "SALES-02" => await MonthlyRoleAsync(scope, query, baseCode, "SALES-02", "مبيعات شهرية", "Monthly sales", 3, 4, true, cancellationToken).ConfigureAwait(false),
            "SALES-03" => await BranchRoleAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "BUY-02" => await MonthlyRoleAsync(scope, query, baseCode, "BUY-02", "مشتريات شهرية", "Monthly purchases", 5, 6, false, cancellationToken).ConfigureAwait(false),
            "GL-12" => await UnpostedEntriesAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "FX-09" => await RateMovementAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "INV-11" => await NegativeStockAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "FX-06" => await DualBalancesAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "FX-07" => await RealizedFxAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "INV-09" => await TransfersAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "INV-12" => await CostOfGoodsSoldAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "GL-10" => await ReversalsAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "SALES-12" => await SalesByCurrencyAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "GL-13" => await BranchMappingSnapshotAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "FIN-05" => await TradingAccountAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "FIN-06" => await OperatingAccountAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "FIN-07" => await EquityChangesAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "CC-03" => await CostCenterBalanceAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "CC-04" => await CostCenterProfitAsync(scope, query, baseCode, "CC-04", "ربحية مركز كلفة", "Cost-centre profitability", false, cancellationToken).ConfigureAwait(false),
            "CC-06" => await CostCenterProfitAsync(scope, query, baseCode, "CC-06", "مراكز الكلفة حسب الفرع", "Cost centres by branch", true, cancellationToken).ConfigureAwait(false),
            "CC-07" => await UnallocatedExpensesAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "CC-08" => await MostActiveCentersAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "CASH-06" => await CashExpensesAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "SALES-01" => await DailyRoleAsync(scope, query, baseCode, "SALES-01", "المبيعات اليومية", "Daily sales", 3, 4, true, cancellationToken).ConfigureAwait(false),
            "BUY-01" => await DailyRoleAsync(scope, query, baseCode, "BUY-01", "المشتريات اليومية", "Daily purchases", 5, 6, false, cancellationToken).ConfigureAwait(false),
            "SALES-07" => await ReturnsOnlyAsync(scope, query, baseCode, "SALES-07", "مردودات المبيعات", "Sales returns", 3, 4, true, cancellationToken).ConfigureAwait(false),
            "BUY-05" => await ReturnsOnlyAsync(scope, query, baseCode, "BUY-05", "مردودات المشتريات", "Purchase returns", 5, 6, false, cancellationToken).ConfigureAwait(false),
            "SALES-10" => await BranchMarginAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "INV-10" => await BelowMinimumAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "INV-16" => await PriceChangesAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "SALES-11" => await PendingSalesInvoicesAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "BUY-07" => await PendingPurchaseInvoicesAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "BUY-08" => await SupplierPriceAnalysisAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "ARAP-06" => await UnallocatedVouchersAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "ARAP-11" => await SettlementMovementsAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "GL-11" => await ReversedEntriesAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "ARAP-05" => await DueOpenItemsAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "ARAP-07" => await AdvancesAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "ARAP-08" => await MonthlyCollectionsAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "CASH-05" => await ChequeMovementsAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "CASH-07" => await VoucherListAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "INV-14" => await InventoryTurnoverAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "INV-07" => await FifoLayersAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            "INV-13" => await SlowMovingAsync(scope, query, baseCode, cancellationToken).ConfigureAwait(false),
            _ => throw new ResourceNotFoundException("report"),
        };
    }

    private static string RequireCurrency(AnalyticalQuery query, string baseCode)
    {
        var code = query.CurrencyCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) throw new RequestValidationException("report.currency_required", "A currency code is required.", "currencyCode");
        if (code == baseCode) throw new RequestValidationException("report.currency_is_base", "Choose a foreign currency; base-currency movements are in the account ledger.", "currencyCode");
        return code;
    }

    /// <summary>Maximum detail rows one analytical response carries; larger sets are flagged, with totals still computed over the whole set.</summary>
    internal const int DetailRowLimit = 100_000;

    private async Task<AnalyticalReport> CurrencyLedgerAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var currency = RequireCurrency(q, baseCode);
        var start = q.From ?? DateOnly.MinValue;
        var lines = ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.TransactionCurrencyCode == currency);
        if (q.AccountId is { } account) lines = lines.Where(l => l.AccountId == account);
        if (q.To is { } to) lines = lines.Where(l => l.Journal!.AccountingDate <= to);
        var opening = await lines.Where(l => l.Journal!.AccountingDate < start).SumAsync(l => (decimal?)(l.TransactionDebit - l.TransactionCredit), ct).ConfigureAwait(false) ?? 0m;
        var baseOpening = await lines.Where(l => l.Journal!.AccountingDate < start).SumAsync(l => (decimal?)(l.BaseDebit - l.BaseCredit), ct).ConfigureAwait(false) ?? 0m;
        var period = lines.Where(l => l.Journal!.AccountingDate >= start);
        var periodCount = await period.CountAsync(ct).ConfigureAwait(false);
        var fullDebit = await period.SumAsync(l => (decimal?)l.TransactionDebit, ct).ConfigureAwait(false) ?? 0m;
        var fullCredit = await period.SumAsync(l => (decimal?)l.TransactionCredit, ct).ConfigureAwait(false) ?? 0m;
        var fullBase = await period.SumAsync(l => (decimal?)(l.BaseDebit - l.BaseCredit), ct).ConfigureAwait(false) ?? 0m;
        var rows = await period
            .OrderBy(l => l.Journal!.AccountingDate).ThenBy(l => l.Journal!.PostingSequence).ThenBy(l => l.Id)
            .Select(l => new { l.Journal!.AccountingDate, l.Journal.PostingSequence, l.AccountId, l.Description, l.TransactionDebit, l.TransactionCredit, l.RateValue, l.BaseDebit, l.BaseCredit })
            .Take(DetailRowLimit).ToListAsync(ct).ConfigureAwait(false);
        var names = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId).ToDictionaryAsync(a => a.Id, a => a.Code, ct).ConfigureAwait(false);
        var data = new List<IReadOnlyList<string>> { new[] { string.Empty, string.Empty, string.Empty, "رصيد افتتاحي", A(0), A(0), A(opening), string.Empty, A(baseOpening) } };
        var running = opening; var baseRunning = baseOpening; decimal td = 0, tc = 0;
        foreach (var r in rows)
        {
            running += r.TransactionDebit - r.TransactionCredit; baseRunning += r.BaseDebit - r.BaseCredit; td += r.TransactionDebit; tc += r.TransactionCredit;
            data.Add([D(r.AccountingDate), r.PostingSequence?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, names.GetValueOrDefault(r.AccountId, string.Empty), r.Description,
                A(r.TransactionDebit), A(r.TransactionCredit), A(running), r.RateValue.ToString("0.########", CultureInfo.InvariantCulture), A(baseRunning)]);
        }

        var truncated = periodCount > rows.Count;
        if (truncated) { td = fullDebit; tc = fullCredit; running = opening + fullDebit - fullCredit; baseRunning = baseOpening + fullBase; }
        var notes = truncated ? new[] { string.Create(CultureInfo.InvariantCulture, $"عُرض أول {rows.Count} سطر من {periodCount}؛ صف المجموع والرصيد الختامي محسوبان على المجموعة الكاملة (الأرصدة الجارية في الأسطر المعروضة فقط). ضيّق المدى لرؤية الباقي.") } : Array.Empty<string>();
        return new("FX-04", "دفتر أستاذ العملة", "Currency ledger", baseCode,
            [Col("date", "التاريخ", "Date", Dt), Col("seq", "التسلسل", "Sequence"), Col("account", "الحساب", "Account"), Col("desc", "البيان", "Description"),
             Col("debit", $"مدين ({currency})", "Debit", Amt), Col("credit", $"دائن ({currency})", "Credit", Amt), Col("balance", $"الرصيد ({currency})", "Balance", Amt),
             Col("rate", "السعر", "Rate"), Col("baseBalance", $"الرصيد ({baseCode})", "Base balance", Amt)],
            data, [string.Empty, string.Empty, string.Empty, "المجموع", A(td), A(tc), A(running), string.Empty, A(baseRunning)], notes);
    }

    private async Task<AnalyticalReport> CurrencyTrialBalanceAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var start = q.From ?? DateOnly.MinValue;
        var lines = ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.TransactionCurrencyCode != baseCode);
        if (!string.IsNullOrWhiteSpace(q.CurrencyCode)) { var c = q.CurrencyCode.Trim().ToUpperInvariant(); lines = lines.Where(l => l.TransactionCurrencyCode == c); }
        if (q.To is { } to) lines = lines.Where(l => l.Journal!.AccountingDate <= to);
        var sums = await lines.GroupBy(l => new { l.AccountId, l.TransactionCurrencyCode }).Select(g => new
        {
            g.Key.AccountId, Currency = g.Key.TransactionCurrencyCode,
            Opening = g.Sum(l => l.Journal!.AccountingDate < start ? l.TransactionDebit - l.TransactionCredit : 0m),
            Debit = g.Sum(l => l.Journal!.AccountingDate >= start ? l.TransactionDebit : 0m),
            Credit = g.Sum(l => l.Journal!.AccountingDate >= start ? l.TransactionCredit : 0m),
            BaseClosing = g.Sum(l => l.BaseDebit - l.BaseCredit),
        }).ToListAsync(ct).ConfigureAwait(false);
        var accounts = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId).ToDictionaryAsync(a => a.Id, ct).ConfigureAwait(false);
        var data = sums.Where(s => s.Opening != 0 || s.Debit != 0 || s.Credit != 0).OrderBy(s => s.Currency, StringComparer.Ordinal).ThenBy(s => accounts[s.AccountId].Code, StringComparer.Ordinal)
            .Select(s => (IReadOnlyList<string>)[s.Currency, accounts[s.AccountId].Code, accounts[s.AccountId].NameArabic, A(s.Opening), A(s.Debit), A(s.Credit), A(s.Opening + s.Debit - s.Credit), A(s.BaseClosing)]).ToList();
        return new("FX-05", "ميزان العملات", "Currency trial balance", baseCode,
            [Col("cur", "العملة", "Currency"), Col("code", "رمز الحساب", "Account code"), Col("name", "الحساب", "Account"), Col("open", "افتتاحي", "Opening", Amt),
             Col("debit", "مدين", "Debit", Amt), Col("credit", "دائن", "Credit", Amt), Col("close", "ختامي بالعملة", "Closing (currency)", Amt), Col("baseClose", $"مكافئ ({baseCode}) عند الترحيل", "Base equivalent at posting", Amt)],
            data, null, ["المكافئ بالأساس هو مجموع المبالغ المرحّلة بأسعار وقت الترحيل، وليس إعادة تقييم."]);
    }

    private async Task<AnalyticalReport> DormantAccountsAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var to = q.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var since = q.From ?? to.AddDays(-90);
        var last = await ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate <= to)
            .GroupBy(l => l.AccountId).Select(g => new { AccountId = g.Key, Last = g.Max(l => l.Journal!.AccountingDate), Balance = g.Sum(l => l.BaseDebit - l.BaseCredit) })
            .ToDictionaryAsync(x => x.AccountId, ct).ConfigureAwait(false);
        var accounts = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId && a.IsActive && a.PostingAllowed).OrderBy(a => a.Code).ToListAsync(ct).ConfigureAwait(false);
        var data = accounts.Where(a => !last.TryGetValue(a.Id, out var x) || x.Last < since)
            .Select(a => (IReadOnlyList<string>)[a.Code, a.NameArabic, last.TryGetValue(a.Id, out var x) ? D(x.Last) : "—", A(last.TryGetValue(a.Id, out var y) ? y.Balance : 0m)]).ToList();
        return new("GL-08", "حسابات راكدة", "Dormant accounts", baseCode,
            [Col("code", "الرمز", "Code"), Col("name", "الحساب", "Account"), Col("last", "آخر حركة", "Last movement", Dt), Col("balance", "الرصيد", "Balance", Amt)],
            data, null, [$"حسابات فعّالة قابلة للترحيل بلا حركة مرحّلة منذ {D(since)} حتى {D(to)}."]);
    }

    private async Task<HashSet<Guid>> CashAccountIdsAsync(Guid companyId, CancellationToken ct) =>
        (await context.BranchAccountLinks.AsNoTracking().Where(l => l.CompanyId == companyId && (l.Role == 1 || l.Role == 2)).Select(l => l.AccountId).Distinct().ToListAsync(ct).ConfigureAwait(false)).ToHashSet();

    private const string OperatingSection = "التشغيل", FinancingSection = "التمويل (حقوق الملكية)", UnclassifiedSection = "غير مصنّف (يلزم تصنيف التدفق)";

    /// <summary>Working-capital accounts recognised by branch role (inventory 7, receivable 9, payable 10) are operating; equity is financing;
    /// every other balance-sheet account is reported as unclassified rather than silently counted as operating.</summary>
    private async Task<HashSet<Guid>> WorkingCapitalIdsAsync(Guid companyId, CancellationToken ct)
    {
        var ids = new HashSet<Guid>();
        foreach (byte role in new byte[] { 7, 9, 10 }) ids.UnionWith(await RoleAccountIdsAsync(companyId, role, ct).ConfigureAwait(false));
        return ids;
    }

    private static string CashFlowSection(byte accountType, Guid accountId, HashSet<Guid> workingCapital) => accountType switch
    {
        3 => FinancingSection,
        4 or 5 => OperatingSection,
        _ => workingCapital.Contains(accountId) ? OperatingSection : UnclassifiedSection,
    };

    private async Task<AnalyticalReport> CashFlowDirectAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var cash = await CashAccountIdsAsync(scope.CompanyId, ct).ConfigureAwait(false);
        if (cash.Count == 0) throw new BusinessRuleException("report.no_cash_accounts", "Map the branch Cash/Bank accounts first (branch account mappings).", "accountId");
        var start = q.From ?? DateOnly.MinValue; var to = q.To ?? DateOnly.MaxValue;
        var posted = ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State));
        var openingCash = await posted.Where(l => cash.Contains(l.AccountId) && l.Journal!.AccountingDate < start).SumAsync(l => (decimal?)(l.BaseDebit - l.BaseCredit), ct).ConfigureAwait(false) ?? 0m;
        var closingCash = await posted.Where(l => cash.Contains(l.AccountId) && l.Journal!.AccountingDate <= to).SumAsync(l => (decimal?)(l.BaseDebit - l.BaseCredit), ct).ConfigureAwait(false) ?? 0m;
        var touching = posted.Where(l => l.Journal!.AccountingDate >= start && l.Journal.AccountingDate <= to && cash.Contains(l.AccountId)).Select(l => l.JournalId).Distinct();
        var counter = await posted.Where(l => touching.Contains(l.JournalId) && !cash.Contains(l.AccountId))
            .GroupBy(l => l.AccountId).Select(g => new { AccountId = g.Key, Net = g.Sum(l => l.BaseCredit - l.BaseDebit) }).ToListAsync(ct).ConfigureAwait(false);
        var accounts = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId).ToDictionaryAsync(a => a.Id, ct).ConfigureAwait(false);
        var workingCapital = await WorkingCapitalIdsAsync(scope.CompanyId, ct).ConfigureAwait(false);
        var data = new List<IReadOnlyList<string>>();
        decimal inflow = 0, outflow = 0;
        foreach (var grp in counter.Where(c => c.Net != 0).GroupBy(c => CashFlowSection(accounts[c.AccountId].AccountType, c.AccountId, workingCapital)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            data.Add([grp.Key, string.Empty, string.Empty, string.Empty]);
            foreach (var c in grp.OrderBy(c => accounts[c.AccountId].Code, StringComparer.Ordinal))
            {
                var inn = c.Net > 0 ? c.Net : 0m; var outt = c.Net < 0 ? -c.Net : 0m; inflow += inn; outflow += outt;
                data.Add([string.Empty, accounts[c.AccountId].Code + " " + accounts[c.AccountId].NameArabic, A(inn), A(outt)]);
            }
        }

        data.Add(["رصيد النقد أول المدة", string.Empty, A(openingCash), string.Empty]);
        data.Add(["صافي التغير في النقد", string.Empty, A(inflow - outflow), string.Empty]);
        data.Add(["رصيد النقد آخر المدة", string.Empty, A(closingCash), string.Empty]);
        var notes = new List<string> { "الطريقة المباشرة: كل حركة نقدية تُنسب إلى الحسابات المقابلة في القيود التي تمس حسابات الصندوق/البنك المربوطة بالفروع.",
            "التصنيف: إيرادات/مصروفات وحسابات رأس المال العامل المربوطة بأدوار (مخزون، ذمم مدينة، ذمم دائنة) = تشغيل؛ حقوق الملكية = تمويل؛ أي حساب ميزانية آخر يظهر تحت «غير مصنّف» ولا يُحتسب تشغيلياً. لا يوجد قسم استثمار قبل إضافة تصنيف تدفق لكل حساب." };
        if (data.Any(r => r[0] == UnclassifiedSection)) notes.Add("تنبيه: توجد حركات نقدية مقابلة لحسابات غير مصنفة تدفقاً (قد تكون استثمارية أو تمويلية).");
        if (openingCash + inflow - outflow != closingCash) notes.Add("تحذير: فرق بين الصافي المحسوب ورصيد النقد الختامي؛ راجع قيوداً غير متوازنة بين الفروع.");
        return new("FIN-03", "قائمة التدفقات النقدية — مباشر", "Cash flow (direct)", baseCode,
            [Col("section", "القسم", "Section"), Col("account", "الحساب المقابل", "Counter account"), Col("in", "مقبوضات", "Inflow", Amt), Col("out", "مدفوعات", "Outflow", Amt)],
            data, ["المجموع", string.Empty, A(inflow), A(outflow)], notes);
    }

    private async Task<AnalyticalReport> CashFlowIndirectAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var cash = await CashAccountIdsAsync(scope.CompanyId, ct).ConfigureAwait(false);
        if (cash.Count == 0) throw new BusinessRuleException("report.no_cash_accounts", "Map the branch Cash/Bank accounts first (branch account mappings).", "accountId");
        var start = q.From ?? DateOnly.MinValue; var to = q.To ?? DateOnly.MaxValue;
        var period = ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate >= start && l.Journal.AccountingDate <= to);
        var byAccount = await period.GroupBy(l => l.AccountId).Select(g => new { AccountId = g.Key, Net = g.Sum(l => l.BaseDebit - l.BaseCredit) }).ToListAsync(ct).ConfigureAwait(false);
        var accounts = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId).ToDictionaryAsync(a => a.Id, ct).ConfigureAwait(false);
        var netIncome = -byAccount.Where(x => accounts[x.AccountId].AccountType is 4 or 5).Sum(x => x.Net);
        var data = new List<IReadOnlyList<string>> { new[] { "صافي الربح (إيرادات − مصاريف)", A(netIncome) } };
        var workingCapital = await WorkingCapitalIdsAsync(scope.CompanyId, ct).ConfigureAwait(false);
        decimal operating = netIncome, financing = 0, unclassified = 0;
        foreach (var x in byAccount.Where(x => !cash.Contains(x.AccountId) && accounts[x.AccountId].AccountType is 1 or 2 or 3 && x.Net != 0).OrderBy(x => accounts[x.AccountId].Code, StringComparer.Ordinal))
        {
            var effect = -x.Net; // an increase in a debit-balance account consumes cash; an increase in a credit-balance account supplies it
            var section = CashFlowSection(accounts[x.AccountId].AccountType, x.AccountId, workingCapital);
            if (section == FinancingSection) financing += effect; else if (section == OperatingSection) operating += effect; else unclassified += effect;
            var prefix = section == FinancingSection ? "تمويل: " : section == OperatingSection ? "تغير تشغيلي: " : "غير مصنّف: ";
            data.Add([prefix + accounts[x.AccountId].Code + " " + accounts[x.AccountId].NameArabic, A(effect)]);
        }

        var cashChange = byAccount.Where(x => cash.Contains(x.AccountId)).Sum(x => x.Net);
        data.Add(["صافي التدفق التشغيلي", A(operating)]);
        data.Add(["صافي التدفق التمويلي (حقوق الملكية)", A(financing)]);
        data.Add(["صافي التدفق غير المصنّف", A(unclassified)]);
        data.Add(["التغير المحسوب في النقد", A(operating + financing + unclassified)]);
        data.Add(["التغير الفعلي في النقد", A(cashChange)]);
        var notes = new List<string> { "الطريقة غير المباشرة: صافي الربح معدَّلاً بتغير أرصدة الأصول غير النقدية والخصوم وحقوق الملكية خلال المدة.",
            "حسابات الميزانية غير المربوطة بأدوار رأس المال العامل تظهر في «غير مصنّف» ولا تُدمج في التشغيل؛ لا قسم استثمار قبل إضافة تصنيف تدفق لكل حساب." };
        if (operating + financing + unclassified != cashChange) notes.Add("تحذير: التغير المحسوب لا يساوي التغير الفعلي في النقد.");
        return new("FIN-04", "التدفقات النقدية — غير مباشر", "Cash flow (indirect)", baseCode,
            [Col("item", "البند", "Item"), Col("amount", "المبلغ", "Amount", Amt)], data, null, notes);
    }

    private async Task<AnalyticalReport> MonthlyRevenueExpenseAsync(LedgerReportScope scope, AnalyticalQuery q, string baseCode, CancellationToken ct)
    {
        var start = q.From ?? new DateOnly(DateTime.UtcNow.Year, 1, 1); var to = q.To ?? start.AddYears(1).AddDays(-1);
        var rows = await ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate >= start && l.Journal.AccountingDate <= to)
            .Join(context.Accounts.AsNoTracking(), l => l.AccountId, a => a.Id, (l, a) => new { l.Journal!.AccountingDate, a.AccountType, l.BaseDebit, l.BaseCredit })
            .Where(x => x.AccountType == 4 || x.AccountType == 5)
            .GroupBy(x => new { x.AccountingDate.Year, x.AccountingDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(x => x.AccountType == 4 ? x.BaseCredit - x.BaseDebit : 0m), Expense = g.Sum(x => x.AccountType == 5 ? x.BaseDebit - x.BaseCredit : 0m) })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ToListAsync(ct).ConfigureAwait(false);
        var data = rows.Select(r => (IReadOnlyList<string>)[string.Create(CultureInfo.InvariantCulture, $"{r.Year}-{r.Month:00}"), A(r.Revenue), A(r.Expense), A(r.Revenue - r.Expense)]).ToList();
        return new("FIN-08", "تحليل الإيرادات والمصاريف الشهري", "Monthly revenue / expense analysis", baseCode,
            [Col("month", "الشهر", "Month"), Col("rev", "الإيرادات", "Revenue", Amt), Col("exp", "المصاريف", "Expenses", Amt), Col("net", "الصافي", "Net", Amt)],
            data, ["المجموع", A(rows.Sum(r => r.Revenue)), A(rows.Sum(r => r.Expense)), A(rows.Sum(r => r.Revenue - r.Expense))], []);
    }
}
