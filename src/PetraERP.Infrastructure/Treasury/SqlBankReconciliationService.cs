using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Treasury;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Treasury;

/// <summary>
/// Bank reconciliation of one book account in base currency. Balances are debit-positive, so the statement balance is entered in the
/// same convention as the book (a bank asset account: money in the bank is positive). Reads are consistent within one query batch;
/// every write runs in a serializable transaction under the ManageBankReconciliations scope.
/// </summary>
public sealed partial class SqlBankReconciliationService(FinancialDbContext context) : IBankReconciliationService
{
    private static readonly byte[] PostedStates = [(byte)JournalEntryState.Posted, (byte)JournalEntryState.Reversed];

    public async Task<BankReconciliationDto> GetAsync(Guid companyId, Guid branchId, Guid reconciliationId, CancellationToken cancellationToken)
    {
        var row = await context.BankReconciliations.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == reconciliationId && r.CompanyId == companyId && r.BranchId == branchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("bank reconciliation");
        return await BuildAsync(row, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BankReconciliationSummaryDto>> ListAsync(Guid companyId, Guid branchId, Guid? accountId, CancellationToken cancellationToken)
    {
        var rows = context.BankReconciliations.AsNoTracking().Where(r => r.CompanyId == companyId && r.BranchId == branchId);
        if (accountId is { } account) rows = rows.Where(r => r.AccountId == account);
        var list = await rows.OrderByDescending(r => r.StatementDate).ThenByDescending(r => r.CreatedAtUtc).Take(200)
            .Select(r => new { r.Id, r.AccountId, Code = context.Accounts.Where(a => a.Id == r.AccountId).Select(a => a.Code).First(), r.StatementDate, r.StatementBalance, r.Status })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<BankReconciliationSummaryDto>(list.Count);
        foreach (var item in list)
        {
            var prior = await PriorClearedAsync(companyId, branchId, item.AccountId, item.Id, item.StatementDate, cancellationToken).ConfigureAwait(false);
            var cleared = await context.BankReconciliationLines.AsNoTracking().Where(l => l.ReconciliationId == item.Id)
                .SumAsync(l => (decimal?)l.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;
            result.Add(new(item.Id, item.AccountId, item.Code, item.StatementDate, item.StatementBalance, item.StatementBalance - prior - cleared, item.Status));
        }

        return result;
    }

    /// <summary>Balance cleared by completed reconciliations of this account other than the one asked about.</summary>
    private async Task<decimal> PriorClearedAsync(Guid companyId, Guid branchId, Guid accountId, Guid exceptId, DateOnly asOf, CancellationToken token) =>
        await (from l in context.BankReconciliationLines.AsNoTracking()
               join r in context.BankReconciliations.AsNoTracking() on l.ReconciliationId equals r.Id
               where r.CompanyId == companyId && r.BranchId == branchId && r.AccountId == accountId && r.Id != exceptId
                     && r.Status == BankReconciliationStatus.Completed && r.StatementDate <= asOf
               select (decimal?)l.Amount).SumAsync(token).ConfigureAwait(false) ?? 0m;

    private async Task<BankReconciliationDto> BuildAsync(BankReconciliationRow row, CancellationToken token)
    {
        var code = await context.Accounts.AsNoTracking().Where(a => a.Id == row.AccountId).Select(a => a.Code).SingleAsync(token).ConfigureAwait(false);
        var prior = await PriorClearedAsync(row.CompanyId, row.BranchId, row.AccountId, row.Id, row.StatementDate, token).ConfigureAwait(false);
        var clearedIds = await context.BankReconciliationLines.AsNoTracking().Where(l => l.ReconciliationId == row.Id)
            .Select(l => l.JournalLineId).ToListAsync(token).ConfigureAwait(false);
        var cleared = new HashSet<Guid>(clearedIds);

        // A draft shows everything still open up to the statement date plus what it already cleared; a completed one shows only its own lines.
        var lines = context.JournalLines.AsNoTracking().Where(l => l.CompanyId == row.CompanyId && l.AccountId == row.AccountId
            && l.Journal!.BranchId == row.BranchId && PostedStates.Contains(l.Journal.State) && l.Journal.AccountingDate <= row.StatementDate);
        var otherCleared = context.BankReconciliationLines.AsNoTracking().Where(c => c.ReconciliationId != row.Id).Select(c => c.JournalLineId);
        lines = row.Status == BankReconciliationStatus.Completed
            ? lines.Where(l => cleared.Contains(l.Id))
            : lines.Where(l => !otherCleared.Contains(l.Id));
        var rows = await lines.OrderBy(l => l.Journal!.AccountingDate).ThenBy(l => l.Journal!.PostingSequence ?? 0L).ThenBy(l => l.Id)
            .Select(l => new { l.Id, l.JournalId, l.Journal!.AccountingDate, l.Journal.PostingSequence, l.Description, Amount = l.BaseDebit - l.BaseCredit })
            .ToListAsync(token).ConfigureAwait(false);
        var dtoLines = rows.Select(l => new BankReconciliationLineDto(l.Id, l.JournalId, l.AccountingDate, l.PostingSequence, l.Description, l.Amount, cleared.Contains(l.Id))).ToList();
        var clearedTotal = await context.BankReconciliationLines.AsNoTracking().Where(l => l.ReconciliationId == row.Id)
            .SumAsync(l => (decimal?)l.Amount, token).ConfigureAwait(false) ?? 0m;
        var book = await context.JournalLines.AsNoTracking().Where(l => l.CompanyId == row.CompanyId && l.AccountId == row.AccountId
                && l.Journal!.BranchId == row.BranchId && PostedStates.Contains(l.Journal.State) && l.Journal.AccountingDate <= row.StatementDate)
            .SumAsync(l => (decimal?)(l.BaseDebit - l.BaseCredit), token).ConfigureAwait(false) ?? 0m;
        return new(row.Id, row.BranchId, row.AccountId, code, row.StatementDate, row.StatementBalance, prior, clearedTotal,
            row.StatementBalance - prior - clearedTotal, book, row.Status, Convert.ToBase64String(row.RowVersion), dtoLines);
    }
}
