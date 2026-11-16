using Microsoft.Data.SqlClient;

namespace PetraERP.Infrastructure.AlAmeenMigration;

public sealed record SchemaValidationResult(
    bool IsValid,
    IReadOnlyList<string> MissingTables,
    IReadOnlyList<string> PresentTables,
    IReadOnlyList<string> Warnings,
    string? Error);

public static class AlAmeenSourceSchemaValidator
{
    private static readonly string[] RequiredCoreTables =
    [
        "ac000",
        "cu000",
        "mt000",
        "st000",
        "my000",
        "ce000",
        "en000"
    ];

    private static readonly string[] OptionalTables =
    [
        "bt000",
        "bu000",
        "bi000",
        "ms000",
        "co000"
    ];

    public static async Task<SchemaValidationResult> ValidateSchemaAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var present = new List<string>();
        var missing = new List<string>();
        var warnings = new List<string>();

        try
        {
            var conn = new SqlConnection(connectionString);
            await using (conn.ConfigureAwait(false))
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                var cmd = conn.CreateCommand();
                await using (cmd.ConfigureAwait(false))
                {
                    cmd.CommandText = "SELECT LOWER(name) FROM sys.tables WHERE is_ms_shipped = 0";

                    var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    await using (reader.ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            existingTables.Add(reader.GetString(0));
                        }
                    }

                    foreach (var req in RequiredCoreTables)
                    {
                        if (existingTables.Contains(req))
                        {
                            present.Add(req);
                        }
                        else
                        {
                            missing.Add(req);
                        }
                    }

                    foreach (var opt in OptionalTables)
                    {
                        if (existingTables.Contains(opt))
                        {
                            present.Add(opt);
                        }
                        else
                        {
                            warnings.Add($"Optional table '{opt}' not found in source database.");
                        }
                    }

                    var isValid = missing.Count == 0;
                    return new SchemaValidationResult(isValid, missing, present, warnings, null);
                }
            }
        }
        catch (Exception ex)
        {
            return new SchemaValidationResult(false, RequiredCoreTables, present, warnings, ex.Message);
        }
    }

    /// <summary>
    /// Opens a read transaction that fixes one point in time for the whole capture. Snapshot is preferred because it
    /// does not block the live Al-Ameen database; databases without it fall back to serializable, which does block.
    /// The guarantee actually obtained is recorded so the evidence never claims more than the read delivered.
    /// </summary>
    public static async Task<ConsistentRead> BeginConsistentReadAsync(
        SqlConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("The source connection must be open before a consistent read begins.");

        // SQL Server rejects snapshot transactions when the database has not opted in, and it does so on the first
        // command rather than when the transaction begins, so the capability is checked first.
        var snapshotAllowed = await AllowsSnapshotIsolationAsync(connection, cancellationToken).ConfigureAwait(false);
        var isolation = snapshotAllowed ? System.Data.IsolationLevel.Snapshot : System.Data.IsolationLevel.Serializable;
        return new ConsistentRead(connection.BeginTransaction(isolation), snapshotAllowed ? SourceReadGuarantee.Snapshot : SourceReadGuarantee.Serializable);
    }

    private static async Task<bool> AllowsSnapshotIsolationAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = "SELECT CAST(CASE WHEN snapshot_isolation_state = 1 THEN 1 ELSE 0 END AS bit) FROM sys.databases WHERE database_id = DB_ID()";
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is not null && value is not DBNull && Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public sealed record ConsistentRead(SqlTransaction Transaction, SourceReadGuarantee Kind);
}
