namespace PetraERP.Application.Workspaces;

/// <summary>
/// Ambient context representing the active tenant workspace for the current operation.
/// </summary>
public interface IWorkspaceContext
{
    Guid WorkspaceId { get; }
    Guid CompanyId { get; }
    Guid FiscalYearId { get; }
    Guid BranchId { get; }
    int YearNumber { get; }
    string DatabaseName { get; }
}

public sealed record WorkspaceContextInstance(
    Guid WorkspaceId,
    Guid CompanyId,
    Guid FiscalYearId,
    Guid BranchId,
    int YearNumber,
    string DatabaseName) : IWorkspaceContext;

public sealed record ResolvedFiscalDatabase(
    Guid WorkspaceId,
    Guid CompanyId,
    string CompanyCode,
    string CompanyNameArabic,
    string? CompanyNameEnglish,
    string BaseCurrencyCode,
    Guid FiscalYearId,
    int YearNumber,
    string SafeDatabaseKey,
    string DatabaseName,
    string ConnectionString,
    string State,
    int SchemaVersion,
    bool IsDefault = false);

/// <summary>
/// Resolves the physical database and connection string for a given company and fiscal year.
/// Never trusts raw database names or user-supplied connection strings from the client.
/// </summary>
public interface IFiscalDatabaseResolver
{
    Task<ResolvedFiscalDatabase> ResolveAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves from the registry even if this unit of work already resolved the workspace. <see cref="ResolveAsync"/> may reuse a
    /// result obtained earlier in the same scope; use this after changing the workspace or its database when the new state is required.
    /// </summary>
    Task<ResolvedFiscalDatabase> ResolveFreshAsync(Guid workspaceId, CancellationToken cancellationToken = default) => ResolveAsync(workspaceId, cancellationToken);

    Task<ResolvedFiscalDatabase> ResolveByCompanyAndYearAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResolvedFiscalDatabase>> ListCompanyWorkspacesAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResolvedFiscalDatabase>> ListAllActiveWorkspacesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Validates user authorization for specific company-year workspaces.
/// </summary>
public interface IWorkspaceAuthorizationService
{
    Task<bool> CanAccessWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResolvedFiscalDatabase>> GetAuthorizedWorkspacesAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record ProvisionCompanyInput(
    string Code,
    string NameArabic,
    string? NameEnglish,
    string BaseCurrencyCode,
    int InitialYear,
    DateOnly FiscalYearStart,
    DateOnly FiscalYearEnd,
    string? DatabaseName = null,
    string? ChartTemplateCode = null);

public sealed record ProvisionFiscalYearInput(
    Guid CompanyId,
    int YearNumber,
    string YearCode,
    DateOnly StartDate,
    DateOnly EndDate,
    bool CopyChartOfAccounts = true,
    bool CopyWarehouses = true,
    bool CopyPatterns = true,
    Guid? SourceFiscalYearId = null,
    string? DatabaseName = null,
    string? ChartTemplateCode = null);

public sealed record ProvisioningResult(
    Guid OperationId,
    Guid CompanyId,
    Guid FiscalYearId,
    Guid WorkspaceId,
    string DatabaseName,
    bool Succeeded,
    string? Error,
    string ReadinessState = "Active",
    string? Notice = null);

/// <summary>
/// Atomic provisioning service for independent annual company databases.
/// </summary>
public interface ICompanyProvisioningService
{
    Task<ProvisioningResult> ProvisionCompanyAsync(ProvisionCompanyInput input, Guid requestedByUserId, CancellationToken cancellationToken = default);
    Task<ProvisioningResult> ProvisionFiscalYearDatabaseAsync(ProvisionFiscalYearInput input, Guid requestedByUserId, CancellationToken cancellationToken = default);
}

public sealed record MigrationSummary(
    Guid CompanyId,
    Guid FiscalYearId,
    string TargetDatabaseName,
    int AccountsMigrated,
    int CostCentersMigrated,
    int JournalEntriesMigrated,
    int JournalLinesMigrated,
    decimal TotalDebit,
    decimal TotalCredit,
    int ItemsMigrated,
    int WarehousesMigrated,
    int StockMovementsMigrated,
    decimal TotalStockQuantity,
    int SalesInvoicesMigrated,
    int PurchaseInvoicesMigrated,
    int VouchersMigrated,
    IReadOnlyList<string> Discrepancies)
{
    public bool IsDryRun { get; init; } = true;
    public bool IsExecutionApproved { get; init; }
    public IReadOnlyDictionary<string, int> PlannedCounts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> PendingEntities { get; init; } = [];
}

/// <summary>
/// Migration mechanism from the monolithic database layout into separate annual company databases.
/// </summary>
public interface ILegacyToIndependentDbMigrator
{
    Task<IReadOnlyList<MigrationSummary>> PlanMigrationAsync(CancellationToken cancellationToken = default);
    Task<MigrationSummary> MigrateCompanyYearAsync(Guid companyId, Guid fiscalYearId, bool dryRun = false, CancellationToken cancellationToken = default);
}
