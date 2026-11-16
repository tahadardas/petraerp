using PetraERP.ApiClient;

namespace PetraERP.Desktop.Reports;

/// <summary>Typed-client seam for the financial and commercial reports; every figure is computed by the server.</summary>
public interface IReportsClient
{
    Task<IncomeStatementResponse> IncomeStatementAsync(Guid companyId, Guid branchId, DateOnly fromDate, DateOnly toDate, bool consolidated, CancellationToken cancellationToken);

    Task<BalanceSheetResponse> BalanceSheetAsync(Guid companyId, Guid branchId, DateOnly asOf, bool consolidated, CancellationToken cancellationToken);

    Task<PartyStatementResponse> PartyStatementAsync(Guid companyId, Guid branchId, Guid partyId, PartyStatementKind kind, string currencyCode, DateOnly fromDate,
        DateOnly toDate, CancellationToken cancellationToken);

    Task<AgingResponse> AgingAsync(Guid companyId, Guid branchId, PartyStatementKind kind, string currencyCode, DateOnly asOf, CancellationToken cancellationToken);

    Task<SalesAnalysisResponse> SalesAnalysisAsync(Guid companyId, Guid branchId, DateOnly fromDate, DateOnly toDate, SalesAnalysisGrouping groupBy, Guid? itemId,
        CancellationToken cancellationToken);

    Task<PurchaseAnalysisResponse> PurchaseAnalysisAsync(Guid companyId, Guid branchId, DateOnly fromDate, DateOnly toDate, PurchaseAnalysisGrouping groupBy, Guid? itemId,
        CancellationToken cancellationToken);

    Task<InventoryValuationResponse> InventoryValuationAsync(Guid companyId, Guid branchId, DateOnly asOf, Guid? warehouseId, CancellationToken cancellationToken);

    Task<InventoryMovementReportResponse> InventoryMovementsAsync(Guid companyId, Guid branchId, Guid itemId, Guid? warehouseId, DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CashAccountDto>> CashAccountsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);

    Task<CashStatementResponse> CashStatementAsync(Guid companyId, Guid branchId, Guid accountId, DateOnly fromDate, DateOnly toDate, bool consolidated,
        CancellationToken cancellationToken);
}
