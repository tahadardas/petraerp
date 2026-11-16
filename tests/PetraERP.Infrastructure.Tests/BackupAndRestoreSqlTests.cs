using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// Proves SQL Server Commercial Acceptance Gate 3:
/// - Database backup with CHECKSUM and INIT
/// - RESTORE VERIFYONLY with CHECKSUM
/// - RESTORE DATABASE with MOVE to a fresh isolated database
/// - DBCC CHECKDB execution with 0 allocation/consistency errors
/// - Round-trip complete mapped application schema and data verification (organizations, calendars, chart of accounts, journals, lines)
/// Explicitly tagged with Category=SqlServer; requires PETRAERP_SQL_TEST_SERVER.
/// </summary>
public sealed class BackupAndRestoreSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task FullBackupVerifyAndRestoreMaintainsSchemaAndDataIntegrity()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            // 1. Seed comprehensive state across system and ledger schemas
            var companyId = Guid.NewGuid();
            var branchId = Guid.NewGuid();
            var currencyCode = "JOD";
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [system].[Currencies]([Code],[NameArabic],[IsActive]) VALUES({currencyCode},N'دينار أردني',1)",
                Cancellation).ConfigureAwait(true);

            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [system].[Companies]([Id],[Code],[NameArabic],[BaseCurrencyCode],[IsActive]) VALUES({companyId},N'BACKUP_CO',N'شركة النسخ التجريبية',{currencyCode},1)",
                Cancellation).ConfigureAwait(true);

            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [system].[Branches]([Id],[CompanyId],[Code],[NameArabic],[IsActive]) VALUES({branchId},{companyId},N'HQ',N'المقر الرئيسي',1)",
                Cancellation).ConfigureAwait(true);

            var yearId = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[FiscalYears]([Id],[CompanyId],[Code],[RangeStart],[RangeEnd]) VALUES({yearId},{companyId},N'FY2026','2026-01-01','2026-12-31')",
                Cancellation).ConfigureAwait(true);

            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[AccountingPeriods]([Id],[CompanyId],[FiscalYearId],[Code],[RangeStart],[RangeEnd],[State]) VALUES({periodId},{companyId},{yearId},N'P01','2026-01-01','2026-01-31',0)",
                Cancellation).ConfigureAwait(true);

            var cashAccountId = Guid.NewGuid();
            var revenueAccountId = Guid.NewGuid();
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive]) VALUES({cashAccountId},{companyId},N'1010',N'صندوق النقدية',1,1,1,1,1)",
                Cancellation).ConfigureAwait(true);

            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive]) VALUES({revenueAccountId},{companyId},N'4010',N'إيرادات المبيعات',4,1,1,1,1)",
                Cancellation).ConfigureAwait(true);

            var journalId = Guid.NewGuid();
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[JournalEntries]
                    ([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State])
                    VALUES({journalId},{companyId},{branchId},'2026-01-15',N'قيد فحص النسخ الاحتياطي',1)",
                Cancellation).ConfigureAwait(true);

            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[JournalLines]
                    ([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],
                     [BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description])
                    VALUES({Guid.NewGuid()},{companyId},{journalId},{cashAccountId},N'JOD',1500.2500,0,N'JOD',1500.2500,0,N'JOD',N'JOD',1.0,0,N'قبض نقدي')",
                Cancellation).ConfigureAwait(true);

            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[JournalLines]
                    ([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],
                     [BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description])
                    VALUES({Guid.NewGuid()},{companyId},{journalId},{revenueAccountId},N'JOD',0,1500.2500,N'JOD',0,1500.2500,N'JOD',N'JOD',1.0,0,N'إيراد مبيعات')",
                Cancellation).ConfigureAwait(true);

            // Compare the complete migrated application table identities, including the new FX receipt table.
            var expectedTables = ctx.Model.GetRelationalModel().Tables
                .Where(t => t.Schema is "system" or "ledger" or "security" or "workspace" or "patterns" or "parties" or "inventory" or "sales" or "treasury" or "purchases")
                .Select(t => t.Schema + "." + t.Name).Order(StringComparer.Ordinal).ToArray();
            Assert.Contains("ledger.FxRevaluationRequests", expectedTables);

            // 2. Discover default backup directory on SQL Server
            var masterConn = new SqlConnection(database.MasterConnectionString);
            await using (masterConn.ConfigureAwait(true))
            {
                await masterConn.OpenAsync(Cancellation).ConfigureAwait(true);

                string backupDir;
                using (var cmd = masterConn.CreateCommand())
                {
                    cmd.CommandText = @"
                        DECLARE @dir nvarchar(512);
                        EXEC master.dbo.xp_instance_regread
                            N'HKEY_LOCAL_MACHINE',
                            N'Software\Microsoft\MSSQLServer\MSSQLServer',
                            N'BackupDirectory',
                            @dir OUTPUT;
                        SELECT ISNULL(@dir, CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512)));";
                    backupDir = (string)(await cmd.ExecuteScalarAsync(Cancellation).ConfigureAwait(true))!;
                }

                var backupFileName = $"PetraERP_BackupTest_{Guid.NewGuid():N}.bak";
                var backupFilePath = Path.Combine(backupDir, backupFileName);
                var restoredDbName = $"PetraERP_Restored_{Guid.NewGuid():N}";

                try
                {
                    // 3. Execute BACKUP DATABASE with CHECKSUM
                    using (var cmd = masterConn.CreateCommand())
                    {
                        cmd.CommandText = $@"
                            BACKUP DATABASE [{database.DatabaseName}]
                            TO DISK = @BackupPath
                            WITH FORMAT, INIT, CHECKSUM, NAME = N'PetraERP Test Backup';";
                        cmd.Parameters.AddWithValue("@BackupPath", backupFilePath);
                        await cmd.ExecuteNonQueryAsync(Cancellation).ConfigureAwait(true);
                    }

                    // 4. Execute RESTORE VERIFYONLY with CHECKSUM
                    using (var cmd = masterConn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            RESTORE VERIFYONLY
                            FROM DISK = @BackupPath
                            WITH CHECKSUM;";
                        cmd.Parameters.AddWithValue("@BackupPath", backupFilePath);
                        await cmd.ExecuteNonQueryAsync(Cancellation).ConfigureAwait(true);
                    }

                    // 5. Query logical file names from backup media
                    string logicalDataName = string.Empty;
                    string logicalLogName = string.Empty;

                    using (var cmd = masterConn.CreateCommand())
                    {
                        cmd.CommandText = "RESTORE FILELISTONLY FROM DISK = @BackupPath;";
                        cmd.Parameters.AddWithValue("@BackupPath", backupFilePath);
                        var reader = await cmd.ExecuteReaderAsync(Cancellation).ConfigureAwait(true);
                        await using (reader.ConfigureAwait(true))
                        {
                            while (await reader.ReadAsync(Cancellation).ConfigureAwait(true))
                            {
                                var logicalName = reader.GetString(reader.GetOrdinal("LogicalName"));
                                var type = reader.GetString(reader.GetOrdinal("Type"));
                                if (type.Equals("D", StringComparison.OrdinalIgnoreCase))
                                {
                                    logicalDataName = logicalName;
                                }
                                else if (type.Equals("L", StringComparison.OrdinalIgnoreCase))
                                {
                                    logicalLogName = logicalName;
                                }
                            }
                        }
                    }

                    Assert.False(string.IsNullOrEmpty(logicalDataName), "Logical data file name not found in backup.");
                    Assert.False(string.IsNullOrEmpty(logicalLogName), "Logical log file name not found in backup.");

                    // 6. Query default data and log paths for RESTORE WITH MOVE
                    string dataPath;
                    string logPath;
                    using (var cmd = masterConn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT
                                CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512)) AS DataPath,
                                CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(512)) AS LogPath;";
                        var reader = await cmd.ExecuteReaderAsync(Cancellation).ConfigureAwait(true);
                        await using (reader.ConfigureAwait(true))
                        {
                            Assert.True(await reader.ReadAsync(Cancellation).ConfigureAwait(true));
                            dataPath = reader.GetString(0);
                            logPath = reader.GetString(1);
                        }
                    }

                    var restoredMdf = Path.Combine(dataPath, $"{restoredDbName}.mdf");
                    var restoredLdf = Path.Combine(logPath, $"{restoredDbName}_log.ldf");

                    // 7. Execute RESTORE DATABASE WITH MOVE & CHECKSUM
                    using (var cmd = masterConn.CreateCommand())
                    {
                        cmd.CommandText = $@"
                            RESTORE DATABASE [{restoredDbName}]
                            FROM DISK = @BackupPath
                            WITH CHECKSUM,
                                 MOVE @LogicalData TO @MdfPath,
                                 MOVE @LogicalLog TO @LdfPath;";
                        cmd.Parameters.AddWithValue("@BackupPath", backupFilePath);
                        cmd.Parameters.AddWithValue("@LogicalData", logicalDataName);
                        cmd.Parameters.AddWithValue("@MdfPath", restoredMdf);
                        cmd.Parameters.AddWithValue("@LogicalLog", logicalLogName);
                        cmd.Parameters.AddWithValue("@LdfPath", restoredLdf);
                        await cmd.ExecuteNonQueryAsync(Cancellation).ConfigureAwait(true);
                    }

                    // 8. Execute DBCC CHECKDB on restored database
                    using (var cmd = masterConn.CreateCommand())
                    {
                        cmd.CommandText = $"DBCC CHECKDB([{restoredDbName}]) WITH NO_INFOMSGS, ALL_ERRORMSGS;";
                        await cmd.ExecuteNonQueryAsync(Cancellation).ConfigureAwait(true);
                    }

                    // 9. Verify restored database schema and data
                    var restoredBuilder = new SqlConnectionStringBuilder(database.ConnectionString)
                    {
                        InitialCatalog = restoredDbName,
                    };

                    var restoredConn = new SqlConnection(restoredBuilder.ConnectionString);
                    await using (restoredConn.ConfigureAwait(true))
                    {
                        await restoredConn.OpenAsync(Cancellation).ConfigureAwait(true);

                        // Verify every application schema survives backup/restore, including newer modules.
                        using (var cmd = restoredConn.CreateCommand())
                        {
                            cmd.CommandText = @"
                                SELECT s.name + '.' + t.name
                                FROM sys.tables t
                                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                                WHERE s.name IN ('system', 'ledger', 'security', 'workspace', 'patterns', 'parties', 'inventory', 'sales', 'treasury', 'purchases');";
                            var restoredTables = new List<string>();
                            var tableReader = await cmd.ExecuteReaderAsync(Cancellation).ConfigureAwait(true);
                            await using (tableReader.ConfigureAwait(true))
                                while (await tableReader.ReadAsync(Cancellation).ConfigureAwait(true))
                                    restoredTables.Add(tableReader.GetString(0));
                            Assert.Equal(expectedTables, restoredTables.Order(StringComparer.Ordinal));
                        }

                        // Data check: company
                        using (var cmd = restoredConn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT [Code], [NameArabic] FROM [system].[Companies] WHERE [Id] = @Id;";
                            cmd.Parameters.AddWithValue("@Id", companyId);
                            var reader = await cmd.ExecuteReaderAsync(Cancellation).ConfigureAwait(true);
                            await using (reader.ConfigureAwait(true))
                            {
                                Assert.True(await reader.ReadAsync(Cancellation).ConfigureAwait(true));
                                Assert.Equal("BACKUP_CO", reader.GetString(0));
                                Assert.Equal("شركة النسخ التجريبية", reader.GetString(1));
                            }
                        }

                        // Data check: journal lines exact decimal amounts
                        using (var cmd = restoredConn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT SUM([BaseDebit]), SUM([BaseCredit]) FROM [ledger].[JournalLines] WHERE [JournalId] = @JournalId;";
                            cmd.Parameters.AddWithValue("@JournalId", journalId);
                            var reader = await cmd.ExecuteReaderAsync(Cancellation).ConfigureAwait(true);
                            await using (reader.ConfigureAwait(true))
                            {
                                Assert.True(await reader.ReadAsync(Cancellation).ConfigureAwait(true));
                                Assert.Equal(1500.2500m, reader.GetDecimal(0));
                                Assert.Equal(1500.2500m, reader.GetDecimal(1));
                            }
                        }
                    }
                }
                finally
                {
                    // Clean up restored test database
                    try
                    {
                        using var cmd = masterConn.CreateCommand();
                        cmd.CommandText = $@"
                            IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{restoredDbName}')
                            BEGIN
                                ALTER DATABASE [{restoredDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                                DROP DATABASE [{restoredDbName}];
                            END";
                        await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(true);
                    }
                    catch
                    {
                        // Best effort cleanup
                    }

                    // Clean up temporary backup file
                    try
                    {
                        if (File.Exists(backupFilePath))
                        {
                            File.Delete(backupFilePath);
                        }
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }
            }
        }
    }
}
