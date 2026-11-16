using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Currencies;
using PetraERP.Domain.GeneralLedger;

namespace PetraERP.Infrastructure.Currencies;

public sealed partial class SqlFxRevaluationService
{
    private const int RateAuditMaximumLines = 50_000;

    public async Task<FxRateAuditResult> AuditRatesAsync(Guid companyId, Guid branchId, FxRateAuditCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.To < command.From)
            throw new RequestValidationException("fx.audit_range_invalid", "The end date cannot precede the start date.", "to");

        var company = await context.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("company.not_found", $"Company {companyId} not found.");
        if (!await context.Branches.AsNoTracking().AnyAsync(b => b.Id == branchId && b.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("fx.branch_invalid", $"Branch {branchId} does not belong to company {companyId}.");
        var baseCurrency = company.BaseCurrencyCode;
        var filterCode = string.IsNullOrWhiteSpace(command.CurrencyCode) ? null : command.CurrencyCode.Trim().ToUpperInvariant();

        // Posted, non-reversed originals only: a reversed original and its reversal entry cancel each other and would only add noise.
        var query =
            from line in context.JournalLines.AsNoTracking()
            join entry in context.JournalEntries.AsNoTracking() on line.JournalId equals entry.Id
            join account in context.Accounts.AsNoTracking() on line.AccountId equals account.Id
            where entry.CompanyId == companyId && entry.BranchId == branchId
                  && entry.State == (byte)JournalEntryState.Posted && entry.ReversesEntryId == null
                  && entry.AccountingDate >= command.From && entry.AccountingDate <= command.To
                  && line.TransactionCurrencyCode != baseCurrency
                  && (filterCode == null || line.TransactionCurrencyCode == filterCode)
            select new
            {
                entry.Id, entry.PostingSequence, entry.AccountingDate, line.AccountId, account.Code, account.NameArabic,
                line.Description, line.TransactionCurrencyCode, line.TransactionDebit, line.TransactionCredit,
                line.BaseDebit, line.BaseCredit, line.RateValue,
            };
        var lines = await query.OrderBy(l => l.AccountingDate).ThenBy(l => l.PostingSequence).Take(RateAuditMaximumLines + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (lines.Count > RateAuditMaximumLines)
            throw new BusinessRuleException("fx.audit_too_large", $"The audit range has more than {RateAuditMaximumLines} foreign lines; narrow the dates or the currency.");

        // The bulletin is append-only: for one date the effective rate is the latest effective-dated, latest-created entry on or before it
        // (the same rule documents use when they copy a rate). Resolved in memory per currency so the audit costs one query, not one per line.
        var currencies = lines.Select(l => l.TransactionCurrencyCode).Distinct().ToList();
        var register = (await context.ExchangeRates.AsNoTracking()
                .Where(r => r.CompanyId == companyId && r.BaseCurrencyCode == baseCurrency && currencies.Contains(r.CurrencyCode) && r.EffectiveDate <= command.To)
                .Select(r => new { r.CurrencyCode, r.EffectiveDate, r.CreatedAtUtc, r.Id, r.Rate })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(r => r.CurrencyCode)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.EffectiveDate).ThenByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id).ToList());

        var items = new List<FxRateAuditItemResult>();
        var missing = 0;
        foreach (var line in lines)
        {
            var effective = register.GetValueOrDefault(line.TransactionCurrencyCode)?.FirstOrDefault(r => r.EffectiveDate <= line.AccountingDate);
            if (effective is null) { missing++; continue; }
            if (effective.Rate == line.RateValue) continue;

            // Signed in the line's own direction (debit positive), so the difference reads as extra (+) or lesser (-) debit-side base value.
            var foreign = line.TransactionDebit - line.TransactionCredit;
            var booked = line.BaseDebit - line.BaseCredit;
            var bulletin = Math.Round(foreign * effective.Rate, 4, MidpointRounding.AwayFromZero);
            items.Add(new FxRateAuditItemResult(line.Id, line.PostingSequence, line.AccountingDate, line.AccountId, line.Code, line.NameArabic,
                line.Description, line.TransactionCurrencyCode, foreign, line.RateValue, effective.Rate, booked, bulletin, bulletin - booked));
        }
        return new FxRateAuditResult(command.From, command.To, baseCurrency, lines.Count, missing, items, items.Sum(i => i.Difference));
    }
}
