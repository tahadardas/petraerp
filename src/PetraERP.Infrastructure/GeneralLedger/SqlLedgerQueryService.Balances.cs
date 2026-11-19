using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;

namespace PetraERP.Infrastructure.GeneralLedger;

public sealed partial class SqlLedgerQueryService
{
    public async Task<TrialBalanceDto> GetTrialBalanceAsync(LedgerReportScope scope, TrialBalanceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        var from = query.From;
        var to = query.To;
        if (query.PeriodId is { } periodId)
        {
            var period = await context.AccountingPeriods.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("accounting period");
            from = period.RangeStart;
            to = period.RangeEnd;
        }

        EnsureRange(from, to);
        await EnsureScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var start = from ?? DateOnly.MinValue;
        var options = query.Options ?? new TrialBalanceOptions();
        var displayRate = ValidateDisplay(options);
        var filtered = ApplyTrialBalanceFilters(ScopedLines(scope), scope.CompanyId, options)
            .Where(l => PostedStates.Contains(l.Journal!.State) && l.Journal.AccountingDate <= to);
        var lastMovement = options.ShowLastMovement
            ? await filtered.GroupBy(l => l.AccountId)
                .Select(g => new
                {
                    AccountId = g.Key,
                    Debit = g.Max(l => l.BaseDebit > 0m ? (DateOnly?)l.Journal!.AccountingDate : null),
                    Credit = g.Max(l => l.BaseCredit > 0m ? (DateOnly?)l.Journal!.AccountingDate : null),
                })
                .ToDictionaryAsync(m => m.AccountId, cancellationToken).ConfigureAwait(false)
            : [];
        var sums = await filtered
            .GroupBy(l => l.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Opening = g.Sum(l => l.Journal!.AccountingDate < start ? l.BaseDebit - l.BaseCredit : 0m),
                Debit = g.Sum(l => l.Journal!.AccountingDate >= start ? l.BaseDebit : 0m),
                Credit = g.Sum(l => l.Journal!.AccountingDate >= start ? l.BaseCredit : 0m),
            })
            .ToDictionaryAsync(s => s.AccountId, cancellationToken).ConfigureAwait(false);

        var chart = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (query.AccountType is { } type) chart = chart.Where(a => a.AccountType == (byte)type).ToList();
        if (options.AccountId is { } rootId)
        {
            var subtree = SubtreeIds(chart, rootId);
            if (subtree.Count == 0) throw new ResourceNotFoundException("account");
            chart = chart.Where(a => subtree.Contains(a.Id)).ToList();
        }

        var parents = chart.ToDictionary(a => a.Id, a => a.ParentId);
        var children = chart.Where(a => a.ParentId is not null).ToLookup(a => a.ParentId!.Value);

        // Roll leaf sums up the hierarchy in memory; reported values remain server-side and derived from posted lines.
        var totals = new Dictionary<Guid, (decimal Opening, decimal Debit, decimal Credit)>();
        (decimal Opening, decimal Debit, decimal Credit) Total(Guid id)
        {
            if (totals.TryGetValue(id, out var cached)) return cached;
            var own = sums.TryGetValue(id, out var s) ? (s.Opening, s.Debit, s.Credit) : (0m, 0m, 0m);
            foreach (var child in children[id])
            {
                var c = Total(child.Id);
                own = (own.Item1 + c.Opening, own.Item2 + c.Debit, own.Item3 + c.Credit);
            }

            totals[id] = own;
            return own;
        }

        // The newest movement of an account, or of any account below it, so a control account shows its latest child movement.
        DateOnly? LastOf(Guid id, Func<(DateOnly? Debit, DateOnly? Credit), DateOnly?> pick)
        {
            DateOnly? best = lastMovement.TryGetValue(id, out var own) ? pick((own.Debit, own.Credit)) : null;
            foreach (var child in children[id])
            {
                var value = LastOf(child.Id, pick);
                if (value is { } v && (best is null || v > best)) best = v;
            }

            return best;
        }

        var rows = new List<TrialBalanceRowDto>();
        void Visit(AccountRow account, int level)
        {
            var (opening, debit, credit) = Total(account.Id);
            var ending = opening + debit - credit;
            var visible = (query.IncludeZeroRows || opening != 0m || debit != 0m || credit != 0m)
                          && (query.MaximumLevel is null || level <= query.MaximumLevel)
                          && options.AccountScope switch
                          {
                              TrialBalanceAccountScope.PostingOnly => account.PostingAllowed,
                              TrialBalanceAccountScope.ParentsOnly => !account.PostingAllowed,
                              _ => true,
                          };
            if (visible)
            {
                rows.Add(new TrialBalanceRowDto
                {
                    AccountId = account.Id,
                    ParentId = account.ParentId,
                    Code = account.Code,
                    NameArabic = account.NameArabic,
                    NameEnglish = account.NameEnglish,
                    AccountType = (AccountTypeCode)account.AccountType,
                    PostingAllowed = account.PostingAllowed,
                    Level = level,
                    OpeningDebit = opening > 0 ? opening : 0m,
                    OpeningCredit = opening < 0 ? -opening : 0m,
                    PeriodDebit = debit,
                    PeriodCredit = credit,
                    EndingDebit = ending > 0 ? ending : 0m,
                    EndingCredit = ending < 0 ? -ending : 0m,
                    LastDebitDate = options.ShowLastMovement ? LastOf(account.Id, static m => m.Debit) : null,
                    LastCreditDate = options.ShowLastMovement ? LastOf(account.Id, static m => m.Credit) : null,
                });
            }

            foreach (var child in children[account.Id].OrderBy(c => c.Code, StringComparer.Ordinal))
            {
                Visit(child, level + 1);
            }
        }

        foreach (var root in chart.Where(a => a.ParentId is null || !parents.ContainsKey(a.ParentId.Value)).OrderBy(a => a.Code, StringComparer.Ordinal))
        {
            Visit(root, 0);
        }

        // Sum each account's own posted lines once, never the rolled-up display rows or only the current leaves.
        // Historical data may contain lines on an account that now has children.
        var includedIds = chart.Select(a => a.Id).ToHashSet();
        var reportTotals = TrialBalanceTotalsCalculator.Calculate(
            sums.Values.Select(s => new TrialBalanceAccountAmounts(s.AccountId, s.Opening, s.Debit, s.Credit)), includedIds);
        var (openingDebit, openingCredit, periodDebit, periodCredit, endingDebit, endingCredit) = reportTotals;

        var balanced = openingDebit == openingCredit && periodDebit == periodCredit && endingDebit == endingCredit;
        decimal Show(decimal value) => displayRate == 1m ? value : Math.Round(value / displayRate, 4, MidpointRounding.AwayFromZero);
        return new TrialBalanceDto
        {
            From = from,
            To = to,
            BaseCurrencyCode = await BaseCurrencyAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false),
            Rows = displayRate == 1m
                ? rows.AsReadOnly()
                : rows.Select(r => r with
                {
                    OpeningDebit = Show(r.OpeningDebit),
                    OpeningCredit = Show(r.OpeningCredit),
                    PeriodDebit = Show(r.PeriodDebit),
                    PeriodCredit = Show(r.PeriodCredit),
                    EndingDebit = Show(r.EndingDebit),
                    EndingCredit = Show(r.EndingCredit),
                }).ToList().AsReadOnly(),
            TotalOpeningDebit = Show(openingDebit),
            TotalOpeningCredit = Show(openingCredit),
            TotalPeriodDebit = Show(periodDebit),
            TotalPeriodCredit = Show(periodCredit),
            TotalEndingDebit = Show(endingDebit),
            TotalEndingCredit = Show(endingCredit),
            IsBalanced = balanced,
            DisplayCurrencyCode = string.IsNullOrWhiteSpace(options.DisplayCurrencyCode) ? null : options.DisplayCurrencyCode.Trim().ToUpperInvariant(),
            DisplayRate = displayRate,
        };
    }

    internal static decimal ValidateDisplay(TrialBalanceOptions options)
    {
        if (options.DisplayRate is null && string.IsNullOrWhiteSpace(options.DisplayCurrencyCode)) return 1m;
        var code = options.DisplayCurrencyCode?.Trim();
        if (code is not { Length: 3 } || !code.All(char.IsAsciiLetter))
            throw new BusinessRuleException("ledger.report_currency_invalid", "A three-letter report currency is required when specifying an exchange rate.", "options.displayCurrencyCode");
        if (options.DisplayRate is not > 0m or > 1_000_000_000m)
            throw new BusinessRuleException("ledger.report_rate_invalid", "The report exchange rate must be greater than zero.", "options.displayRate");
        return options.DisplayRate.Value;
    }

    private static HashSet<Guid> SubtreeIds(List<AccountRow> chart, Guid root)
    {
        var result = new HashSet<Guid>();
        if (chart.All(a => a.Id != root)) return result;
        var children = chart.Where(a => a.ParentId is not null).ToLookup(a => a.ParentId!.Value);
        var pending = new Stack<Guid>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!result.Add(id)) continue;
            foreach (var child in children[id]) pending.Push(child.Id);
        }

        return result;
    }

    /// <summary>Cost center, journal number range and document-source restrictions of a trial balance request.</summary>
    private IQueryable<JournalLineRow> ApplyTrialBalanceFilters(IQueryable<JournalLineRow> lines, Guid companyId, TrialBalanceOptions options)
    {
        if (options.CostCenterId is { } center) lines = lines.Where(l => l.CostCenterId == center);
        if (options.FromJournalNumber is { } fromNumber) lines = lines.Where(l => l.Journal!.PostingSequence >= fromNumber);
        if (options.ToJournalNumber is { } toNumber) lines = lines.Where(l => l.Journal!.PostingSequence <= toNumber);
        return ApplySourceFilter(lines, companyId, options.Sources);
    }

    /// <summary>Restricts journal lines to the chosen originating document kinds; null/empty/all-selected means no restriction.</summary>
    private IQueryable<JournalLineRow> ApplySourceFilter(IQueryable<JournalLineRow> lines, Guid companyId, IReadOnlyCollection<string>? sources)
    {
        if (sources is not { Count: > 0 } || TrialBalanceSources.All.All(sources.Contains)) return lines;
        var wantJournal = sources.Contains(TrialBalanceSources.Journal);
        var wantSales = sources.Contains(TrialBalanceSources.SalesInvoice);
        var wantPurchase = sources.Contains(TrialBalanceSources.PurchaseInvoice);
        var wantVoucher = sources.Contains(TrialBalanceSources.Voucher);
        var sales = context.SalesInvoices.AsNoTracking().Where(i => i.CompanyId == companyId && i.JournalEntryId != null).Select(i => i.JournalEntryId!.Value);
        var purchases = context.PurchaseInvoices.AsNoTracking().Where(i => i.CompanyId == companyId && i.JournalEntryId != null).Select(i => i.JournalEntryId!.Value);
        var vouchers = context.Vouchers.AsNoTracking().Where(v => v.CompanyId == companyId && v.JournalEntryId != null).Select(v => v.JournalEntryId!.Value);
        return lines.Where(l =>
            (wantSales && sales.Contains(l.JournalId))
            || (wantPurchase && purchases.Contains(l.JournalId))
            || (wantVoucher && vouchers.Contains(l.JournalId))
            || (wantJournal && !sales.Contains(l.JournalId) && !purchases.Contains(l.JournalId) && !vouchers.Contains(l.JournalId)));
    }

    private static IEnumerable<Guid> Descendants(List<AccountRow> chart, Guid id)
    {
        var children = chart.Where(a => a.ParentId is not null).ToLookup(a => a.ParentId!.Value);
        var pending = new Stack<Guid>(children[id].Select(c => c.Id));
        var seen = new HashSet<Guid>();
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            foreach (var child in children[current]) pending.Push(child.Id);
        }
    }

    private static int Depth(Dictionary<Guid, Guid?> parents, Guid id)
    {
        var depth = 0;
        var current = parents.GetValueOrDefault(id);
        var seen = new HashSet<Guid> { id };
        while (current is { } parent && seen.Add(parent))
        {
            depth++;
            current = parents.GetValueOrDefault(parent);
        }

        return depth;
    }
}
