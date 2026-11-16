using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.GeneralLedger.Reconciliation;

public enum PriorYearStatus
{
    /// <summary>The registry proves that this fiscal year has no predecessor.</summary>
    NoPredecessor = 0,
    /// <summary>The predecessor's closing balances were read from its own database.</summary>
    Loaded = 1,
    /// <summary>A predecessor is expected but could not be read; continuity is UNVERIFIED.</summary>
    Unavailable = 2,
}

/// <summary>Closing position of the previous fiscal year: balance-sheet accounts as signed base (debit - credit) and the net income (credit-positive) to be carried to retained earnings.</summary>
public sealed record PriorYearClosing(Guid FiscalYearId, string YearCode, IReadOnlyDictionary<Guid, decimal> BalanceSheetNet, decimal NetIncome);

public sealed record PriorYearClosingResult(PriorYearStatus Status, PriorYearClosing? Closing, string? Reason);

/// <summary>Trusted cross-year read: the previous year's own database, resolved through the central fiscal-database registry.</summary>
public interface IPriorYearClosingSource
{
    Task<PriorYearClosingResult> LoadAsync(Guid companyId, Guid fiscalYearId, string? sourceYearCode, CancellationToken cancellationToken);
}

public sealed class FiscalDatabasePriorYearClosingSource(SystemDbContext systemContext, IAccountingDbContextFactory factory) : IPriorYearClosingSource
{
    public async Task<PriorYearClosingResult> LoadAsync(Guid companyId, Guid fiscalYearId, string? sourceYearCode, CancellationToken cancellationToken)
    {
        var current = await systemContext.FiscalDatabases.AsNoTracking()
            .FirstOrDefaultAsync(d => d.CompanyId == companyId && d.FiscalYearId == fiscalYearId, cancellationToken).ConfigureAwait(false);
        if (current is null) return new(PriorYearStatus.Unavailable, null, "السنة الحالية غير مسجلة في سجل قواعد البيانات المالية / current year is not in the fiscal-database registry");

        var hasCode = !string.IsNullOrWhiteSpace(sourceYearCode);
        Persistence.Registry.FiscalDatabaseRegistryRow? prior = null;
        if (hasCode)
            prior = await systemContext.FiscalDatabases.AsNoTracking().OrderBy(d => d.YearNumber)
                .FirstOrDefaultAsync(d => d.CompanyId == companyId && d.YearCode == sourceYearCode && d.FiscalYearId != fiscalYearId, cancellationToken).ConfigureAwait(false);
        else if (current.SourceFiscalYearId is { } sourceId)
            prior = await systemContext.FiscalDatabases.AsNoTracking()
                .FirstOrDefaultAsync(d => d.CompanyId == companyId && d.FiscalYearId == sourceId, cancellationToken).ConfigureAwait(false);
        if (prior is null)
            return hasCode || current.SourceFiscalYearId is not null
                ? new(PriorYearStatus.Unavailable, null, "السنة السابقة المشار إليها غير مسجلة / referenced previous year is not registered")
                : new(PriorYearStatus.NoPredecessor, null, null);

        try
        {
            using var source = await factory.CreateDbContextAsync(companyId, prior.FiscalYearId, cancellationToken).ConfigureAwait(false);
            var lines = await (
                from line in source.JournalLines.AsNoTracking()
                join entry in source.JournalEntries.AsNoTracking() on line.JournalId equals entry.Id
                join account in source.Accounts.AsNoTracking() on line.AccountId equals account.Id
                where entry.CompanyId == companyId && (entry.State == 4 || entry.State == 5)
                group new { line.BaseDebit, line.BaseCredit } by new { line.AccountId, account.AccountType } into g
                select new { g.Key.AccountId, g.Key.AccountType, Debit = g.Sum(x => x.BaseDebit), Credit = g.Sum(x => x.BaseCredit) })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var balanceSheet = lines.Where(l => l.AccountType is 1 or 2 or 3 && l.Debit - l.Credit != 0m)
                .ToDictionary(l => l.AccountId, l => l.Debit - l.Credit);
            var netIncome = lines.Where(l => l.AccountType == 4).Sum(l => l.Credit - l.Debit) - lines.Where(l => l.AccountType == 5).Sum(l => l.Debit - l.Credit);
            return new(PriorYearStatus.Loaded, new(prior.FiscalYearId, prior.YearCode ?? string.Empty, balanceSheet, netIncome), null);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            return new(PriorYearStatus.Unavailable, null, $"تعذرت قراءة قاعدة السنة السابقة ({ex.GetType().Name}) / previous-year database could not be read");
        }
    }
}
