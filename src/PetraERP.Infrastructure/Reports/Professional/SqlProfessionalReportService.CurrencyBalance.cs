using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Reports.Professional;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Reports.Professional;

public sealed partial class SqlProfessionalReportService
{
    private sealed record CurrencyBalanceAggregate(Guid AccountId, string Currency, decimal OpeningNative, decimal OpeningBase,
        decimal DebitNative, decimal CreditNative, decimal DebitBase, decimal CreditBase, long Count);

    private sealed record CurrencyBalanceShown(decimal DebitNative, decimal CreditNative, decimal DebitBase, decimal CreditBase);

    /// <summary>
    /// «ميزان العملات»: one row per account and transaction currency. Native amounts are the exact transaction amounts in that currency;
    /// the equivalent is the sum of each movement's own stored base amount (its historical rate), so the equivalent balance equals the trial balance.
    /// </summary>
    private static async Task<ProfessionalReportExecutionResponse> CurrencyBalanceAsync(ProfessionalReportRequest request, List<AccountRow> chart,
        HashSet<Guid> included, IQueryable<JournalLineRow> lines, IQueryable<JournalLineRow> filtered, string baseCurrency, CancellationToken cancellationToken)
    {
        var actual = (await lines.GroupBy(l => new { l.AccountId, l.TransactionCurrencyCode }).Select(g => new
        {
            g.Key.AccountId, Currency = g.Key.TransactionCurrencyCode,
            OpeningNative = g.Sum(l => l.Journal!.AccountingDate < request.From ? l.TransactionDebit - l.TransactionCredit : 0m),
            OpeningBase = g.Sum(l => l.Journal!.AccountingDate < request.From ? l.BaseDebit - l.BaseCredit : 0m),
            DebitNative = g.Sum(l => l.Journal!.AccountingDate >= request.From ? l.TransactionDebit : 0m),
            CreditNative = g.Sum(l => l.Journal!.AccountingDate >= request.From ? l.TransactionCredit : 0m),
            DebitBase = g.Sum(l => l.Journal!.AccountingDate >= request.From ? l.BaseDebit : 0m),
            CreditBase = g.Sum(l => l.Journal!.AccountingDate >= request.From ? l.BaseCredit : 0m),
            Count = g.LongCount(),
        }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(s => new CurrencyBalanceAggregate(s.AccountId, s.Currency, s.OpeningNative, s.OpeningBase, s.DebitNative, s.CreditNative, s.DebitBase, s.CreditBase, s.Count))
            .ToList();
        var shown = (await filtered.GroupBy(l => new { l.AccountId, l.TransactionCurrencyCode }).Select(g => new
        {
            g.Key.AccountId, Currency = g.Key.TransactionCurrencyCode, DebitNative = g.Sum(l => l.TransactionDebit), CreditNative = g.Sum(l => l.TransactionCredit),
            DebitBase = g.Sum(l => l.BaseDebit), CreditBase = g.Sum(l => l.BaseCredit),
        }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(s => (s.AccountId, s.Currency), s => new CurrencyBalanceShown(s.DebitNative, s.CreditNative, s.DebitBase, s.CreditBase));

        var accounts = chart.Where(a => included.Contains(a.Id)).ToDictionary(a => a.Id);
        var rows = new List<ProfessionalReportRowDto>();
        decimal openingBase = 0m, periodDebitBase = 0m, periodCreditBase = 0m, shownDebitBase = 0m, shownCreditBase = 0m;
        decimal openingDebit = 0m, openingCredit = 0m, closingDebit = 0m, closingCredit = 0m;
        foreach (var item in actual.Where(a => accounts.ContainsKey(a.AccountId))
                     .OrderBy(a => accounts[a.AccountId].Code, StringComparer.Ordinal).ThenBy(a => a.Currency, StringComparer.Ordinal))
        {
            var closingNative = item.OpeningNative + item.DebitNative - item.CreditNative;
            var closingBase = item.OpeningBase + item.DebitBase - item.CreditBase;
            if (request.OnlyNonZero && closingNative == 0m && closingBase == 0m) continue;
            var show = shown.GetValueOrDefault((item.AccountId, item.Currency)) ?? new CurrencyBalanceShown(0m, 0m, 0m, 0m);
            var account = accounts[item.AccountId];
            rows.Add(new ProfessionalReportRowDto
            {
                RowId = Guid.CreateVersion7(), AccountId = account.Id, AccountCode = account.Code, AccountName = account.NameArabic,
                Description = string.Empty, CurrencyCode = baseCurrency,
                // Equivalent (base) amounts at each movement's own rate; the native amounts are in TransactionCurrencyCode.
                Debit = show.DebitBase, Credit = show.CreditBase, ActualBalance = closingBase, DisplayBalance = closingBase,
                Opening = item.OpeningNative, Closing = closingNative,
                OpeningDebit = Math.Max(item.OpeningNative, 0m), OpeningCredit = Math.Max(-item.OpeningNative, 0m),
                ClosingDebit = Math.Max(closingNative, 0m), ClosingCredit = Math.Max(-closingNative, 0m),
                TransactionCurrencyCode = item.Currency, TransactionDebit = show.DebitNative, TransactionCredit = show.CreditNative,
                GroupKey = string.Empty, IsAggregate = false, CanDrillThrough = true,
            });
            openingBase += item.OpeningBase; periodDebitBase += item.DebitBase; periodCreditBase += item.CreditBase;
            shownDebitBase += show.DebitBase; shownCreditBase += show.CreditBase;
            openingDebit += Math.Max(item.OpeningBase, 0m); openingCredit += Math.Max(-item.OpeningBase, 0m);
            closingDebit += Math.Max(closingBase, 0m); closingCredit += Math.Max(-closingBase, 0m);
        }
        var summary = Summary(openingBase, periodDebitBase, periodCreditBase, shownDebitBase, shownCreditBase, rows.Count, false, request.BalancePresentation) with
        {
            OpeningDebit = openingDebit, OpeningCredit = openingCredit, ClosingDebit = closingDebit, ClosingCredit = closingCredit,
        };
        return Empty(request, baseCurrency, string.Empty, "ميزان العملات", "DebitMinusCredit", rows, summary,
            ["لكل حساب وعملة صف مستقل: المبالغ الأصلية بعملة الحركة نفسها، ومعادلها بالعملة المحلية محسوب بسعر كل حركة وقت تسجيلها.",
                "مجموع المعادل المحلي يطابق ميزان المراجعة لأنه مجموع المبالغ المحلية المسجلة للحركات نفسها.",
                "تصفية العملة تُبقي حركات تلك العملة فقط، والرصيد السابق يشمل كل الحركات قبل بداية الفترة."]);
    }
}
