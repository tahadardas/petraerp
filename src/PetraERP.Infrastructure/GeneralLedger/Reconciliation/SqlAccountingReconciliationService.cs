using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reconciliation;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Infrastructure.GeneralLedger.Reconciliation;

public sealed partial class SqlAccountingReconciliationService(FinancialDbContext context, TimeProvider timeProvider, IPriorYearClosingSource? priorYear = null) : IAccountingReconciliationService
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };
    public async Task<ReconciliationReportDto> RunAllReconciliationsAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, CancellationToken cancellationToken = default)
    {
        var findings = new List<ReconciliationFindingDto>();

        // 1. CHECK_01_TRIAL_BALANCE
        await CheckTrialBalanceAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        // 2. CHECK_02_GL_VS_JOURNAL_LINES
        await CheckJournalLinesBalancedAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        // 3. CHECK_03_AR_SUBLEDGER_VS_CONTROL
        await CheckCustomerSubledgerAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        // 4. CHECK_04_AP_SUBLEDGER_VS_CONTROL
        await CheckSupplierSubledgerAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        // 5. CHECK_05_TREASURY_VS_GL
        await CheckTreasuryVsGlAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        // 6. CHECK_06_INVENTORY_VS_GL
        await CheckInventoryVsGlAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        // 7. CHECK_07_SALES_DOCS_VS_POSTING
        await CheckSalesDocsVsPostingAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        // 8. CHECK_08_PURCHASE_DOCS_VS_POSTING
        await CheckPurchaseDocsVsPostingAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        // 9. CHECK_09_SETTLEMENT_ALLOCATIONS
        await CheckSettlementAllocationsAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        // 10. CHECK_10_PERIOD_CONTINUITY
        await CheckPeriodContinuityAsync(companyId, fiscalYearId, findings, cancellationToken).ConfigureAwait(false);

        // 11. CHECK_11_YEAR_ROLLOVER_CONTINUITY
        await CheckRolloverContinuityAsync(companyId, fiscalYearId, findings, cancellationToken).ConfigureAwait(false);

        // 12. CHECK_12_FX_EXPOSURE_REVALUATION
        await CheckFxRevaluationsAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        // 13. CHECK_13_DOC_STATE_VS_POSTING_STATE
        await CheckDocStateVsPostingStateAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        // 14. CHECK_14_DUPLICATE_OR_ORPHAN_POSTING
        await CheckDuplicateOrOrphanPostingAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        // 15. CHECK_15_DUPLICATE_DRAFTS (read-only; never deletes)
        await CheckDuplicateDraftsAsync(companyId, fiscalYearId, branchId, findings, cancellationToken).ConfigureAwait(false);

        var total = findings.Count;
        var passed = findings.Count(f => f.Severity == "PASS");
        var warnings = findings.Count(f => f.Severity == "WARNING");
        var blockings = findings.Count(f => f.Severity == "BLOCKING");

        return new ReconciliationReportDto(
            companyId,
            fiscalYearId,
            timeProvider.GetUtcNow(),
            total,
            passed,
            warnings,
            blockings,
            blockings > 0,
            findings);
    }

    public async Task<string> ExportReportToJsonAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, CancellationToken cancellationToken = default)
    {
        var report = await RunAllReconciliationsAsync(companyId, fiscalYearId, branchId, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(report, IndentedJsonOptions);
    }

    public async Task<string> ExportReportToCsvAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, CancellationToken cancellationToken = default)
    {
        var report = await RunAllReconciliationsAsync(companyId, fiscalYearId, branchId, cancellationToken).ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.AppendLine("CheckCode,Severity,Difference,SourceAmount,CalculatedAmount,Explanation,SuggestedAction");
        foreach (var f in report.Findings)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\"",
                f.CheckCode, f.Severity, f.Difference?.ToString(CultureInfo.InvariantCulture) ?? "",
                f.SourceAmount?.ToString(CultureInfo.InvariantCulture) ?? "",
                f.CalculatedAmount?.ToString(CultureInfo.InvariantCulture) ?? "",
                f.Explanation.Replace("\"", "\"\""),
                (f.SuggestedAction ?? "").Replace("\"", "\"\"")));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Posted journals of one fiscal year, scoped to that year's own date range. A finding is stamped with the year, so an imbalance booked
    /// in another year must neither block nor silently clear the year under review. An unknown year has no range to scope to.
    /// </summary>
    private async Task<IQueryable<JournalEntryRow>> PostedJournalEntriesAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, CancellationToken token)
    {
        var range = await context.FiscalYears.AsNoTracking().Where(y => y.CompanyId == companyId && y.Id == fiscalYearId)
            .Select(y => new { y.RangeStart, y.RangeEnd }).FirstOrDefaultAsync(token).ConfigureAwait(false);
        var posted = context.JournalEntries.AsNoTracking()
            .Where(j => j.CompanyId == companyId && (j.State == 4 || j.State == 5) && (!branchId.HasValue || j.BranchId == branchId.Value));
        return range is null ? posted : posted.Where(j => j.AccountingDate >= range.RangeStart && j.AccountingDate <= range.RangeEnd);
    }

    private async Task CheckTrialBalanceAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        // Aggregated by SQL Server over decimal columns: exact, and no per-line materialisation on a large ledger.
        var journals = await PostedJournalEntriesAsync(companyId, fiscalYearId, branchId, token).ConfigureAwait(false);
        var totals = await (from l in context.JournalLines.AsNoTracking()
                            join j in journals on l.JournalId equals j.Id
                            group l by 1 into g
                            select new { Debit = g.Sum(x => x.BaseDebit), Credit = g.Sum(x => x.BaseCredit) })
            .FirstOrDefaultAsync(token).ConfigureAwait(false);
        var debit = totals?.Debit ?? 0m;
        var credit = totals?.Credit ?? 0m;
        var diff = debit - credit;

        findings.Add(new ReconciliationFindingDto(
            Guid.NewGuid(), companyId, fiscalYearId, branchId,
            "CHECK_01_TRIAL_BALANCE",
            null, null, null, null, null, null,
            debit, credit, diff,
            diff == 0 ? "PASS" : "BLOCKING",
            diff == 0 ? "ميزان المراجعة متوازن تماماً / Trial balance is perfectly balanced." : $"ميزان المراجعة غير متوازن بفارق {diff}",
            diff == 0 ? null : "راجع قيود اليومية غير المتوازنة وأصلح القيود المرحلة الشاذة",
            null));
    }

    private async Task CheckJournalLinesBalancedAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        var journals = await PostedJournalEntriesAsync(companyId, fiscalYearId, branchId, token).ConfigureAwait(false);
        var unbalancedQuery = from l in context.JournalLines.AsNoTracking()
                              join j in journals on l.JournalId equals j.Id
                              group l by new { j.Id, j.Description } into g
                              where g.Sum(x => x.BaseDebit) != g.Sum(x => x.BaseCredit)
                              select new { g.Key.Id, g.Key.Description, Diff = g.Sum(x => x.BaseDebit) - g.Sum(x => x.BaseCredit) };

        // The count is the finding; taking five rows would report a five-journal problem when there are five hundred.
        var unbalancedCount = await unbalancedQuery.CountAsync(token).ConfigureAwait(false);
        var examples = unbalancedCount == 0 ? [] : await unbalancedQuery.Take(5).ToListAsync(token).ConfigureAwait(false);

        findings.Add(new ReconciliationFindingDto(
            Guid.NewGuid(), companyId, fiscalYearId, branchId,
            "CHECK_02_GL_VS_JOURNAL_LINES",
            null, null, null, null, null, null,
            0m, 0m, unbalancedCount,
            unbalancedCount == 0 ? "PASS" : "BLOCKING",
            unbalancedCount == 0 ? "جميع أسطر القيود متطابقة مع ميزان الحسابات العام / All journal lines balance cleanly." : $"يوجد {unbalancedCount} قيود غير متوازنة داخلياً",
            unbalancedCount == 0 ? null : "عكس القيود غير المتوازنة وإنشاء قيود تصويبية",
            examples.Count > 0 ? examples[0].Id.ToString() : null));
    }

    private const decimal Tolerance = 0.01m;

    private static ReconciliationFindingDto Finding(Guid companyId, Guid fiscalYearId, Guid? branchId, string code, decimal? source, decimal? calculated,
        decimal? difference, string severity, string explanation, string? action = null, string? evidence = null, Guid? accountId = null) =>
        new(Guid.NewGuid(), companyId, fiscalYearId, branchId, code, accountId, null, null, null, null, null, source, calculated, difference,
            severity, explanation, action, evidence);

    /// <summary>A check that cannot be grounded yet is reported as such (WARNING), never as PASS.</summary>
    private static ReconciliationFindingDto NotImplemented(Guid companyId, Guid fiscalYearId, Guid? branchId, string code, string reason) =>
        Finding(companyId, fiscalYearId, branchId, code, null, null, null, "WARNING",
            "NOT_IMPLEMENTED: لم يُنفَّذ هذا الفحص بعد ولا يُعتبر ناجحاً / This check is not implemented and is not a pass. " + reason,
            "نفِّذ مقارنة مستقلة لهذا الفحص قبل الاعتماد المالي");

    private async Task CheckCustomerSubledgerAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token) =>
        findings.Add(await SubledgerVsControlAsync(companyId, fiscalYearId, branchId, "CHECK_03_AR_SUBLEDGER_VS_CONTROL", customers: true, token).ConfigureAwait(false));

    private async Task CheckSupplierSubledgerAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token) =>
        findings.Add(await SubledgerVsControlAsync(companyId, fiscalYearId, branchId, "CHECK_04_AP_SUBLEDGER_VS_CONTROL", customers: false, token).ConfigureAwait(false));

    // Every posted voucher must own a posted journal whose debit total equals voucher amount x rate.
    private async Task CheckTreasuryVsGlAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        var rows = await context.Vouchers.AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.Status == 2 && (!branchId.HasValue || v.BranchId == branchId.Value))
            .Select(v => new
            {
                v.Id, v.Amount, v.ExchangeRate, v.JournalEntryId,
                JournalState = context.JournalEntries.Where(j => j.Id == v.JournalEntryId).Select(j => (byte?)j.State).FirstOrDefault(),
                Debit = context.JournalLines.Where(l => l.JournalId == v.JournalEntryId).Sum(l => (decimal?)l.BaseDebit) ?? 0m,
            }).ToListAsync(token).ConfigureAwait(false);
        var missing = rows.Where(r => r.JournalEntryId is null || r.JournalState is not (4 or 5)).ToList();
        var mismatched = rows.Except(missing).Where(r => Math.Abs(r.Debit - r.Amount * r.ExchangeRate) > Tolerance * 10).ToList();
        var expected = rows.Sum(r => r.Amount * r.ExchangeRate);
        var actual = rows.Sum(r => r.Debit);
        var severity = missing.Count > 0 ? "BLOCKING" : mismatched.Count > 0 ? "WARNING" : "PASS";
        findings.Add(Finding(companyId, fiscalYearId, branchId, "CHECK_05_TREASURY_VS_GL", expected, actual, Math.Abs(expected - actual), severity,
            severity == "PASS" ? "كل سند نقدي مرحَّل يملك قيداً مرحَّلاً بمبلغ مطابق / Every posted voucher has a matching posted journal."
                : $"سندات مرحلة بلا قيد مرحَّل: {missing.Count}؛ سندات بمبلغ قيد مختلف: {mismatched.Count}",
            severity == "PASS" ? null : "راجع السندات المذكورة وقيودها", (missing.Concat(mismatched).FirstOrDefault())?.Id.ToString()));
    }

    private async Task CheckSalesDocsVsPostingAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        var unposted = await context.SalesInvoices.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.Status == 2 && s.JournalEntryId == null && (!branchId.HasValue || s.BranchId == branchId.Value))
            .CountAsync(token).ConfigureAwait(false);
        findings.Add(Finding(companyId, fiscalYearId, branchId, "CHECK_07_SALES_DOCS_VS_POSTING", 0m, 0m, unposted, unposted == 0 ? "PASS" : "BLOCKING",
            unposted == 0 ? "جميع فواتير البيع المرحلة تحمل قيوداً محاسبية نظامية / All posted sales invoices have authoritative journals." : $"يوجد {unposted} فواتير بيع مرحلة تفتقر لقيود محاسبية",
            unposted == 0 ? null : "إعادة ترحيل الفواتير المعلقة"));
    }

    private async Task CheckPurchaseDocsVsPostingAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        var unposted = await context.PurchaseInvoices.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.Status == 2 && p.JournalEntryId == null && (!branchId.HasValue || p.BranchId == branchId.Value))
            .CountAsync(token).ConfigureAwait(false);
        findings.Add(Finding(companyId, fiscalYearId, branchId, "CHECK_08_PURCHASE_DOCS_VS_POSTING", 0m, 0m, unposted, unposted == 0 ? "PASS" : "BLOCKING",
            unposted == 0 ? "جميع فواتير الشراء المرحلة تحمل قيوداً محاسبية نظامية / All posted purchase invoices have authoritative journals." : $"يوجد {unposted} فواتير شراء مرحلة تفتقر لقيود محاسبية",
            unposted == 0 ? null : "إعادة ترحيل الفواتير المعلقة"));
    }

    // The fiscal year's accounting periods must be ordered, contiguous, non-overlapping, start on the year's first day and end on its last day.
    private async Task CheckPeriodContinuityAsync(Guid companyId, Guid fiscalYearId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        var year = await context.FiscalYears.AsNoTracking().Where(y => y.CompanyId == companyId && y.Id == fiscalYearId)
            .Select(y => new { y.Code, y.RangeStart, y.RangeEnd }).FirstOrDefaultAsync(token).ConfigureAwait(false);
        var periods = await context.AccountingPeriods.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.FiscalYearId == fiscalYearId).OrderBy(p => p.RangeStart)
            .Select(p => new { p.Code, p.RangeStart, p.RangeEnd }).ToListAsync(token).ConfigureAwait(false);
        var problems = new List<string>();
        if (year is null) problems.Add("السنة المالية غير موجودة");
        if (periods.Count == 0) problems.Add("لا توجد فترات محاسبية للسنة");
        for (var i = 1; i < periods.Count; i++)
        {
            var expected = periods[i - 1].RangeEnd.AddDays(1);
            if (periods[i].RangeStart != expected) problems.Add($"{periods[i - 1].Code}→{periods[i].Code}: المتوقع {expected:yyyy-MM-dd} والفعلي {periods[i].RangeStart:yyyy-MM-dd}");
        }
        if (year is not null && periods.Count > 0)
        {
            if (periods[0].RangeStart != year.RangeStart) problems.Add($"أول فترة {periods[0].Code} تبدأ {periods[0].RangeStart:yyyy-MM-dd} وبداية السنة {year.RangeStart:yyyy-MM-dd}");
            if (periods[^1].RangeEnd != year.RangeEnd) problems.Add($"آخر فترة {periods[^1].Code} تنتهي {periods[^1].RangeEnd:yyyy-MM-dd} ونهاية السنة {year.RangeEnd:yyyy-MM-dd}");
            foreach (var p in periods.Where(p => p.RangeStart < year.RangeStart || p.RangeEnd > year.RangeEnd)) problems.Add($"{p.Code} خارج حدود السنة");
        }
        findings.Add(Finding(companyId, fiscalYearId, null, "CHECK_10_PERIOD_CONTINUITY", periods.Count, periods.Count, problems.Count,
            problems.Count == 0 ? "PASS" : "BLOCKING",
            problems.Count == 0 ? "الفترات المحاسبية متصلة وتغطي السنة من أول يوم إلى آخر يوم دون ثغرات أو تداخل / Periods are contiguous and cover the whole year." : "ثغرات أو تداخل أو عدم تغطية للسنة: " + string.Join("؛ ", problems.Take(5)),
            problems.Count == 0 ? null : "صحح حدود الفترات المحاسبية"));
    }

    // FX: (1) request tracking integrity; (2) independent recomputation of the unrevalued exposure: for every monetary-eligible account holding ONE foreign
    // currency, round(foreign balance x closing rate) - book base balance. Exposure is expected between revaluations, so a residual is a WARNING with the
    // amount (never PASS); incomplete/unlinked revaluation requests and unusable data are BLOCKING.
    private async Task CheckFxRevaluationsAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        var requests = await context.FxRevaluationRequests.AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.FiscalYearId == fiscalYearId && (!branchId.HasValue || r.BranchId == branchId.Value))
            .Select(r => new
            {
                r.Id, r.Status, r.OutcomeJournalId,
                OutcomePosted = context.JournalEntries.Any(j => j.Id == r.OutcomeJournalId && (j.State == 4 || j.State == 5)),
            }).ToListAsync(token).ConfigureAwait(false);
        var pending = requests.Count(r => r.Status == 1);
        var missingJournal = requests.Count(r => r.Status == 2 && !r.OutcomePosted);
        var integrityBad = pending + missingJournal;

        var company = await context.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BaseCurrencyCode).FirstOrDefaultAsync(token).ConfigureAwait(false);
        var yearEnd = await context.FiscalYears.AsNoTracking().Where(y => y.CompanyId == companyId && y.Id == fiscalYearId).Select(y => (DateOnly?)y.RangeEnd).FirstOrDefaultAsync(token).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var asOf = yearEnd is { } end && end < today ? end : today;

        var foreign = company is null ? [] : await (
            from line in context.JournalLines.AsNoTracking()
            join entry in context.JournalEntries.AsNoTracking() on line.JournalId equals entry.Id
            join account in context.Accounts.AsNoTracking() on line.AccountId equals account.Id
            where entry.CompanyId == companyId && (!branchId.HasValue || entry.BranchId == branchId.Value) && (entry.State == 4 || entry.State == 5)
                  && entry.AccountingDate <= asOf && account.FxMonetaryEligibility == 1 && line.TransactionCurrencyCode != company
            group line by new { line.AccountId, line.TransactionCurrencyCode } into g
            select new { g.Key.AccountId, Currency = g.Key.TransactionCurrencyCode, Foreign = g.Sum(x => x.TransactionDebit - x.TransactionCredit) })
            .ToListAsync(token).ConfigureAwait(false);
        var accountIds = foreign.Select(f => f.AccountId).Distinct().ToList();
        var book = accountIds.Count == 0 ? [] : await (
            from line in context.JournalLines.AsNoTracking()
            join entry in context.JournalEntries.AsNoTracking() on line.JournalId equals entry.Id
            where entry.CompanyId == companyId && (!branchId.HasValue || entry.BranchId == branchId.Value) && (entry.State == 4 || entry.State == 5)
                  && entry.AccountingDate <= asOf && accountIds.Contains(line.AccountId)
            group line by line.AccountId into g
            select new { AccountId = g.Key, Base = g.Sum(x => x.BaseDebit - x.BaseCredit) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Base, token).ConfigureAwait(false);

        decimal residual = 0m; var ambiguous = 0; var noRate = 0; var open = 0;
        foreach (var f in foreign)
        {
            if (foreign.Count(x => x.AccountId == f.AccountId) > 1) { ambiguous++; continue; }
            var rate = await CompanyCurrencyRateAsync(companyId, company!, f.Currency, asOf, token).ConfigureAwait(false);
            if (rate is null) { noRate++; continue; }
            var diff = Math.Round(f.Foreign * rate.Value, 4, MidpointRounding.AwayFromZero) - book.GetValueOrDefault(f.AccountId);
            if (Math.Abs(diff) > Tolerance) { residual += Math.Abs(diff); open++; }
        }

        var blocking = integrityBad > 0;
        var warning = open > 0 || ambiguous > 0 || noRate > 0;
        findings.Add(Finding(companyId, fiscalYearId, branchId, "CHECK_12_FX_EXPOSURE_REVALUATION", foreign.Count, foreign.Count - open - ambiguous - noRate, residual,
            blocking ? "BLOCKING" : warning ? "WARNING" : "PASS",
            blocking ? $"طلبات إعادة تقييم عملات معلقة: {pending}؛ مكتملة بلا قيد مرحَّل: {missingJournal}"
                : warning ? $"تعرض عملات غير معاد تقييمه بتاريخ {asOf:yyyy-MM-dd}: حسابات={open} إجمالي الفارق={residual}؛ حسابات بعملات متعددة (تحتاج مراجعة)={ambiguous}؛ عملات بلا سعر={noRate}"
                : "لا تعرض غير معاد تقييمه، وطلبات إعادة التقييم سليمة / No unrevalued FX exposure; revaluation requests are consistent.",
            blocking ? "أكمل أو ألغِ الطلبات المعلقة وراجع القيود" : warning ? "نفّذ إعادة تقييم العملات الأجنبية أو راجع الحسابات المذكورة" : null));
    }

    private async Task<decimal?> CompanyCurrencyRateAsync(Guid companyId, string baseCurrency, string currency, DateOnly date, CancellationToken token)
    {
        var row = await PetraERP.Infrastructure.Currencies.CompanyCurrencies.EffectiveRateAsync(context, companyId, baseCurrency, currency, date, token).ConfigureAwait(false);
        return row is { Rate: > 0m } ? row.Rate : null;
    }

    // A document's state must agree with its journal: posted => posted journal; draft/approved => no journal.
    private async Task CheckDocStateVsPostingStateAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        var salesBad = await context.SalesInvoices.AsNoTracking().Where(s => s.CompanyId == companyId && (!branchId.HasValue || s.BranchId == branchId.Value)
            && ((s.Status == 2 && (s.JournalEntryId == null || !context.JournalEntries.Any(j => j.Id == s.JournalEntryId && j.CompanyId == companyId && (j.State == 4 || j.State == 5))))
                || ((s.Status == 0 || s.Status == 1) && s.JournalEntryId != null))).CountAsync(token).ConfigureAwait(false);
        var purchaseBad = await context.PurchaseInvoices.AsNoTracking().Where(p => p.CompanyId == companyId && (!branchId.HasValue || p.BranchId == branchId.Value)
            && ((p.Status == 2 && (p.JournalEntryId == null || !context.JournalEntries.Any(j => j.Id == p.JournalEntryId && j.CompanyId == companyId && (j.State == 4 || j.State == 5))))
                || ((p.Status == 0 || p.Status == 1) && p.JournalEntryId != null))).CountAsync(token).ConfigureAwait(false);
        var voucherBad = await context.Vouchers.AsNoTracking().Where(v => v.CompanyId == companyId && (!branchId.HasValue || v.BranchId == branchId.Value)
            && ((v.Status == 2 && (v.JournalEntryId == null || !context.JournalEntries.Any(j => j.Id == v.JournalEntryId && j.CompanyId == companyId && (j.State == 4 || j.State == 5))))
                || ((v.Status == 0 || v.Status == 1) && v.JournalEntryId != null))).CountAsync(token).ConfigureAwait(false);
        var bad = salesBad + purchaseBad + voucherBad;
        findings.Add(Finding(companyId, fiscalYearId, branchId, "CHECK_13_DOC_STATE_VS_POSTING_STATE", 0m, 0m, bad, bad == 0 ? "PASS" : "BLOCKING",
            bad == 0 ? "حالات المستندات تطابق حالات قيودها / Document states agree with their journals."
                : $"حالة مستند لا تطابق قيده — مبيعات: {salesBad}، مشتريات: {purchaseBad}، سندات: {voucherBad}",
            bad == 0 ? null : "راجع المستندات المذكورة؛ لا تُعدَّل البيانات تلقائياً"));
    }

    // Pre-existing duplicate drafts (same party, warehouse, day, currency, total and line set). Reported only; never auto-deleted.
    private async Task CheckDuplicateDraftsAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        var sales = await context.SalesInvoices.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.Status == 0 && (!branchId.HasValue || s.BranchId == branchId.Value))
            .Select(s => new { s.Id, s.BranchId, Party = s.CustomerId, s.WarehouseId, s.InvoiceDateUtc, s.CurrencyCode, s.GrandTotal,
                Lines = s.Lines.Select(l => new { l.ItemId, l.UnitId, l.Quantity, l.UnitPrice }).ToList() })
            .ToListAsync(token).ConfigureAwait(false);
        var purchases = await context.PurchaseInvoices.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.Status == 0 && (!branchId.HasValue || p.BranchId == branchId.Value))
            .Select(p => new { p.Id, p.BranchId, Party = p.SupplierId, p.WarehouseId, p.InvoiceDateUtc, p.CurrencyCode, p.GrandTotal,
                Lines = p.Lines.Select(l => new { l.ItemId, l.UnitId, l.Quantity, UnitPrice = l.UnitCost }).ToList() })
            .ToListAsync(token).ConfigureAwait(false);

        static string Key(string kind, Guid branch, Guid party, Guid warehouse, DateTimeOffset date, string currency, decimal total, IEnumerable<string> lines) =>
            string.Join('|', kind, branch, party, warehouse, date.UtcDateTime.Date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
                currency, total.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture), string.Join(';', lines.OrderBy(x => x, StringComparer.Ordinal)));

        var keyed = sales.Select(s => (s.Id, Key: Key("S", s.BranchId, s.Party, s.WarehouseId, s.InvoiceDateUtc, s.CurrencyCode, s.GrandTotal,
                s.Lines.Select(l => FormattableString.Invariant($"{l.ItemId}:{l.UnitId}:{l.Quantity:0.####}:{l.UnitPrice:0.####}")))))
            .Concat(purchases.Select(p => (p.Id, Key: Key("P", p.BranchId, p.Party, p.WarehouseId, p.InvoiceDateUtc, p.CurrencyCode, p.GrandTotal,
                p.Lines.Select(l => FormattableString.Invariant($"{l.ItemId}:{l.UnitId}:{l.Quantity:0.####}:{l.UnitPrice:0.####}"))))));
        var groups = keyed.GroupBy(x => x.Key).Where(g => g.Count() > 1).ToList();
        var extra = groups.Sum(g => g.Count() - 1);
        findings.Add(Finding(companyId, fiscalYearId, branchId, "CHECK_15_DUPLICATE_DRAFTS", 0m, 0m, extra, extra == 0 ? "PASS" : "WARNING",
            extra == 0 ? "لا توجد مسودات مكررة / No duplicate drafts found."
                : $"مجموعات مسودات متطابقة: {groups.Count} (مسودات زائدة: {extra}). للمراجعة فقط، لا حذف تلقائي",
            extra == 0 ? null : "راجع المسودات المتطابقة وألغِ الزائد يدوياً", groups.FirstOrDefault()?.First().Id.ToString()));
    }

    // No journal may be claimed by two documents, and no journal line may lack its journal.
    private async Task CheckDuplicateOrOrphanPostingAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, List<ReconciliationFindingDto> findings, CancellationToken token)
    {
        var orphans = await context.JournalLines.AsNoTracking()
            .Where(l => l.CompanyId == companyId && !context.JournalEntries.Any(j => j.Id == l.JournalId)).CountAsync(token).ConfigureAwait(false);
        // A journal id may be claimed by exactly ONE document of ANY type (sales, purchase, voucher, settlement batch, opening batch, inventory document),
        // counting both posting and reversal journals (the initial settlement batch legitimately shares its voucher's journal and is excluded); a shared id inside one type or across types is a duplicate.
        var claims = new List<Guid>();
        claims.AddRange(await context.SalesInvoices.AsNoTracking().Where(x => x.CompanyId == companyId && x.JournalEntryId != null).Select(x => x.JournalEntryId!.Value).ToListAsync(token).ConfigureAwait(false));
        claims.AddRange(await context.PurchaseInvoices.AsNoTracking().Where(x => x.CompanyId == companyId && x.JournalEntryId != null).Select(x => x.JournalEntryId!.Value).ToListAsync(token).ConfigureAwait(false));
        claims.AddRange(await context.Vouchers.AsNoTracking().Where(x => x.CompanyId == companyId && x.JournalEntryId != null).Select(x => x.JournalEntryId!.Value).ToListAsync(token).ConfigureAwait(false));
        claims.AddRange(await context.Vouchers.AsNoTracking().Where(x => x.CompanyId == companyId && x.ReversalJournalEntryId != null).Select(x => x.ReversalJournalEntryId!.Value).ToListAsync(token).ConfigureAwait(false));
        claims.AddRange(await context.SettlementBatches.AsNoTracking().Where(x => x.CompanyId == companyId && !x.IsInitial && x.JournalId != null).Select(x => x.JournalId!.Value).ToListAsync(token).ConfigureAwait(false));
        claims.AddRange(await context.SettlementBatches.AsNoTracking().Where(x => x.CompanyId == companyId && !x.IsInitial && x.ReversalJournalId != null).Select(x => x.ReversalJournalId!.Value).ToListAsync(token).ConfigureAwait(false));
        claims.AddRange(await context.OpeningBalanceBatches.AsNoTracking().Where(x => x.CompanyId == companyId && x.PostedJournalId != null).Select(x => x.PostedJournalId!.Value).ToListAsync(token).ConfigureAwait(false));
        claims.AddRange(await context.InventoryDocuments.AsNoTracking().Where(x => x.CompanyId == companyId && x.JournalId != null).Select(x => x.JournalId!.Value).ToListAsync(token).ConfigureAwait(false));
        var shared = claims.GroupBy(id => id).Count(g => g.Count() > 1);
        var bad = orphans + shared;
        findings.Add(Finding(companyId, fiscalYearId, branchId, "CHECK_14_DUPLICATE_OR_ORPHAN_POSTING", 0m, 0m, bad, bad == 0 ? "PASS" : "BLOCKING",
            bad == 0 ? "لا قيود مكررة لمستند ولا أسطر يتيمة / No duplicate-journal documents or orphan lines."
                : $"أسطر يتيمة: {orphans}؛ قيود يدّعيها أكثر من مستند (عبر كل الأنواع): {shared}",
            bad == 0 ? null : "حقّق في الترحيل المكرر؛ لا تحذف بيانات تاريخية تلقائياً"));
    }
}
