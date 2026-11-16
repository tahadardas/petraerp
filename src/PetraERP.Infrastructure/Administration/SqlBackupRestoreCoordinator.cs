using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PetraERP.Application.Administration;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Registry;

namespace PetraERP.Infrastructure.Administration;

public sealed class SqlBackupRestoreCoordinator(
    SystemDbContext systemContext,
    IConfiguration configuration,
    TimeProvider timeProvider) : IBackupRestoreCoordinator
{
    private readonly string connectionString = configuration.GetConnectionString("PetraERP")
        ?? throw new InvalidOperationException("ConnectionStrings:PetraERP is not configured.");

    public async Task<BackupOperationResultDto> BackupDatabaseAsync(string databaseName, string? backupDirectory = null, string? password = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ValidateSafeDatabaseName(databaseName);
        if (!string.IsNullOrEmpty(password) && password.Length < BackupArchive.MinPasswordLength)
            throw new RequestValidationException("backup.password_too_short", $"The backup password must be at least {BackupArchive.MinPasswordLength} characters.", nameof(password));

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var dir = !string.IsNullOrWhiteSpace(backupDirectory)
            ? backupDirectory
            : configuration["PetraERP:BackupDirectory"];

        if (string.IsNullOrWhiteSpace(dir))
        {
            var publicBackups = @"C:\Users\Public\PetraBackups";
            try
            {
                Directory.CreateDirectory(publicBackups);
                dir = publicBackups;
            }
            catch
            {
                dir = await DiscoverBackupDirectoryAsync(connection, cancellationToken).ConfigureAwait(false);
            }
        }

        Directory.CreateDirectory(dir);
        var now = timeProvider.GetUtcNow();
        var baseName = $"{databaseName}_{now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}";
        var rawPath = Path.Combine(dir, baseName + ".bak");
        var protectedBackup = !string.IsNullOrEmpty(password);
        var finalPath = Path.Combine(dir, baseName + (protectedBackup ? BackupArchive.ProtectedExtension : BackupArchive.CompressedExtension));
        var backupPath = rawPath;

        // Execute BACKUP DATABASE with CHECKSUM
        var backupSql = $"BACKUP DATABASE [{databaseName}] TO DISK = @path WITH FORMAT, INIT, CHECKSUM, STATS = 10;";
        using (var cmd = new SqlCommand(backupSql, connection))
        {
            cmd.Parameters.AddWithValue("@path", backupPath);
            cmd.CommandTimeout = 300;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Verify with RESTORE VERIFYONLY
        var verifySql = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;";
        using (var cmd = new SqlCommand(verifySql, connection))
        {
            cmd.Parameters.AddWithValue("@path", backupPath);
            cmd.CommandTimeout = 300;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Compress (and encrypt when a password was given); the raw .bak never stays on disk.
        var originalSize = new FileInfo(rawPath).Length;
        try { await BackupArchive.PackAsync(rawPath, finalPath, password, cancellationToken).ConfigureAwait(false); }
        finally { try { File.Delete(rawPath); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        backupPath = finalPath;

        // Compute SHA-256 of the file that is kept
        var fileInfo = new FileInfo(backupPath);
        string sha256;
        using (var stream = File.OpenRead(backupPath))
        {
            var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            sha256 = Convert.ToHexString(hashBytes);
        }

        var catalogRow = new BackupCatalogRow
        {
            Id = Guid.NewGuid(),
            DatabaseName = databaseName,
            BackupType = "Full",
            BackupFilePath = backupPath,
            ChecksumSha256 = sha256,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            VerifiedAtUtc = now,
        };

        systemContext.BackupCatalog.Add(catalogRow);
        await systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new BackupOperationResultDto(
            catalogRow.Id,
            databaseName,
            backupPath,
            sha256,
            fileInfo.Length,
            now,
            true)
        {
            IsEncrypted = protectedBackup,
            OriginalSizeBytes = originalSize,
        };
    }

    public async Task<BackupVerificationResultDto> VerifyBackupAsync(Guid backupCatalogId, string? password = null, CancellationToken cancellationToken = default)
    {
        var record = await systemContext.BackupCatalog.SingleOrDefaultAsync(b => b.Id == backupCatalogId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("BackupCatalogRecord");

        if (!File.Exists(record.BackupFilePath))
            throw new FileNotFoundException("Backup file does not exist on disk.", record.BackupFilePath);

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // A protected archive is opened with the password first; a wrong one stops here, before SQL Server is asked anything.
        using var unpacked = await BackupArchive.UnpackAsync(record.BackupFilePath, password, cancellationToken).ConfigureAwait(false);
        var verifySql = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;";
        using var cmd = new SqlCommand(verifySql, connection);
        cmd.Parameters.AddWithValue("@path", unpacked.Path);
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        record.VerifiedAtUtc = now;
        record.UpdatedAtUtc = now;
        await systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new BackupVerificationResultDto(
            record.Id,
            record.DatabaseName,
            record.BackupFilePath,
            true,
            now,
            null);
    }

    public async Task<RestoreValidationResultDto> TestRestoreInDisposableDbAsync(Guid backupCatalogId, string? password = null, CancellationToken cancellationToken = default)
    {
        var record = await systemContext.BackupCatalog.SingleOrDefaultAsync(b => b.Id == backupCatalogId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("BackupCatalogRecord");

        if (!File.Exists(record.BackupFilePath))
            throw new FileNotFoundException("Backup file does not exist on disk.", record.BackupFilePath);

        // Nothing is restored unless the archive opens: a protected backup needs its password.
        using var unpacked = await BackupArchive.UnpackAsync(record.BackupFilePath, password, cancellationToken).ConfigureAwait(false);
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var (dataPath, logPath) = await DiscoverDataAndLogPathsAsync(connection, cancellationToken).ConfigureAwait(false);
        var disposableDbName = $"Petra_RestoreTest_{Guid.NewGuid().ToString("N")[..12]}";
        var tempMdf = Path.Combine(dataPath, $"{disposableDbName}.mdf");
        var tempLdf = Path.Combine(logPath, $"{disposableDbName}.ldf");

        try
        {
            // Read logical names from backup
            var logicalNames = new List<(string LogicalName, string Type)>();
            var fileListSql = "RESTORE FILELISTONLY FROM DISK = @path;";
            using (var cmd = new SqlCommand(fileListSql, connection))
            {
                cmd.Parameters.AddWithValue("@path", unpacked.Path);
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    logicalNames.Add((reader.GetString(0), reader.GetString(2))); // LogicalName, Type ('D' or 'L')
                }
            }

            var dataLogical = logicalNames.FirstOrDefault(l => l.Type == "D").LogicalName;
            var logLogical = logicalNames.FirstOrDefault(l => l.Type == "L").LogicalName;

            if (string.IsNullOrEmpty(dataLogical) || string.IsNullOrEmpty(logLogical))
                throw new BusinessRuleException("backup.invalid_filelist", "Backup does not contain valid data and log file definitions.");

            // Restore into disposable database
            var restoreSql = $"""
                RESTORE DATABASE [{disposableDbName}]
                FROM DISK = @path
                WITH MOVE @dataLogical TO @mdfPath,
                     MOVE @logLogical TO @ldfPath,
                     CHECKSUM,
                     RECOVERY;
                """;

            using (var cmd = new SqlCommand(restoreSql, connection))
            {
                cmd.Parameters.AddWithValue("@path", unpacked.Path);
                cmd.Parameters.AddWithValue("@dataLogical", dataLogical);
                cmd.Parameters.AddWithValue("@mdfPath", tempMdf);
                cmd.Parameters.AddWithValue("@logLogical", logLogical);
                cmd.Parameters.AddWithValue("@ldfPath", tempLdf);
                cmd.CommandTimeout = 300;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // DBCC CHECKDB
            var checkdbSql = $"DBCC CHECKDB ([{disposableDbName}]) WITH NO_INFOMSGS, ALL_ERRORMSGS;";
            using (var cmd = new SqlCommand(checkdbSql, connection))
            {
                cmd.CommandTimeout = 300;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var now = timeProvider.GetUtcNow();
            return new RestoreValidationResultDto(
                record.Id,
                disposableDbName,
                true,
                true,
                now,
                "Restored successfully in disposable database and DBCC CHECKDB passed with 0 errors.");
        }
        finally
        {
            // Always drop the disposable database
            try
            {
                var dropSql = $"""
                    IF DB_ID('{disposableDbName}') IS NOT NULL
                    BEGIN
                        ALTER DATABASE [{disposableDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                        DROP DATABASE [{disposableDbName}];
                    END
                    """;
                using var cmd = new SqlCommand(dropSql, connection);
                await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Ignore cleanup error
            }

            try { if (File.Exists(tempMdf)) File.Delete(tempMdf); } catch { }
            try { if (File.Exists(tempLdf)) File.Delete(tempLdf); } catch { }
        }
    }

    public async Task<IReadOnlyList<BackupCatalogDto>> ListBackupsAsync(string? databaseName = null, CancellationToken cancellationToken = default)
    {
        var query = systemContext.BackupCatalog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(databaseName))
            query = query.Where(b => b.DatabaseName == databaseName);

        var list = await query.OrderByDescending(b => b.CreatedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
        return list.Select(b => new BackupCatalogDto(
            b.Id,
            b.DatabaseName,
            b.BackupType,
            b.BackupFilePath,
            b.ChecksumSha256,
            b.CreatedAtUtc,
            b.VerifiedAtUtc)
        {
            IsCompressed = BackupArchive.IsCompressedPath(b.BackupFilePath),
            IsEncrypted = BackupArchive.IsProtectedPath(b.BackupFilePath),
        }).ToList();
    }

    private static void ValidateSafeDatabaseName(string name)
    {
        if (name.Length > 128 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new RequestValidationException("backup.database_name_invalid", "Database name must be alphanumeric ASCII with underscores only.", nameof(name));
    }

    private static async Task<string> DiscoverBackupDirectoryAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var sql = """
            DECLARE @dir nvarchar(512);
            EXEC master.dbo.xp_instance_regread
                N'HKEY_LOCAL_MACHINE',
                N'Software\Microsoft\MSSQLServer\MSSQLServer',
                N'BackupDirectory',
                @dir OUTPUT;
            SELECT ISNULL(@dir, CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512)));
            """;
        try
        {
            using var cmd = new SqlCommand(sql, connection);
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is string path && !string.IsNullOrWhiteSpace(path))
                return path;
        }
        catch
        {
            // Fallback if regread permission denied
        }
        return Path.Combine(Path.GetTempPath(), "PetraERP_Backups");
    }

    private static async Task<(string dataPath, string logPath)> DiscoverDataAndLogPathsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var sql = """
            SELECT
                CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512)) AS DataPath,
                CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(512)) AS LogPath;
            """;
        try
        {
            using var cmd = new SqlCommand(sql, connection);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var data = reader.IsDBNull(0) ? Path.GetTempPath() : reader.GetString(0);
                var log = reader.IsDBNull(1) ? data : reader.GetString(1);
                return (data, log);
            }
        }
        catch
        {
            // Fallback
        }
        return (Path.GetTempPath(), Path.GetTempPath());
    }
}
