namespace PetraERP.Contracts.Workspaces;

public sealed record WorkspaceSummaryResponse
{
    public required Guid WorkspaceId { get; init; }
    public required Guid CompanyId { get; init; }
    public required string CompanyCode { get; init; }
    public required string CompanyNameArabic { get; init; }
    public string? CompanyNameEnglish { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required Guid FiscalYearId { get; init; }
    public required int YearNumber { get; init; }
    public required string FiscalYearCode { get; init; }
    public required string State { get; init; }
    public required bool IsDefault { get; init; }
}

public sealed record WorkspaceListResponse
{
    public required IReadOnlyList<WorkspaceSummaryResponse> Items { get; init; }
}

public sealed record CreateCompanyWorkspaceRequest
{
    public override string ToString() => nameof(CreateCompanyWorkspaceRequest);
    public string? DatabaseName { get; init; }
    public string? AdministratorPassword { get; init; }
    public string ChartTemplateCode { get; init; } = "PETRA-GENERIC";
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required int InitialYear { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
}

public sealed record CreateFiscalYearWorkspaceRequest
{
    public Guid? SourceFiscalYearId { get; init; }
    public string? DatabaseName { get; init; }
    public required Guid CompanyId { get; init; }
    public required int YearNumber { get; init; }
    public required string YearCode { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public bool CopyChartOfAccounts { get; init; } = true;
    public bool CopyWarehouses { get; init; } = true;
    public bool CopyPatterns { get; init; } = true;
}

public sealed record ProvisioningOperationResponse
{
    public string ReadinessState { get; init; } = "Active";
    public string? Notice { get; init; }
    public required Guid OperationId { get; init; }
    public required Guid? CompanyId { get; init; }
    public required Guid? FiscalYearId { get; init; }
    public required string Status { get; init; }
    public required string TargetDatabaseName { get; init; }
    public string? CurrentStep { get; init; }
    public string? ErrorDetails { get; init; }
}

/// <summary>Legacy-to-independent database migration summary; totals are decimal strings (ADR-0002).</summary>
public sealed record MigrationSummaryResponse
{
    public required Guid CompanyId { get; init; }
    public required Guid FiscalYearId { get; init; }
    public required string TargetDatabaseName { get; init; }
    public required int AccountsMigrated { get; init; }
    public required int CostCentersMigrated { get; init; }
    public required int JournalEntriesMigrated { get; init; }
    public required int JournalLinesMigrated { get; init; }
    public required string TotalDebit { get; init; }
    public required string TotalCredit { get; init; }
    public required int ItemsMigrated { get; init; }
    public required int WarehousesMigrated { get; init; }
    public required int StockMovementsMigrated { get; init; }
    public required string TotalStockQuantity { get; init; }
    public required int SalesInvoicesMigrated { get; init; }
    public required int PurchaseInvoicesMigrated { get; init; }
    public required int VouchersMigrated { get; init; }
    public required IReadOnlyList<string> Discrepancies { get; init; }
    public bool IsDryRun { get; init; } = true;
    public bool IsExecutionApproved { get; init; }
    public IReadOnlyDictionary<string, int> PlannedCounts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> PendingEntities { get; init; } = [];
}

/// <summary>A base currency the server accepts when a company is created.</summary>
public sealed record SupportedCurrencyResponse
{
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public required string NameEnglish { get; init; }
}

public sealed record SupportedCurrencyListResponse
{
    public required IReadOnlyList<SupportedCurrencyResponse> Items { get; init; }
}
