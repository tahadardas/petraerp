using PetraERP.ApiClient;

namespace PetraERP.Desktop.Management;

/// <summary>Desktop view of the branch, warehouse, rate-bulletin, document-number, inter-company and report-catalog routes.</summary>
public interface IManagementClient
{
    Task<IReadOnlyList<BranchCardDto>> ListBranchesAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<BranchCardDto> CreateBranchAsync(Guid companyId, Guid branchId, SaveBranchRequest request, CancellationToken cancellationToken);
    Task<BranchCardDto> UpdateBranchAsync(Guid companyId, Guid branchId, Guid target, UpdateBranchRequest request, CancellationToken cancellationToken);
    Task<BranchCardDto> SetBranchActiveAsync(Guid companyId, Guid branchId, Guid target, SetBranchActiveRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<BranchAccountLinkDto>> ListBranchAccountLinksAsync(Guid companyId, Guid branchId, Guid target, CancellationToken cancellationToken);
    Task<BranchAccountLinkDto> SetBranchAccountLinkAsync(Guid companyId, Guid branchId, Guid target, SetBranchAccountLinkRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<BranchSubAccountPlanDto>> PreviewBranchSubAccountsAsync(Guid companyId, Guid branchId, Guid target, Guid modelBranchId, CancellationToken cancellationToken);
    Task<BranchSubAccountApplyResponse> ApplyBranchSubAccountsAsync(Guid companyId, Guid branchId, Guid target, ApplyBranchSubAccountsRequest request, CancellationToken cancellationToken);
    Task<BranchTrialBalanceResponse> GetBranchTrialBalanceAsync(Guid companyId, Guid branchId, DateOnly? fromDate, DateOnly? toDate, string? branchIds, bool eliminate, CancellationToken cancellationToken);

    Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<WarehouseDto> CreateWarehouseAsync(Guid companyId, Guid branchId, CreateWarehouseRequest request, CancellationToken cancellationToken);
    Task<WarehouseAccountingDto> GetWarehouseAccountingAsync(Guid companyId, Guid branchId, Guid warehouseId, CancellationToken cancellationToken);
    Task<WarehouseAccountingDto> SetWarehouseAccountingAsync(Guid companyId, Guid branchId, Guid warehouseId, SetWarehouseAccountingRequest request, CancellationToken cancellationToken);
    Task<WarehouseDto> UpdateWarehouseAsync(Guid companyId, Guid branchId, Guid warehouseId, UpdateWarehouseRequest request, CancellationToken cancellationToken);

    Task<CompanyCurrencySettingsResponse> GetCurrencySettingsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    /// <summary>Finds documents of a numbered family by number text and date range, so a number change never needs a typed identifier.</summary>
    Task<IReadOnlyList<DocumentNumberCandidate>> FindDocumentsAsync(Guid companyId, Guid branchId, string family, string? text, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken);
    Task<ExchangeRatePageResponse> ListRatesAsync(Guid companyId, Guid branchId, string? currencyCode, DateOnly? validFrom, DateOnly? validTo, int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExchangeRateDto>> RegisterRateBulletinAsync(Guid companyId, Guid branchId, RegisterRateBulletinRequest request, CancellationToken cancellationToken);

    Task<DocumentNumberPreviewResponse> PreviewDocumentNumberAsync(Guid companyId, Guid branchId, string family, Guid documentId, string number, CancellationToken cancellationToken);
    Task<DocumentNumberChangeDto> RequestDocumentNumberChangeAsync(Guid companyId, Guid branchId, RequestDocumentNumberChangeRequest request, CancellationToken cancellationToken);
    Task<DocumentNumberChangeDto> DecideDocumentNumberChangeAsync(Guid companyId, Guid branchId, Guid requestId, string decision, string reason, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentNumberChangeDto>> ListDocumentNumberChangesAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentNumberAuditDto>> ListDocumentNumberAuditAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<DocumentNumberChainVerificationResponse> VerifyDocumentNumberAuditAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InterCompanyLinkDto>> ListInterCompanyLinksAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<InterCompanyLinkDto> SaveInterCompanyLinkAsync(Guid companyId, Guid branchId, SaveInterCompanyLinkRequest request, CancellationToken cancellationToken);
    Task<InterCompanyLinkDto> SetInterCompanyLinkActiveAsync(Guid companyId, Guid branchId, Guid linkId, bool active, CancellationToken cancellationToken);
    Task<InterCompanyReconciliationResponse> ReconcileInterCompanyAsync(Guid companyId, Guid branchId, Guid linkId, DateOnly? asOf, CancellationToken cancellationToken);

    Task<AnalyticalReportResponse> RunAnalyticalReportAsync(Guid companyId, Guid branchId, string reportId, DateOnly? fromDate, DateOnly? toDate, string? currencyCode, Guid? accountId, CancellationToken cancellationToken);
    Task<ReportMasterCatalogResponse> GetReportCatalogAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
}

/// <summary>A document found by number/date search; <see cref="Id"/> is internal and never shown to the user.</summary>
public sealed record DocumentNumberCandidate(Guid Id, string Number, DateOnly? Date, string Party, string Status);
