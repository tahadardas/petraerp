using PetraERP.ApiClient;
using PetraERP.Desktop.Reports;

namespace PetraERP.Desktop.Connection;

public sealed class ReportsClient(DesktopSession session) : IReportsClient
{
    public Task<IncomeStatementResponse> IncomeStatementAsync(Guid companyId, Guid branchId, DateOnly fromDate, DateOnly toDate, bool consolidated,
        CancellationToken cancellationToken) =>
        session.Api.GetIncomeStatementAsync(companyId, branchId, fromDate, toDate, consolidated, cancellationToken);

    public Task<BalanceSheetResponse> BalanceSheetAsync(Guid companyId, Guid branchId, DateOnly asOf, bool consolidated, CancellationToken cancellationToken) =>
        session.Api.GetBalanceSheetAsync(companyId, branchId, asOf, consolidated, cancellationToken);

    public Task<PartyStatementResponse> PartyStatementAsync(Guid companyId, Guid branchId, Guid partyId, PartyStatementKind kind, string currencyCode,
        DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        session.Api.GetPartyStatementAsync(companyId, branchId, partyId, kind, currencyCode, fromDate, toDate, cancellationToken);

    public Task<AgingResponse> AgingAsync(Guid companyId, Guid branchId, PartyStatementKind kind, string currencyCode, DateOnly asOf,
        CancellationToken cancellationToken) =>
        session.Api.GetAgingAsync(companyId, branchId, kind, currencyCode, asOf, cancellationToken);

    public Task<SalesAnalysisResponse> SalesAnalysisAsync(Guid companyId, Guid branchId, DateOnly fromDate, DateOnly toDate, SalesAnalysisGrouping groupBy, Guid? itemId,
        CancellationToken cancellationToken) =>
        session.Api.GetSalesAnalysisAsync(companyId, branchId, fromDate, toDate, groupBy, itemId: itemId, cancellationToken: cancellationToken);

    public Task<PurchaseAnalysisResponse> PurchaseAnalysisAsync(Guid companyId, Guid branchId, DateOnly fromDate, DateOnly toDate, PurchaseAnalysisGrouping groupBy,
        Guid? itemId, CancellationToken cancellationToken) =>
        session.Api.GetPurchaseAnalysisAsync(companyId, branchId, fromDate, toDate, groupBy, itemId: itemId, cancellationToken: cancellationToken);

    public Task<InventoryValuationResponse> InventoryValuationAsync(Guid companyId, Guid branchId, DateOnly asOf, Guid? warehouseId,
        CancellationToken cancellationToken) =>
        session.Api.GetInventoryValuationAsync(companyId, branchId, asOf, warehouseId, cancellationToken);

    public Task<InventoryMovementReportResponse> InventoryMovementsAsync(Guid companyId, Guid branchId, Guid itemId, Guid? warehouseId, DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken) =>
        session.Api.GetInventoryMovementReportAsync(companyId, branchId, itemId, warehouseId, fromDate, toDate, cancellationToken);

    public Task<IReadOnlyList<CashAccountDto>> CashAccountsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.Api.ListCashAccountsAsync(companyId, branchId, cancellationToken);

    public Task<CashStatementResponse> CashStatementAsync(Guid companyId, Guid branchId, Guid accountId, DateOnly fromDate, DateOnly toDate, bool consolidated,
        CancellationToken cancellationToken) =>
        session.Api.GetCashStatementAsync(companyId, branchId, accountId, fromDate, toDate, consolidated, cancellationToken);
}
