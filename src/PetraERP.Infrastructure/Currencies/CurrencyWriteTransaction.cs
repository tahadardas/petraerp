using System.Data;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Currencies;

/// <summary>Hold currency resolution and its persisted document under the same company lock as base-currency changes.</summary>
internal static class CurrencyWriteTransaction
{
    internal static async Task<OwnedDatabaseTransaction> BeginAsync(FinancialDbContext context, Guid companyId, CancellationToken cancellationToken)
    {
        var transaction = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            await InventoryWriteScope.LockCompanyAsync(context, companyId, cancellationToken).ConfigureAwait(false);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
