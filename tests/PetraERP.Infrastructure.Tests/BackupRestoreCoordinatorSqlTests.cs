#pragma warning disable CA1707

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Administration;

namespace PetraERP.Infrastructure.Tests;

public sealed class BackupRestoreCoordinatorSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task BackupRestoreCoordinator_Backup_Verify_TestRestore_Lifecycle()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PetraERP"] = fixture.Database.ConnectionString,
                ["PetraERP:BackupDirectory"] = @"C:\Users\Public\PetraBackups"
            }).Build();
            var coordinator = new SqlBackupRestoreCoordinator(fixture.System, config, TimeProvider.System);

            // 1. Backup annual database
            var backupResult = await coordinator.BackupDatabaseAsync(provisioned.DatabaseName, null, cancellationToken: Cancellation).ConfigureAwait(true);
            Assert.NotNull(backupResult);
            Assert.Equal(provisioned.DatabaseName, backupResult.DatabaseName);
            Assert.True(File.Exists(backupResult.BackupFilePath));
            Assert.False(string.IsNullOrWhiteSpace(backupResult.ChecksumSha256));
            Assert.True(backupResult.FileSizeBytes > 0);
            Assert.EndsWith(".bak.gz", backupResult.BackupFilePath, StringComparison.OrdinalIgnoreCase); // every backup is compressed
            Assert.False(backupResult.IsEncrypted);

            try
            {
                // 2. Verify backup via RESTORE VERIFYONLY
                var verifyResult = await coordinator.VerifyBackupAsync(backupResult.BackupCatalogId, cancellationToken: Cancellation).ConfigureAwait(true);
                Assert.True(verifyResult.IsReadSuccess);
                Assert.Null(verifyResult.ErrorDetails);

                // 3. Test Restore in Disposable Database with DBCC CHECKDB
                var restoreResult = await coordinator.TestRestoreInDisposableDbAsync(backupResult.BackupCatalogId, cancellationToken: Cancellation).ConfigureAwait(true);
                Assert.True(restoreResult.RestoreSuccess);
                Assert.True(restoreResult.DbccCheckdbPass);
                Assert.Contains("DBCC CHECKDB passed with 0 errors", restoreResult.Message);

                // 4. List Backups
                var list = await coordinator.ListBackupsAsync(provisioned.DatabaseName, Cancellation).ConfigureAwait(true);
                Assert.Single(list);
                Assert.Equal(backupResult.BackupCatalogId, list[0].Id);
            }
            finally
            {
                if (File.Exists(backupResult.BackupFilePath))
                {
                    try { File.Delete(backupResult.BackupFilePath); } catch { }
                }
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task APasswordProtectedBackupCannotBeVerifiedOrRestoredWithoutItsPassword()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PetraERP"] = fixture.Database.ConnectionString,
                ["PetraERP:BackupDirectory"] = @"C:\Users\Public\PetraBackups"
            }).Build();
            var coordinator = new SqlBackupRestoreCoordinator(fixture.System, config, TimeProvider.System);

            var backup = await coordinator.BackupDatabaseAsync(provisioned.DatabaseName, null, "كلمة-سر-قوية-123", Cancellation).ConfigureAwait(true);
            try
            {
                Assert.True(backup.IsEncrypted);
                Assert.EndsWith(".pbk", backup.BackupFilePath, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(backup.BackupFilePath)!, Path.GetFileNameWithoutExtension(backup.BackupFilePath) + ".bak*"));

                var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => coordinator.VerifyBackupAsync(backup.BackupCatalogId, null, Cancellation)).ConfigureAwait(true);
                Assert.Equal("backup.password_required", missing.Code);
                var wrong = await Assert.ThrowsAsync<BusinessRuleException>(() => coordinator.TestRestoreInDisposableDbAsync(backup.BackupCatalogId, "كلمة-خاطئة-999", Cancellation)).ConfigureAwait(true);
                Assert.Equal("backup.password_invalid", wrong.Code);

                Assert.True((await coordinator.VerifyBackupAsync(backup.BackupCatalogId, "كلمة-سر-قوية-123", Cancellation).ConfigureAwait(true)).IsReadSuccess);
                var restored = await coordinator.TestRestoreInDisposableDbAsync(backup.BackupCatalogId, "كلمة-سر-قوية-123", Cancellation).ConfigureAwait(true);
                Assert.True(restored.RestoreSuccess && restored.DbccCheckdbPass);
                var listed = Assert.Single(await coordinator.ListBackupsAsync(provisioned.DatabaseName, Cancellation).ConfigureAwait(true));
                Assert.True(listed.IsEncrypted && listed.IsCompressed);
                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(backup.BackupFilePath)!, "*.restore-*"));
            }
            finally { try { File.Delete(backup.BackupFilePath); } catch (IOException) { } }
        }
    }
}
