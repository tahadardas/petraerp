using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PetraERP.Application.AlAmeenMigration.Reconciliation;
using PetraERP.Application.Common;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.AlAmeenMigration.Reconciliation;

internal sealed record TargetLedgerBalances(
    Guid CompanyId,
    Guid FiscalYearId,
    Guid BranchId,
    DateOnly CutoffDate,
    int JournalCount,
    IReadOnlyList<AccountBalance> Balances);

/// <summary>
/// Reads gross posted ledger activity in the requested year through the cutoff date.
/// The result is not a durable snapshot or an activation decision.
/// </summary>
internal sealed class TargetLedgerBalanceReader(IAccountingDbContextFactory contextFactory)
{
    public async Task<TargetLedgerBalances> ReadAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid branchId,
        DateOnly cutoffDate,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || fiscalYearId == Guid.Empty || branchId == Guid.Empty)
            throw new ArgumentException("Company, fiscal year and branch are required.");

        using var context = await contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false);
        using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        var result = await ReadWithinTransactionAsync(context, companyId, fiscalYearId, branchId, cutoffDate, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    internal static async Task<TargetLedgerBalances> ReadWithinTransactionAsync(AccountingDbContext context, Guid companyId, Guid fiscalYearId,
        Guid branchId, DateOnly cutoffDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("A serializable read transaction is required.");
        var year = await context.FiscalYears.AsNoTracking()
            .Where(y => y.CompanyId == companyId && y.Id == fiscalYearId)
            .Select(y => new { y.RangeStart, y.RangeEnd })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (year is null || cutoffDate < year.RangeStart || cutoffDate > year.RangeEnd)
            throw new BusinessRuleException("migration.invalid_cutoff", "تاريخ القطع خارج السنة المالية المحددة أو السنة غير موجودة.");

        if (!await context.Branches.AsNoTracking().AnyAsync(b => b.CompanyId == companyId && b.Id == branchId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("migration.invalid_branch", "الفرع غير موجود ضمن الشركة المحددة.");

        var posted = (byte)JournalEntryState.Posted;
        var reversed = (byte)JournalEntryState.Reversed;
        var journals = context.JournalEntries.AsNoTracking().Where(j =>
            j.CompanyId == companyId && j.BranchId == branchId && j.AccountingDate >= year.RangeStart && j.AccountingDate <= cutoffDate
            && (j.State == posted || j.State == reversed));

        // Do not silently drop malformed or legacy entries and then report a complete balance.
        var invalid = await journals.AnyAsync(j => j.PostingFiscalYearId != fiscalYearId
            || !context.JournalLines.Any(l => l.JournalId == j.Id)
            || context.JournalLines.Any(l => l.JournalId == j.Id && l.CompanyId != companyId), cancellationToken).ConfigureAwait(false);
        if (invalid)
            throw new BusinessRuleException("migration.invalid_ledger_evidence", "توجد قيود مرحلة بسنة غير صالحة أو بلا أسطر صالحة؛ تعذرت المطابقة.");

        var journalCount = await journals.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await (from j in journals
                          join line in context.JournalLines.AsNoTracking() on j.Id equals line.JournalId
                          where line.CompanyId == companyId
                          group line by new { j.BranchId, line.AccountId, line.TransactionCurrencyCode } into g
                          select new
                          {
                              g.Key.BranchId,
                              g.Key.AccountId,
                              g.Key.TransactionCurrencyCode,
                              Debit = g.Sum(l => l.TransactionDebit),
                              Credit = g.Sum(l => l.TransactionCredit),
                              BaseDebit = g.Sum(l => l.BaseDebit),
                              BaseCredit = g.Sum(l => l.BaseCredit),
                          }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var balances = rows.Select(row => new AccountBalance(
            new AccountBalanceKey(row.BranchId, row.AccountId, row.TransactionCurrencyCode),
            row.Debit, row.Credit, row.BaseDebit, row.BaseCredit)).ToList();
        return new TargetLedgerBalances(companyId, fiscalYearId, branchId, cutoffDate, journalCount, balances);
    }
}
