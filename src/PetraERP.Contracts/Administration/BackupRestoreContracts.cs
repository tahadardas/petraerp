using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Administration;

public sealed record BackupDatabaseRequest
{
    [Required]
    [MaxLength(128)]
    public required string DatabaseName { get; init; }

    [MaxLength(500)]
    public string? BackupDirectory { get; init; }

    /// <summary>Optional. With a password the backup file is encrypted and cannot be restored without it (every backup is compressed).</summary>
    [MinLength(8)]
    [MaxLength(128)]
    public string? Password { get; init; }
}

public sealed record VerifyBackupRequest
{
    [Required]
    public required Guid BackupCatalogId { get; init; }

    /// <summary>Required only for a password-protected backup.</summary>
    [MaxLength(128)]
    public string? Password { get; init; }
}

public sealed record TestRestoreRequest
{
    [Required]
    public required Guid BackupCatalogId { get; init; }

    /// <summary>Required only for a password-protected backup.</summary>
    [MaxLength(128)]
    public string? Password { get; init; }
}

public sealed record BackupOperationResponse
{
    public required Guid BackupCatalogId { get; init; }
    public required string DatabaseName { get; init; }
    public required string BackupFilePath { get; init; }
    public required string ChecksumSha256 { get; init; }
    public required long FileSizeBytes { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required bool IsVerified { get; init; }
    public bool IsEncrypted { get; init; }
    public long OriginalSizeBytes { get; init; }
}

public sealed record BackupVerificationResponse
{
    public required Guid BackupCatalogId { get; init; }
    public required string DatabaseName { get; init; }
    public required string BackupFilePath { get; init; }
    public required bool IsReadSuccess { get; init; }
    public required DateTimeOffset VerifiedAtUtc { get; init; }
    public string? ErrorDetails { get; init; }
}

public sealed record RestoreValidationResponse
{
    public required Guid BackupCatalogId { get; init; }
    public required string DisposableDatabaseName { get; init; }
    public required bool RestoreSuccess { get; init; }
    public required bool DbccCheckdbPass { get; init; }
    public required DateTimeOffset TestedAtUtc { get; init; }
    public required string Message { get; init; }
}

public sealed record BackupCatalogItemResponse
{
    public required Guid Id { get; init; }
    public required string DatabaseName { get; init; }
    public required string BackupType { get; init; }
    public required string BackupFilePath { get; init; }
    public required string ChecksumSha256 { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? VerifiedAtUtc { get; init; }
    public bool IsCompressed { get; init; }
    public bool IsEncrypted { get; init; }
}
