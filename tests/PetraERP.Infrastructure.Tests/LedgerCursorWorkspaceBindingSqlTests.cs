using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// A restored copy has the same company, branch and account ids as its source, exactly like two fiscal years of one company.
/// A cursor issued on one workspace carries that workspace's running balance, so it must be refused on the other even though every
/// id matches. Without the workspace binding the copy would be paged from the source's balances.
/// </summary>
public sealed class LedgerCursorWorkspaceBindingSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private sealed class Signer : ILedgerCursorSigner
    {
        private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

        public string Protect(ReadOnlySpan<byte> payload) =>
            Convert.ToBase64String(payload) + "." + Convert.ToBase64String(HMACSHA256.HashData(key, payload));

        public bool TryUnprotect(string token, out byte[] payload)
        {
            payload = [];
            var parts = token.Split('.');
            if (parts.Length != 2) return false;
            var body = Convert.FromBase64String(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, body), Convert.FromBase64String(parts[1]))) return false;
            payload = body;
            return true;
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CursorIssuedOnOneWorkspaceIsRefusedOnACopyWithIdenticalIds()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            for (var i = 0; i < 8; i++)
                await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 1 + i), "USD", true, (cash.Id, 100m + i, 0m), (sales.Id, 0m, 100m + i)).ConfigureAwait(true);

            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var signer = new Signer();
            string cursor;
            using (var context = database.Context())
            {
                var first = await new SqlLedgerQueryService(context, signer).GetAccountLedgerAsync(scope,
                    new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(1, 3)), Cancellation).ConfigureAwait(true);
                cursor = first.NextCursor!;
                Assert.NotNull(cursor);
                // Sanity: on its own workspace the cursor works.
                var second = await new SqlLedgerQueryService(context, signer).GetAccountLedgerAsync(scope,
                    new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(2, 3), cursor), Cancellation).ConfigureAwait(true);
                Assert.Equal(3, second.Lines.Count);
            }

            var copy = $"PetraERP_CursorCopy_{Guid.NewGuid():N}";
            var master = new SqlConnection(database.MasterConnectionString);
            await using (master.ConfigureAwait(true))
            {
                await master.OpenAsync(Cancellation).ConfigureAwait(true);
                async Task<string> ScalarTextAsync(string sql)
                {
                    using var command = master.CreateCommand();
                    command.CommandText = sql;
                    return Convert.ToString(await command.ExecuteScalarAsync(Cancellation).ConfigureAwait(true), System.Globalization.CultureInfo.InvariantCulture)!;
                }

                async Task ExecAsync(string sql, params (string Name, object Value)[] parameters)
                {
                    using var command = master.CreateCommand();
                    command.CommandText = sql;
                    command.CommandTimeout = 300;
                    foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
                    await command.ExecuteNonQueryAsync(Cancellation).ConfigureAwait(true);
                }

                var dataPath = await ScalarTextAsync("SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512))").ConfigureAwait(true);
                var logPath = await ScalarTextAsync("SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(512))").ConfigureAwait(true);
                var backupDir = await ScalarTextAsync("SELECT ISNULL(CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(512)), CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512)))").ConfigureAwait(true);
                var logicalData = await ScalarTextAsync($"SELECT name FROM [{database.DatabaseName}].sys.database_files WHERE type = 0").ConfigureAwait(true);
                var logicalLog = await ScalarTextAsync($"SELECT name FROM [{database.DatabaseName}].sys.database_files WHERE type = 1").ConfigureAwait(true);
                var backup = Path.Combine(backupDir, $"cursor_{Guid.NewGuid():N}.bak");
                try
                {
                    await ExecAsync($"BACKUP DATABASE [{database.DatabaseName}] TO DISK = @f WITH FORMAT, INIT, CHECKSUM", ("@f", backup)).ConfigureAwait(true);
                    await ExecAsync($"RESTORE DATABASE [{copy}] FROM DISK = @f WITH CHECKSUM, MOVE @d TO @m, MOVE @l TO @n",
                        ("@f", backup), ("@d", logicalData), ("@m", Path.Combine(dataPath, copy + ".mdf")),
                        ("@l", logicalLog), ("@n", Path.Combine(logPath, copy + "_log.ldf"))).ConfigureAwait(true);

                    var builder = new SqlConnectionStringBuilder(database.ConnectionString) { InitialCatalog = copy };
                    using var copyContext = new PetraDbContext(new DbContextOptionsBuilder<PetraDbContext>().UseSqlServer(builder.ConnectionString).Options);
                    var service = new SqlLedgerQueryService(copyContext, signer);
                    // Same ids, same data: the copy answers a fresh query...
                    var fresh = await service.GetAccountLedgerAsync(scope, new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(1, 3)), Cancellation).ConfigureAwait(true);
                    Assert.Equal(3, fresh.Lines.Count);
                    // ...but must refuse the cursor that belongs to the source workspace.
                    var refused = await Assert.ThrowsAsync<RequestValidationException>(() => service.GetAccountLedgerAsync(scope,
                        new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(2, 3), cursor), Cancellation)).ConfigureAwait(true);
                    Assert.Equal("ledger.cursor_mismatch", refused.Code);
                }
                finally
                {
                    try { await ExecAsync($"IF DB_ID(N'{copy}') IS NOT NULL BEGIN ALTER DATABASE [{copy}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{copy}]; END").ConfigureAwait(true); }
                    catch (SqlException) { /* throwaway copy */ }
                    try { if (File.Exists(backup)) File.Delete(backup); }
                    catch (IOException) { /* owned by SQL Server */ }
                }
            }
        }
    }
}
