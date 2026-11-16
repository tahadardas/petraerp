namespace PetraERP.Application.Administration;

public sealed record BackupOperationResultDto(
    Guid BackupCatalogId,
    string DatabaseName,
    string BackupFilePath,
    string ChecksumSha256,
    long FileSizeBytes,
    DateTimeOffset CreatedAtUtc,
    bool IsVerified)
{
    /// <summary>Every backup is compressed; it is also encrypted when a password was given.</summary>
    public bool IsEncrypted { get; init; }
    public long OriginalSizeBytes { get; init; }
}

public sealed record BackupVerificationResultDto(
    Guid BackupCatalogId,
    string DatabaseName,
    string BackupFilePath,
    bool IsReadSuccess,
    DateTimeOffset VerifiedAtUtc,
    string? ErrorDetails);

public sealed record RestoreValidationResultDto(
    Guid BackupCatalogId,
    string DisposableDatabaseName,
    bool RestoreSuccess,
    bool DbccCheckdbPass,
    DateTimeOffset TestedAtUtc,
    string Message);

public sealed record BackupCatalogDto(
    Guid Id,
    string DatabaseName,
    string BackupType,
    string BackupFilePath,
    string ChecksumSha256,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? VerifiedAtUtc)
{
    public bool IsCompressed { get; init; }
    public bool IsEncrypted { get; init; }
}

public interface IBackupRestoreCoordinator
{
    /// <summary>Creates a verified, compressed backup; with a <paramref name="password"/> the file is also encrypted and cannot be restored without it.</summary>
    Task<BackupOperationResultDto> BackupDatabaseAsync(string databaseName, string? backupDirectory = null, string? password = null, CancellationToken cancellationToken = default);
    Task<BackupVerificationResultDto> VerifyBackupAsync(Guid backupCatalogId, string? password = null, CancellationToken cancellationToken = default);
    Task<RestoreValidationResultDto> TestRestoreInDisposableDbAsync(Guid backupCatalogId, string? password = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupCatalogDto>> ListBackupsAsync(string? databaseName = null, CancellationToken cancellationToken = default);
}
