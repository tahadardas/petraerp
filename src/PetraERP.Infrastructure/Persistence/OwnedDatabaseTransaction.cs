using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace PetraERP.Infrastructure.Persistence;

/// <summary>Allows a journal operation to participate in an enclosing document transaction without committing it.</summary>
internal sealed class OwnedDatabaseTransaction(IDbContextTransaction? owned) : IAsyncDisposable
{
    internal static async Task<OwnedDatabaseTransaction> BeginAsync(DbContext context, IsolationLevel isolation,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is { } existing)
        {
            if (existing.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("Composed document operations require an enclosing SERIALIZABLE transaction.");
            return new(null);
        }
        return new(await context.Database.BeginTransactionAsync(isolation, cancellationToken).ConfigureAwait(false));
    }

    internal Task CommitAsync(CancellationToken cancellationToken) => owned?.CommitAsync(cancellationToken) ?? Task.CompletedTask;
    internal Task RollbackAsync(CancellationToken cancellationToken) => owned?.RollbackAsync(cancellationToken) ?? Task.CompletedTask;
    public ValueTask DisposeAsync() => owned?.DisposeAsync() ?? ValueTask.CompletedTask;
}
