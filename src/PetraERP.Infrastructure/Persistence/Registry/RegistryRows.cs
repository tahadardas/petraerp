using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Persistence.Registry;

public sealed class CompanyDatabaseRegistryRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string DatabaseKey { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class FiscalDatabaseRegistryRow : ITrackedStorageRow
{
    public string? ProvisioningFingerprint { get; set; }
    public Guid? SourceFiscalYearId { get; set; }
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FiscalYearId { get; set; }
    public int YearNumber { get; set; }
    public string? YearCode { get; set; }
    public DateOnly? RangeStart { get; set; }
    public DateOnly? RangeEnd { get; set; }
    public string SafeDatabaseKey { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string State { get; set; } = "Active";
    public int SchemaVersion { get; set; } = 1;
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ProvisioningOperationRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? FiscalYearId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string TargetDatabaseName { get; set; } = string.Empty;
    public string? CurrentStep { get; set; }
    public string? ErrorDetails { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class DatabaseVersionRegistryRow
{
    public Guid Id { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string MigrationId { get; set; } = string.Empty;
    public string ProductVersion { get; set; } = string.Empty;
    public DateTimeOffset AppliedAtUtc { get; set; }
}

public sealed class BackupCatalogRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string BackupType { get; set; } = "Full";
    public string BackupFilePath { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public long? FirstLsn { get; set; }
    public long? LastLsn { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GlobalEntityMappingRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid GlobalEntityId { get; set; }
    public Guid LocalEntityId { get; set; }
    public Guid FiscalYearId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class IntercompanyOperationRegistryRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid SourceCompanyId { get; set; }
    public Guid TargetCompanyId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public Guid SourceDocumentId { get; set; }
    public Guid? TargetDocumentId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One Al-Ameen migration attempt, recorded centrally so progress survives an API restart and so activation can be
/// proved against the capture and the reconciliation that were actually reviewed. No source connection string is kept.
/// </summary>
public sealed class MigrationRunRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? FiscalYearId { get; set; }
    public Guid? BranchId { get; set; }
    public int Mode { get; set; }
    public int State { get; set; }
    public DateOnly CutoffDate { get; set; }
    public string SourceFingerprint { get; set; } = string.Empty;
    public string SourceDatabaseName { get; set; } = string.Empty;
    public string? TargetDatabaseName { get; set; }
    public int IdentityIssueCount { get; set; }
    public bool? ReconciliationPassed { get; set; }
    public string? ReconciliationJson { get; set; }
    public DateTimeOffset? ReconciledAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
