using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

internal static class SecurityAdministrationLock
{
    /// <summary>
    /// Opens a transaction holding an exclusive application lock so concurrent administrators cannot
    /// interleave changes that would, for example, both remove the last remaining administrator.
    /// </summary>
    internal static async Task<IDbContextTransaction> BeginAsync(CentralDataContext context, CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = N'PetraERP.Security.Administration', @LockMode = N'Exclusive',
                    @LockOwner = N'Transaction', @LockTimeout = 15000;
                IF @result < 0 THROW 51010, N'The security administration lock could not be acquired.', 1;
                """, cancellationToken).ConfigureAwait(false);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
