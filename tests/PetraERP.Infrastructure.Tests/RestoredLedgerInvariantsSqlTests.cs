using Microsoft.Data.SqlClient;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// Restore proves more than "the file restores": after a backup/restore of a ledger that was really posted and partly reversed,
/// the restored copy must be the same books — same counts, every posted journal balanced, posting sequence unique and the
/// grand total of debits equal to credits — and the restored copy must pass DBCC CHECKDB.
/// </summary>
public sealed class RestoredLedgerInvariantsSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private sealed record Books(long Journals, long Lines, decimal Debit, decimal Credit, long UnbalancedJournals, long DuplicateSequences);

    private static async Task<Books> ReadBooksAsync(string connection, string database)
    {
        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = database };
        var sql = new SqlConnection(builder.ConnectionString);
        await using (sql.ConfigureAwait(true))
        {
            await sql.OpenAsync(Cancellation).ConfigureAwait(true);
            using var command = sql.CreateCommand();
            command.CommandText = @"
                SELECT
                  (SELECT COUNT_BIG(*) FROM [ledger].[JournalEntries]),
                  (SELECT COUNT_BIG(*) FROM [ledger].[JournalLines]),
                  (SELECT ISNULL(SUM([BaseDebit]),0) FROM [ledger].[JournalLines]),
                  (SELECT ISNULL(SUM([BaseCredit]),0) FROM [ledger].[JournalLines]),
                  (SELECT COUNT_BIG(*) FROM (SELECT [JournalId] FROM [ledger].[JournalLines] GROUP BY [JournalId]
                       HAVING SUM([BaseDebit]) <> SUM([BaseCredit])) x),
                  (SELECT COUNT_BIG(*) FROM (SELECT [PostingSequence] FROM [ledger].[JournalEntries] WHERE [PostingSequence] IS NOT NULL
                       GROUP BY [CompanyId],[PostingSequence] HAVING COUNT(*) > 1) y)";
            var reader = await command.ExecuteReaderAsync(Cancellation).ConfigureAwait(true);
            await using (reader.ConfigureAwait(true))
            {
                Assert.True(await reader.ReadAsync(Cancellation).ConfigureAwait(true));
                return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetInt64(4), reader.GetInt64(5));
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task RestoredCopyHoldsTheSameBalancedBooksAsTheSource()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            PetraERP.Application.GeneralLedger.JournalDetailsDto? toReverse = null;
            for (var i = 0; i < 25; i++)
            {
                var posted = await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 1 + (i % 28)), "USD", true,
                    (cash.Id, 100m + i, 0m), (sales.Id, 0m, 100m + i)).ConfigureAwait(true);
                if (i == 7) toReverse = posted;
            }

            await s.ReverseAsync(s.CompanyA, s.BranchA1, toReverse!, new DateOnly(2026, 2, 3)).ConfigureAwait(true);
            // A draft is part of the books too and must survive the round trip.
            await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 3, 1), "USD", false, (cash.Id, 5m, 0m), (sales.Id, 0m, 5m)).ConfigureAwait(true);

            var source = await ReadBooksAsync(database.MasterConnectionString, database.DatabaseName).ConfigureAwait(true);
            Assert.Equal(source.Debit, source.Credit);
            Assert.Equal(0, source.UnbalancedJournals);
            Assert.True(source.Journals >= 27);

            var restored = $"PetraERP_RestoreInv_{Guid.NewGuid():N}";
            var master = new SqlConnection(database.MasterConnectionString);
            await using (master.ConfigureAwait(true))
            {
                await master.OpenAsync(Cancellation).ConfigureAwait(true);
                string backupDir, dataPath, logPath, logicalData, logicalLog;
                using (var command = master.CreateCommand())
                {
                    command.CommandText = @"SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(512)),
                        CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512)), CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(512))";
                    var reader = await command.ExecuteReaderAsync(Cancellation).ConfigureAwait(true);
                    await using (reader.ConfigureAwait(true))
                    {
                        Assert.True(await reader.ReadAsync(Cancellation).ConfigureAwait(true));
                        dataPath = reader.GetString(1);
                        logPath = reader.GetString(2);
                        backupDir = reader.IsDBNull(0) ? dataPath : reader.GetString(0);
                    }
                }

                using (var command = master.CreateCommand())
                {
                    command.CommandText = $"SELECT MAX(CASE WHEN type = 0 THEN name END), MAX(CASE WHEN type = 1 THEN name END) FROM [{database.DatabaseName}].sys.database_files";
                    var reader = await command.ExecuteReaderAsync(Cancellation).ConfigureAwait(true);
                    await using (reader.ConfigureAwait(true))
                    {
                        Assert.True(await reader.ReadAsync(Cancellation).ConfigureAwait(true));
                        logicalData = reader.GetString(0);
                        logicalLog = reader.GetString(1);
                    }
                }

                var backupFile = Path.Combine(backupDir, $"PetraERP_RestoreInv_{Guid.NewGuid():N}.bak");
                try
                {
                    async Task ExecAsync(string text, params (string Name, object Value)[] parameters)
                    {
                        using var command = master.CreateCommand();
                        command.CommandText = text;
                        command.CommandTimeout = 300;
                        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
                        await command.ExecuteNonQueryAsync(Cancellation).ConfigureAwait(true);
                    }

                    await ExecAsync($"BACKUP DATABASE [{database.DatabaseName}] TO DISK = @f WITH FORMAT, INIT, CHECKSUM", ("@f", backupFile)).ConfigureAwait(true);
                    await ExecAsync("RESTORE VERIFYONLY FROM DISK = @f WITH CHECKSUM", ("@f", backupFile)).ConfigureAwait(true);
                    await ExecAsync($"RESTORE DATABASE [{restored}] FROM DISK = @f WITH CHECKSUM, MOVE @d TO @m, MOVE @l TO @n",
                        ("@f", backupFile), ("@d", logicalData), ("@m", Path.Combine(dataPath, restored + ".mdf")),
                        ("@l", logicalLog), ("@n", Path.Combine(logPath, restored + "_log.ldf"))).ConfigureAwait(true);
                    await ExecAsync($"DBCC CHECKDB([{restored}]) WITH NO_INFOMSGS, ALL_ERRORMSGS").ConfigureAwait(true);

                    var copy = await ReadBooksAsync(database.MasterConnectionString, restored).ConfigureAwait(true);
                    Assert.Equal(source, copy);
                }
                finally
                {
                    try { await ExecAsync2(master, $"IF DB_ID(N'{restored}') IS NOT NULL BEGIN ALTER DATABASE [{restored}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{restored}]; END").ConfigureAwait(true); }
                    catch (SqlException) { /* best-effort cleanup of a throwaway database */ }
                    try { if (File.Exists(backupFile)) File.Delete(backupFile); }
                    catch (IOException) { /* the backup folder belongs to SQL Server; leaving the file is harmless */ }
                }
            }
        }
    }

    private static async Task ExecAsync2(SqlConnection connection, string text)
    {
        using var command = connection.CreateCommand();
        command.CommandText = text;
        await command.ExecuteNonQueryAsync(Cancellation).ConfigureAwait(true);
    }
}
