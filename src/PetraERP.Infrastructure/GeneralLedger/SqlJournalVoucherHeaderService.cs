using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

public sealed class SqlJournalVoucherHeaderService(FinancialDbContext context, TimeProvider time) : IJournalVoucherHeaderService
{
    public async Task<JournalVoucherHeaderData?> GetAsync(JournalEntryScope scope, Guid journalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await RequireJournalAsync(scope, journalId, cancellationToken).ConfigureAwait(false);
        var row = await context.JournalVoucherHeaders.AsNoTracking()
            .FirstOrDefaultAsync(h => h.JournalId == journalId && h.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false);
        return row is null ? null : new JournalVoucherHeaderData(row.Kind, row.CashAccountId);
    }

    public async Task<JournalVoucherHeaderData> SetAsync(JournalEntryScope scope, Guid journalId, JournalVoucherHeaderData data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(data);
        if (!string.Equals(data.Kind, JournalVoucherHeaderData.Daily, StringComparison.Ordinal))
            throw new RequestValidationException("ledger.voucher_kind_invalid", "Unknown voucher kind.", "kind");
        var journal = await RequireJournalAsync(scope, journalId, cancellationToken).ConfigureAwait(false);
        if (journal.State is not (0 or 1))
            throw new BusinessRuleException("ledger.voucher_header_locked", "The voucher marker of a journal can only change while the journal is a draft.");
        var account = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId && a.Id == data.CashAccountId)
            .Select(a => new { a.IsActive, a.PostingAllowed }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (account is not { IsActive: true, PostingAllowed: true })
            throw new BusinessRuleException("ledger.voucher_cash_account_invalid", "The cash account must be an active posting account of this company.", "cashAccountId");

        var row = await context.JournalVoucherHeaders.FirstOrDefaultAsync(h => h.JournalId == journalId && h.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        if (row is null)
        {
            row = new JournalVoucherHeaderRow { JournalId = journalId, CompanyId = scope.CompanyId, CreatedAtUtc = now };
            context.JournalVoucherHeaders.Add(row);
        }

        row.Kind = data.Kind;
        row.CashAccountId = data.CashAccountId;
        row.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new JournalVoucherHeaderData(row.Kind, row.CashAccountId);
    }

    private async Task<JournalEntryRow> RequireJournalAsync(JournalEntryScope scope, Guid journalId, CancellationToken cancellationToken) =>
        await context.JournalEntries.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == journalId && j.CompanyId == scope.CompanyId && j.BranchId == scope.BranchId, cancellationToken).ConfigureAwait(false)
        ?? throw new ResourceNotFoundException("journal");
}
