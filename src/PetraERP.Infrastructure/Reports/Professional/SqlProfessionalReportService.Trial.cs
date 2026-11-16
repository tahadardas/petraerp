using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Reports.Professional;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Reports.Professional;

public sealed partial class SqlProfessionalReportService
{
    private sealed record TrialAggregate(Guid Id, decimal Opening, decimal Debit, decimal Credit, long Count);
    private sealed record TrialShown(decimal Debit, decimal Credit);

    private sealed record TrialTotals(decimal Opening, decimal Debit, decimal Credit, decimal FilteredDebit, decimal FilteredCredit, long Count)
    {
        public static TrialTotals Zero { get; } = new(0m, 0m, 0m, 0m, 0m, 0);
        public decimal Closing => Opening + Debit - Credit;
        public TrialTotals Add(TrialTotals other) => new(Opening + other.Opening, Debit + other.Debit, Credit + other.Credit,
            FilteredDebit + other.FilteredDebit, FilteredCredit + other.FilteredCredit, Count + other.Count);
    }

    private static async Task<ProfessionalReportExecutionResponse> TrialAsync(ProfessionalReportRequest request, List<AccountRow> chart,
        HashSet<Guid> included, IQueryable<JournalLineRow> lines, IQueryable<JournalLineRow> filtered, string currency, DisplayValuation? valuation, CancellationToken cancellationToken)
    {
        var transaction = request.CurrencyBasis == ProfessionalCurrencyBasis.Transaction;
        List<TrialAggregate> actual;
        Dictionary<Guid, TrialShown> displayed;
        if (valuation is null)
        {
            actual = (await lines.GroupBy(l => l.AccountId).Select(g => new
            {
                Id = g.Key, Opening = g.Sum(l => l.Journal!.AccountingDate < request.From
                    ? transaction ? l.TransactionDebit - l.TransactionCredit : l.BaseDebit - l.BaseCredit : 0m),
                Debit = g.Sum(l => l.Journal!.AccountingDate >= request.From ? transaction ? l.TransactionDebit : l.BaseDebit : 0m),
                Credit = g.Sum(l => l.Journal!.AccountingDate >= request.From ? transaction ? l.TransactionCredit : l.BaseCredit : 0m), Count = g.LongCount(),
            }).ToListAsync(cancellationToken).ConfigureAwait(false)).Select(s => new TrialAggregate(s.Id, s.Opening, s.Debit, s.Credit, s.Count)).ToList();
            displayed = (await filtered.GroupBy(l => l.AccountId).Select(g => new
            {
                Id = g.Key, Debit = g.Sum(l => transaction ? l.TransactionDebit : l.BaseDebit), Credit = g.Sum(l => transaction ? l.TransactionCredit : l.BaseCredit),
            }).ToListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.Id, s => new TrialShown(s.Debit, s.Credit));
        }
        else
        {
            // Display currency: each movement is valued at the register rate of its own date, so lines are grouped by account, day and currency.
            var perDay = await lines.GroupBy(l => new { l.AccountId, Date = l.Journal!.AccountingDate, l.TransactionCurrencyCode }).Select(g => new
            {
                g.Key.AccountId, g.Key.Date, g.Key.TransactionCurrencyCode, TransactionDebit = g.Sum(l => l.TransactionDebit),
                TransactionCredit = g.Sum(l => l.TransactionCredit), BaseDebit = g.Sum(l => l.BaseDebit), BaseCredit = g.Sum(l => l.BaseCredit), Count = g.LongCount(),
            }).ToListAsync(cancellationToken).ConfigureAwait(false);
            actual = perDay.GroupBy(r => r.AccountId).Select(g =>
            {
                decimal opening = 0m, debit = 0m, credit = 0m;
                foreach (var r in g)
                {
                    var v = valuation.Value(r.Date, r.TransactionCurrencyCode, r.TransactionDebit, r.TransactionCredit, r.BaseDebit, r.BaseCredit);
                    if (r.Date < request.From) opening += v.Debit - v.Credit; else { debit += v.Debit; credit += v.Credit; }
                }
                return new TrialAggregate(g.Key, opening, debit, credit, g.Sum(r => r.Count));
            }).ToList();
            var shownPerDay = await filtered.GroupBy(l => new { l.AccountId, Date = l.Journal!.AccountingDate, l.TransactionCurrencyCode }).Select(g => new
            {
                g.Key.AccountId, g.Key.Date, g.Key.TransactionCurrencyCode, TransactionDebit = g.Sum(l => l.TransactionDebit),
                TransactionCredit = g.Sum(l => l.TransactionCredit), BaseDebit = g.Sum(l => l.BaseDebit), BaseCredit = g.Sum(l => l.BaseCredit),
            }).ToListAsync(cancellationToken).ConfigureAwait(false);
            displayed = shownPerDay.GroupBy(r => r.AccountId).ToDictionary(g => g.Key, g =>
            {
                decimal debit = 0m, credit = 0m;
                foreach (var r in g)
                {
                    var v = valuation.Value(r.Date, r.TransactionCurrencyCode, r.TransactionDebit, r.TransactionCredit, r.BaseDebit, r.BaseCredit);
                    debit += v.Debit; credit += v.Credit;
                }
                return new TrialShown(debit, credit);
            });
        }        var own = actual.ToDictionary(s => s.Id, s => new TrialTotals(s.Opening, s.Debit, s.Credit,
            displayed.GetValueOrDefault(s.Id)?.Debit ?? 0m, displayed.GetValueOrDefault(s.Id)?.Credit ?? 0m, s.Count));
        var children = chart.Where(a => a.ParentId != null && included.Contains(a.Id)).ToLookup(a => a.ParentId!.Value);
        var parents = chart.ToDictionary(a => a.Id, a => a.ParentId);
        var rolled = new Dictionary<Guid, TrialTotals>();
        var visiting = new HashSet<Guid>();
        TrialTotals Roll(Guid id, int depth)
        {
            if (rolled.TryGetValue(id, out var cached)) return cached;
            if (depth > 100 || !visiting.Add(id)) throw new BusinessRuleException("reporting.chart_cycle", "Invalid account hierarchy.", "accountId");
            var total = own.GetValueOrDefault(id) ?? TrialTotals.Zero;
            foreach (var child in children[id]) total = total.Add(Roll(child.Id, depth + 1));
            visiting.Remove(id);
            rolled.Add(id, total);
            return total;
        }
        int Level(Guid id)
        {
            var seen = new HashSet<Guid> { id };
            var level = 1;
            while (parents.GetValueOrDefault(id) is { } parent)
            {
                if (!seen.Add(parent) || ++level > 100) throw new BusinessRuleException("reporting.chart_cycle", "Invalid account hierarchy.", "accountId");
                id = parent;
            }
            return level;
        }
        var rows = new List<ProfessionalReportRowDto>();
        ProfessionalReportRowDto Row(AccountRow account, TrialTotals total, bool aggregate) => new()
        {
            RowId = aggregate ? Guid.CreateVersion7() : account.Id, AccountId = account.Id, AccountCode = account.Code, AccountName = account.NameArabic,
            Description = string.Empty, CurrencyCode = currency, Debit = total.FilteredDebit, Credit = total.FilteredCredit,
            ActualBalance = total.Closing, DisplayBalance = total.Opening + total.FilteredDebit - total.FilteredCredit,
            Opening = total.Opening, Closing = total.Closing,
            OpeningDebit = Math.Max(total.Opening, 0m), OpeningCredit = Math.Max(-total.Opening, 0m),
            ClosingDebit = Math.Max(total.Closing, 0m), ClosingCredit = Math.Max(-total.Closing, 0m),
            TransactionCurrencyCode = currency, TransactionDebit = 0m, TransactionCredit = 0m, GroupKey = string.Empty,
            IsAggregate = aggregate, CanDrillThrough = !aggregate,
        };
        foreach (var account in chart.Where(a => included.Contains(a.Id)).OrderBy(a => a.Code, StringComparer.Ordinal))
        {
            if (request.AccountLevel is { } level && Level(account.Id) > level) continue;
            var hasChildren = children.Contains(account.Id);
            // Posting accounts report their own movements as authoritative rows; parents add a rolled presentation row only.
            if (account.PostingAllowed && request.IncludePostingAccounts)
            {
                var ownTotal = own.GetValueOrDefault(account.Id) ?? TrialTotals.Zero;
                if ((request.IncludeEmptyAccounts || ownTotal.Count != 0) && (!request.OnlyNonZero || ownTotal.Closing != 0m))
                    rows.Add(Row(account, ownTotal, aggregate: false));
            }
            if (hasChildren && request.IncludeParents)
            {
                var total = Roll(account.Id, 1);
                if ((request.IncludeEmptyAccounts || total.Count != 0) && (!request.OnlyNonZero || total.Closing != 0m))
                    rows.Add(Row(account, total, aggregate: true));
            }
        }
        // Sum own account balances once; parent presentation rows must never multiply financial totals.
        var all = own.Values.ToArray();
        var summary = Summary(all.Sum(t => t.Opening), all.Sum(t => t.Debit), all.Sum(t => t.Credit), all.Sum(t => t.FilteredDebit),
            all.Sum(t => t.FilteredCredit), rows.Count, false, request.BalancePresentation) with
        {
            OpeningDebit = all.Sum(t => Math.Max(t.Opening, 0m)), OpeningCredit = all.Sum(t => Math.Max(-t.Opening, 0m)),
            ClosingDebit = all.Sum(t => Math.Max(t.Closing, 0m)), ClosingCredit = all.Sum(t => Math.Max(-t.Closing, 0m)),
        };
        return Empty(request, currency, string.Empty, "ميزان المراجعة", "DebitMinusCredit", rows, summary,
            ["الإجماليات تجمع أرصدة الحسابات الأصلية مرة واحدة؛ صفوف الحسابات الرئيسية مجاميع شجرية للعرض فقط.",
                "المدين والدائن يعرضان الحركات المطابقة؛ الرصيد السابق والنهائي الفعليان لا يتغيران بسبب تصفية البيان أو المصدر.",
                "عند اختيار عملة الحركة تجمع عملة واحدة فقط؛ في الأساس تستخدم مبالغ الأساس المثبتة بالقيد."]);
    }
}
