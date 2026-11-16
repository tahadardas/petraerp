using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.AlAmeenMigration.Reconciliation;

internal sealed record TargetLedgerStockEvidence(TargetLedgerBalances Ledger, TargetStockBalances Stock);

/// <summary>Captures both target ledgers in one serializable read; evidence is not persisted or approved here.</summary>
internal sealed class TargetLedgerStockCapture(IAccountingDbContextFactory contextFactory)
{
    public async Task<TargetLedgerStockEvidence> ReadAsync(Guid companyId, Guid fiscalYearId, Guid branchId, DateOnly cutoffDate,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || fiscalYearId == Guid.Empty || branchId == Guid.Empty)
            throw new ArgumentException("Company, fiscal year and branch are required.");
        using var context = await contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false);
        using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        var ledger = await TargetLedgerBalanceReader.ReadWithinTransactionAsync(context, companyId, fiscalYearId, branchId, cutoffDate, cancellationToken).ConfigureAwait(false);
        var stock = await TargetStockBalanceReader.ReadWithinTransactionAsync(context, companyId, fiscalYearId, branchId, cutoffDate, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(ledger, stock);
    }
}
