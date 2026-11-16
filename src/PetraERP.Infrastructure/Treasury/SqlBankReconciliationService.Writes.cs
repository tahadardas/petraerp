using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Treasury;
using PetraERP.Infrastructure.CompanyOptions;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Treasury;

public sealed partial class SqlBankReconciliationService
{
    private const decimal MaxAmount = 999_999_999_999_999.9999m;

    public async Task<BankReconciliationDto> StartAsync(Guid companyId, Guid fiscalYearId, Guid branchId, Guid actorId, Guid accountId,
        DateOnly statementDate, decimal statementBalance, CancellationToken cancellationToken)
    {
        if (Math.Abs(statementBalance) > MaxAmount || decimal.Round(statementBalance, 4) != statementBalance)
            throw new BusinessRuleException("bank_reconciliation.balance_invalid", "Use a statement balance with at most four decimal places.");
        Bind(companyId, fiscalYearId, actorId, branchId);
        var tx = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using var lifetime = tx.ConfigureAwait(false);
        if (!await context.Accounts.AnyAsync(a => a.Id == accountId && a.CompanyId == companyId && a.IsActive && a.PostingAllowed, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("bank_reconciliation.account_invalid", "Choose an active posting account of this company.");
        if (!await context.Branches.AnyAsync(b => b.Id == branchId && b.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("branch");
        if (await context.BankReconciliations.AnyAsync(r => r.CompanyId == companyId && r.BranchId == branchId && r.AccountId == accountId
                && r.Status == BankReconciliationStatus.Draft, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("bank_reconciliation.draft_exists", "Finish or discard the open reconciliation of this account first.");
        var lastDone = await context.BankReconciliations.Where(r => r.CompanyId == companyId && r.BranchId == branchId && r.AccountId == accountId
                && r.Status == BankReconciliationStatus.Completed).MaxAsync(r => (DateOnly?)r.StatementDate, cancellationToken).ConfigureAwait(false);
        if (lastDone is { } done && statementDate <= done)
            throw new BusinessRuleException("bank_reconciliation.date_not_after_last", "The statement date must be after the last completed reconciliation.");

        var row = new BankReconciliationRow
        {
            Id = Guid.CreateVersion7(), CompanyId = companyId, FiscalYearId = fiscalYearId, BranchId = branchId, AccountId = accountId,
            StatementDate = statementDate, StatementBalance = statementBalance, CreatedByUserId = actorId,
        };
        context.Add(row);
        Audit(row, actorId, "BankReconciliationStarted", null, new { row.AccountId, row.StatementDate, row.StatementBalance });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await BuildAsync(row, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BankReconciliationDto> SetClearedAsync(Guid companyId, Guid fiscalYearId, Guid branchId, Guid actorId, Guid reconciliationId,
        IReadOnlyCollection<Guid> journalLineIds, bool cleared, string expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journalLineIds);
        var ids = journalLineIds.Distinct().ToArray();
        if (ids.Length is 0 or > 500) throw new BusinessRuleException("bank_reconciliation.lines_invalid", "Send between 1 and 500 line identifiers.");
        Bind(companyId, fiscalYearId, actorId, branchId);
        var tx = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using var lifetime = tx.ConfigureAwait(false);
        var row = await LoadDraftAsync(companyId, branchId, reconciliationId, expectedVersion, cancellationToken).ConfigureAwait(false);
        if (cleared)
        {
            var eligible = await context.JournalLines.AsNoTracking().Where(l => ids.Contains(l.Id) && l.CompanyId == companyId && l.AccountId == row.AccountId
                    && l.Journal!.BranchId == branchId && PostedStates.Contains(l.Journal.State) && l.Journal.AccountingDate <= row.StatementDate)
                .Select(l => new { l.Id, Amount = l.BaseDebit - l.BaseCredit }).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (eligible.Count != ids.Length)
                throw new BusinessRuleException("bank_reconciliation.line_not_eligible", "Every line must be a posted line of this account on or before the statement date.");
            var taken = await context.BankReconciliationLines.AsNoTracking().AnyAsync(l => ids.Contains(l.JournalLineId), cancellationToken).ConfigureAwait(false);
            if (taken) throw new BusinessRuleException("bank_reconciliation.line_already_cleared", "A selected line is already cleared by a reconciliation.");
            foreach (var line in eligible)
                context.Add(new BankReconciliationLineRow { ReconciliationId = row.Id, JournalLineId = line.Id, Amount = line.Amount });
        }
        else
        {
            var owned = await context.BankReconciliationLines.Where(l => l.ReconciliationId == row.Id && ids.Contains(l.JournalLineId)).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (owned.Count != ids.Length) throw new BusinessRuleException("bank_reconciliation.line_not_cleared", "A selected line is not cleared in this reconciliation.");
            context.RemoveRange(owned);
        }

        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Audit(row, actorId, cleared ? "BankReconciliationLinesCleared" : "BankReconciliationLinesUncleared", null, new { Count = ids.Length });
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        context.ChangeTracker.Clear();
        return await GetAsync(companyId, branchId, reconciliationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BankReconciliationDto> CompleteAsync(Guid companyId, Guid fiscalYearId, Guid branchId, Guid actorId, Guid reconciliationId,
        string expectedVersion, CancellationToken cancellationToken)
    {
        Bind(companyId, fiscalYearId, actorId, branchId);
        var tx = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using var lifetime = tx.ConfigureAwait(false);
        var row = await LoadDraftAsync(companyId, branchId, reconciliationId, expectedVersion, cancellationToken).ConfigureAwait(false);
        var state = await BuildAsync(row, cancellationToken).ConfigureAwait(false);
        if (state.Difference != 0m)
            throw new BusinessRuleException("bank_reconciliation.difference_not_zero", "The statement and the cleared book balance still differ; clear the remaining lines first.");
        row.Status = BankReconciliationStatus.Completed;
        row.CompletedByUserId = actorId;
        row.CompletedAtUtc = DateTimeOffset.UtcNow;
        row.UpdatedAtUtc = row.CompletedAtUtc.Value;
        Audit(row, actorId, "BankReconciliationCompleted", new { Status = BankReconciliationStatus.Draft }, new { row.Status, state.ClearedInThisReconciliation, row.StatementBalance });
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        context.ChangeTracker.Clear();
        return await GetAsync(companyId, branchId, reconciliationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DiscardAsync(Guid companyId, Guid fiscalYearId, Guid branchId, Guid actorId, Guid reconciliationId, string expectedVersion,
        CancellationToken cancellationToken)
    {
        Bind(companyId, fiscalYearId, actorId, branchId);
        var tx = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using var lifetime = tx.ConfigureAwait(false);
        var row = await LoadDraftAsync(companyId, branchId, reconciliationId, expectedVersion, cancellationToken).ConfigureAwait(false);
        context.RemoveRange(await context.BankReconciliationLines.Where(l => l.ReconciliationId == row.Id).ToListAsync(cancellationToken).ConfigureAwait(false));
        Audit(row, actorId, "BankReconciliationDiscarded", new { row.AccountId, row.StatementDate }, new { Discarded = true });
        context.Remove(row);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<BankReconciliationRow> LoadDraftAsync(Guid companyId, Guid branchId, Guid id, string expectedVersion, CancellationToken token)
    {
        var row = await context.BankReconciliations.SingleOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId && r.BranchId == branchId, token).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("bank reconciliation");
        if (row.Status != BankReconciliationStatus.Draft)
            throw new BusinessRuleException("bank_reconciliation.completed_locked", "A completed reconciliation cannot be changed.");
        if (Convert.ToBase64String(row.RowVersion) != expectedVersion)
            throw new ConcurrencyConflictException("bank reconciliation");
        return row;
    }

    private async Task SaveAsync(CancellationToken token)
    {
        try
        {
            await context.SaveChangesAsync(token).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("bank reconciliation");
        }
        catch (DbUpdateException)
        {
            // Unique key on the journal line: another reconciliation cleared it between our check and our write.
            throw new BusinessRuleException("bank_reconciliation.line_already_cleared", "A selected line was just cleared by another reconciliation.");
        }
    }

    private void Bind(Guid companyId, Guid fiscalYearId, Guid actorId, Guid branchId)
    {
        if (context is AccountingDbContext accounting && accounting.BoundOperation is null)
            accounting.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, actorId, AccountingOperation.ManageBankReconciliations, branchId));
    }

    private void Audit(BankReconciliationRow row, Guid actor, string type, object? before, object after) => context.Add(new CompanyPolicyAuditRow
    {
        Id = Guid.CreateVersion7(), CompanyId = row.CompanyId, FiscalYearId = row.FiscalYearId, EntityId = row.Id, ActorUserId = actor, EventType = type,
        Reason = "bank reconciliation", BeforeJson = before is null ? null : JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(after),
        OccurredAtUtc = DateTimeOffset.UtcNow,
    });
}
