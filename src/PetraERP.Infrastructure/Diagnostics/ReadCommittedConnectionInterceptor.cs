using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PetraERP.Infrastructure.Diagnostics;

/// <summary>
/// SQL Server keeps a session's isolation level after a transaction ends, and a pooled connection hands that session to the
/// next user. Measured here: after one SERIALIZABLE transaction, every later connection from the pool reported SERIALIZABLE
/// (transaction_isolation_level = 4) even with no transaction open, so plain reads held key-range locks for the length of the
/// statement and closed deadlock cycles with writers (sales.InvoiceLines, graphs in .artifacts/gate-b). Each connection is
/// therefore reset to READ COMMITTED when EF opens it; code that needs SERIALIZABLE still asks for it explicitly with
/// BeginTransaction, which runs after the connection is opened.
/// </summary>
public sealed class ReadCommittedConnectionInterceptor : DbConnectionInterceptor
{
    private const string Reset = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED";

    public static ReadCommittedConnectionInterceptor Instance { get; } = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Reset;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = Reset;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
