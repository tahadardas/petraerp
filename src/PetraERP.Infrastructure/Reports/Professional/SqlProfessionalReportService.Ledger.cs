using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports.Professional;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Reports.Professional;

public sealed partial class SqlProfessionalReportService
{
    private async Task<ProfessionalReportExecutionResponse> LedgerAsync(LedgerReportScope scope, ProfessionalReportRequest request,
        string baseCurrency, ProfessionalReportSource[] patternSources, CancellationToken cancellationToken)
    {
        var chart = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (chart.Count > ProfessionalReportCatalog.MaximumRows) throw TooLarge();
        var included = chart.Select(a => a.Id).ToHashSet();
        AccountRow? subject = null;
        if (request.AccountId is { } id)
        {
            subject = chart.SingleOrDefault(a => a.Id == id) ?? throw new ResourceNotFoundException("account");
            included = Descendants(chart, id);
        }
        if (request.AccountCategory is { } category)
            included.IntersectWith(chart.Where(a => a.AccountType == (byte)category).Select(a => a.Id));
        var ids = included.ToArray();
        var lines = context.JournalLines.AsNoTracking().Where(l => l.CompanyId == scope.CompanyId && l.Journal!.CompanyId == scope.CompanyId
            && l.Journal.BranchId == scope.BranchId && PostedStates.Contains(l.Journal.State) && ids.Contains(l.AccountId)
            && l.Journal.AccountingDate <= request.To);
        if (request.CurrencyCode is { } currency) lines = lines.Where(l => l.TransactionCurrencyCode == currency);
        if (request.CostCenterId is { } center)
        {
            var costCenters = await context.CostCenters.AsNoTracking().Where(c => c.CompanyId == scope.CompanyId).ToListAsync(cancellationToken).ConfigureAwait(false);
            var centerIds = CostCenterDescendants(costCenters, center);
            lines = lines.Where(l => l.CostCenterId.HasValue && centerIds.Contains(l.CostCenterId.Value));
        }
        var useTransaction = request.CurrencyBasis == ProfessionalCurrencyBasis.Transaction;
        var outputCurrency = useTransaction ? request.CurrencyCode! : baseCurrency;
        var period = lines.Where(l => l.Journal!.AccountingDate >= request.From);
        var filtered = FilterLines(period, scope, request, patternSources);
        // Display currency (Al-Ameen rule): every movement is valued at the register rate effective on ITS OWN date; a movement that
        // was natively in the display currency keeps its exact amount. Opening balances follow the same per-movement rule.
        var valuation = !useTransaction && request.DisplayCurrencyCode is { } wanted && wanted != baseCurrency
            ? await DisplayValuation.CreateAsync(context, scope.CompanyId, baseCurrency, wanted, [], request.To, cancellationToken).ConfigureAwait(false) : null;
        var displayCode = valuation?.Code;
        if (valuation is not null) outputCurrency = valuation.Code;
        (decimal Debit, decimal Credit) Valued(DateOnly date, string transactionCurrency, decimal transactionDebit, decimal transactionCredit, decimal baseDebit, decimal baseCredit) =>
            valuation is null ? (baseDebit, baseCredit) : valuation.Value(date, transactionCurrency, transactionDebit, transactionCredit, baseDebit, baseCredit);

        if (request.Kind == ProfessionalReportKind.CurrencyBalance)
            return await CurrencyBalanceAsync(request, chart, included, lines, filtered, baseCurrency, cancellationToken).ConfigureAwait(false);
        if (request.Kind == ProfessionalReportKind.TrialBalance)
            return await TrialAsync(request, chart, included, lines, filtered, outputCurrency, valuation, cancellationToken).ConfigureAwait(false);
        // Without a subject account the ledger covers every account: each account carries its own opening and running balance.
        Dictionary<Guid, decimal>? openingByAccount = null;
        decimal opening;
        if (displayCode is not null)
        {
            var before = await lines.Where(l => l.Journal!.AccountingDate < request.From)
                .GroupBy(l => new { l.AccountId, Date = l.Journal!.AccountingDate, l.TransactionCurrencyCode })
                .Select(g => new
                {
                    g.Key.AccountId, g.Key.Date, g.Key.TransactionCurrencyCode, TransactionDebit = g.Sum(l => l.TransactionDebit),
                    TransactionCredit = g.Sum(l => l.TransactionCredit), BaseDebit = g.Sum(l => l.BaseDebit), BaseCredit = g.Sum(l => l.BaseCredit),
                }).ToListAsync(cancellationToken).ConfigureAwait(false);
            openingByAccount = [];
            foreach (var group in before)
            {
                var value = Valued(group.Date, group.TransactionCurrencyCode, group.TransactionDebit, group.TransactionCredit, group.BaseDebit, group.BaseCredit);
                openingByAccount[group.AccountId] = openingByAccount.GetValueOrDefault(group.AccountId) + value.Debit - value.Credit;
            }
            opening = openingByAccount.Values.Sum();
        }
        else if (subject is null)
        {
            var grouped = await lines.Where(l => l.Journal!.AccountingDate < request.From).GroupBy(l => l.AccountId)
                .Select(g => new { g.Key, Net = g.Sum(l => useTransaction ? l.TransactionDebit - l.TransactionCredit : l.BaseDebit - l.BaseCredit) })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            openingByAccount = grouped.ToDictionary(g => g.Key, g => g.Net);
            opening = openingByAccount.Values.Sum();
        }
        else
            opening = await lines.Where(l => l.Journal!.AccountingDate < request.From)
                .SumAsync(l => useTransaction ? l.TransactionDebit - l.TransactionCredit : l.BaseDebit - l.BaseCredit, cancellationToken).ConfigureAwait(false);
        if (await period.LongCountAsync(cancellationToken).ConfigureAwait(false) > ProfessionalReportCatalog.MaximumRows) throw TooLarge();
        var matches = (await filtered.Select(l => l.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
        var rows = await period.OrderBy(l => l.Journal!.AccountingDate).ThenBy(l => l.Journal!.PostingSequence).ThenBy(l => l.Id)
            .Select(l => new
            {
                l.Id, l.JournalId, l.AccountId, Date = l.Journal!.AccountingDate, l.Journal.PostingSequence,
                Description = l.Journal.Description + " — " + l.Description, l.TransactionCurrencyCode, l.TransactionDebit, l.TransactionCredit,
                Debit = useTransaction ? l.TransactionDebit : l.BaseDebit, Credit = useTransaction ? l.TransactionCredit : l.BaseCredit,
                l.BaseDebit, l.BaseCredit,
                CostCenterCode = context.CostCenters.Where(c => c.Id == l.CostCenterId && c.CompanyId == scope.CompanyId).Select(c => c.Code).FirstOrDefault(),
            }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var links = await Sources(scope).Where(source => period.Any(l => l.JournalId == source.JournalId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        // A journal with several persisted document links degrades to a deterministic label; it never aborts the whole report.
        var ambiguousLinks = links.GroupBy(s => s.JournalId).Count(g => g.Select(s => new { s.Source, s.DocumentId }).Distinct().Count() > 1);
        var sources = links.GroupBy(s => s.JournalId).ToDictionary(g => g.Key, g => g
            .Select(s => new { s.Source, s.DocumentId, s.Number })
            .Distinct().OrderBy(s => (int)s.Source).ThenBy(s => s.DocumentId).First());
        var accounts = chart.ToDictionary(a => a.Id);
        if (subject is null)
            rows = rows.OrderBy(r => accounts[r.AccountId].Code, StringComparer.Ordinal).ThenBy(r => r.Date).ThenBy(r => r.PostingSequence).ThenBy(r => r.Id).ToList();
        // Opposite-account display is a list; a multi-line journal never invents a unique counterpart.
        string Codes(IEnumerable<Guid> accountIds) => string.Join(", ", accountIds.Distinct().Select(id => accounts.GetValueOrDefault(id)?.Code)
            .Where(code => code is not null).Order(StringComparer.Ordinal));
        var actual = opening;
        var display = request.BalancePresentation == ProfessionalBalancePresentation.Actual ? opening : 0m;
        // Counterpart lines are resolved only for the visible journals; the full period never materializes here.
        // Resolved before the row loop so each report row is created once with its final opposite-account text.
        var counterpartJournals = rows.Where(r => matches.Contains(r.Id)).Select(r => r.JournalId).Distinct().ToArray();
        var opposites = new Dictionary<Guid, (string Debit, string Credit)>();
        if (counterpartJournals.Length > 0)
        {
            var oppositeRows = await context.JournalLines.AsNoTracking().Where(l => l.CompanyId == scope.CompanyId
                    && counterpartJournals.Contains(l.JournalId))
                .Select(l => new { l.JournalId, l.AccountId, l.BaseDebit, l.BaseCredit }).ToListAsync(cancellationToken).ConfigureAwait(false);
            opposites = oppositeRows.GroupBy(l => l.JournalId).ToDictionary(g => g.Key,
                g => (Debit: Codes(g.Where(l => l.BaseDebit > 0m).Select(l => l.AccountId)), Credit: Codes(g.Where(l => l.BaseCredit > 0m).Select(l => l.AccountId))));
        }
        var amounts = rows.ToDictionary(r => r.Id, r => displayCode is null ? (r.Debit, r.Credit)
            : Valued(r.Date, r.TransactionCurrencyCode, r.TransactionDebit, r.TransactionCredit, r.BaseDebit, r.BaseCredit));
        var result = new List<ProfessionalReportRowDto>(matches.Count);
        Guid? currentAccount = null;
        foreach (var row in rows)
        {
            if (subject is null && currentAccount != row.AccountId)
            {
                currentAccount = row.AccountId;
                actual = openingByAccount!.GetValueOrDefault(row.AccountId);
                display = request.BalancePresentation == ProfessionalBalancePresentation.Actual ? actual : 0m;
            }
            var (rowDebit, rowCredit) = amounts[row.Id];
            actual += rowDebit - rowCredit;
            if (!matches.Contains(row.Id)) continue;
            display += rowDebit - rowCredit;
            sources.TryGetValue(row.JournalId, out var source);
            var account = accounts[row.AccountId];
            result.Add(new ProfessionalReportRowDto
            {
                RowId = row.Id, JournalId = row.JournalId, DocumentId = source?.DocumentId, Source = source?.Source ?? ProfessionalReportSource.UnclassifiedJournal,
                DocumentNumber = source?.Number, Date = row.Date, PostingSequence = row.PostingSequence, AccountId = account.Id,
                AccountCode = account.Code, AccountName = account.NameArabic, CostCenterCode = row.CostCenterCode,
                OppositeAccountCode = opposites.TryGetValue(row.JournalId, out var counterparts) ? (row.Debit > 0m ? counterparts.Credit : counterparts.Debit) : string.Empty,
                Description = row.Description, CurrencyCode = outputCurrency, Debit = rowDebit, Credit = rowCredit, ActualBalance = actual, DisplayBalance = display,
                Opening = 0m, Closing = 0m, OpeningDebit = 0m, OpeningCredit = 0m, ClosingDebit = 0m, ClosingCredit = 0m,
                TransactionCurrencyCode = row.TransactionCurrencyCode, TransactionDebit = row.TransactionDebit, TransactionCredit = row.TransactionCredit,
                GroupKey = string.Empty, IsAggregate = false, CanDrillThrough = true,
            });
        }
        var summary = Summary(opening, amounts.Values.Sum(a => a.Item1), amounts.Values.Sum(a => a.Item2), result.Sum(r => r.Debit), result.Sum(r => r.Credit),
            result.Count, false, request.BalancePresentation);
        var ledgerNotes = new List<string>
        {
            "الرصيد الفعلي يشمل كامل الحركات المرحّلة؛ رصيد العرض يشمل الحركات المطابقة للتصفية فقط.",
            "الرصيد السابق هو كل الحركات المرحّلة بأحد هذا الفرع قبل تاريخ بداية الفترة داخل قاعدة بيانات السنة الحالية؛ قيود الأرصدة الافتتاحية تظهر كحركة بتاريخ افتتاحها.",
            "البحث في بيان القيد وبيان السطر وفق Collation قاعدة البيانات؛ لا تطبيع للهمزة أو التشكيل.",
            "الرصيد الجاري الفعلي محسوب بترتيب التاريخ ورقم الترحيل ومعرف السطر حتى عند تغيير ترتيب العرض.",
        };
        if (displayCode is not null)
            ledgerNotes.Add($"العرض بعملة {displayCode}: كل حركة مقيَّمة بسعر النشرة بتاريخ حركتها (الحركات الأصلية بهذه العملة بمبلغها الدقيق)، والرصيد السابق بالقاعدة نفسها؛ عمود العملة الأصلية يبيّن عملة كل حركة. / Each movement is valued at the register rate of its own date; movements originally in {displayCode} keep their exact amount.");
        if (subject is null)
            ledgerNotes.Add("لم يُحدَّد حساب: يعرض الدفتر كل الحسابات مرتبة بالرمز، ولكل حساب رصيد سابق ورصيد جارٍ مستقل؛ الحسابات التي لا حركة لها في الفترة لا تظهر.");
        if (ambiguousLinks > 0)
            ledgerNotes.Add($"رُبطت {ambiguousLinks} من القيود بأكثر من مستند مصدري؛ يُعرض مصدر واحد الحتمي الترتيب ولا تُحذف أي حركة ولا تُعد مرتين.");
        return Empty(request, outputCurrency, subject?.Code ?? string.Empty, subject?.NameArabic ?? "كل الحسابات / All accounts", "DebitMinusCredit", OrderRows(request, result, subject is null), summary,
        ledgerNotes.ToArray());
    }

    private static HashSet<Guid> Descendants(IReadOnlyList<AccountRow> chart, Guid root)
    {
        var children = chart.Where(a => a.ParentId != null).ToLookup(a => a.ParentId!.Value);
        var result = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(root);
        while (pending.TryPop(out var id))
        {
            if (!result.Add(id)) throw new BusinessRuleException("reporting.chart_cycle", "The account hierarchy contains a cycle.", "accountId");
            foreach (var child in children[id]) pending.Push(child.Id);
        }
        return result;
    }

    private static HashSet<Guid> CostCenterDescendants(IReadOnlyList<CostCenterRow> centers, Guid root)
    {
        var children = centers.Where(c => c.ParentId != null).ToLookup(c => c.ParentId!.Value);
        var result = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(root);
        while (pending.TryPop(out var id))
        {
            if (!result.Add(id)) throw new BusinessRuleException("reporting.cost_center_cycle", "The cost center hierarchy contains a cycle.", "costCenterId");
            foreach (var child in children[id]) pending.Push(child.Id);
        }
        return result;
    }
}
