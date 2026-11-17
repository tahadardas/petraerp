using PetraERP.Contracts.Assets;

namespace PetraERP.Desktop.Assets;

/// <summary>
/// Service client contract for fixed & movable assets, fleet, custody, maintenance, and fuel operations.
/// </summary>
public interface IAssetsClient
{
    Task<IReadOnlyList<AssetDto>> ListAssetsAsync(CancellationToken cancellationToken = default);
    Task<AssetDto?> GetAssetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssetCustodyVoucherDto>> ListCustodyVouchersAsync(CancellationToken cancellationToken = default);
    Task<AssetCustodyVoucherDto> SaveCustodyVoucherAsync(AssetCustodyVoucherDto voucher, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssetCustomerRentalDto>> ListCustomerRentalsAsync(CancellationToken cancellationToken = default);
    Task<AssetCustomerRentalDto> SaveCustomerRentalAsync(AssetCustomerRentalDto rental, CancellationToken cancellationToken = default);
    Task<AssetMaintenanceExpenseBatchDto> SaveMaintenanceBatchAsync(AssetMaintenanceExpenseBatchDto batch, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssetFuelDispensingVoucherDto>> ListFuelVouchersAsync(CancellationToken cancellationToken = default);
    Task<AssetFuelDispensingVoucherDto> SaveFuelDispensingVoucherAsync(AssetFuelDispensingVoucherDto voucher, CancellationToken cancellationToken = default);
    Task<AssetDossierTcoReportDto> GetAssetDossierTcoReportAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<AssetDepreciationRunDto> CalculateDepreciationRunAsync(int fiscalYear, int periodNumber, CancellationToken cancellationToken = default);
    Task<bool> PostDepreciationRunAsync(Guid runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(Guid Id, string Name, decimal MovingAverageCost, decimal CurrentStockLiters)>> ListFuelWarehousesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(Guid Id, string Code, string Name)>> ListPaymentAccountsAsync(CancellationToken cancellationToken = default);

    // Customer Maintenance Center & Workshop Work Orders
    Task<IReadOnlyList<MaintenanceWorkOrderDto>> ListWorkOrdersAsync(CancellationToken cancellationToken = default);
    Task<MaintenanceWorkOrderDto?> GetWorkOrderByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MaintenanceWorkOrderDto> SaveWorkOrderAsync(MaintenanceWorkOrderDto workOrder, CancellationToken cancellationToken = default);
    Task<MaintenanceWorkOrderDto> UpdateWorkOrderStatusAsync(Guid orderId, WorkOrderStatus newStatus, CancellationToken cancellationToken = default);
    Task<(string InvoiceNumber, decimal TotalBilled)> DeliverAndInvoiceWorkOrderAsync(Guid orderId, decimal discount, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListTechniciansAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(string ItemCode, string ItemName, decimal Cost, decimal SellPrice, string Warehouse)>> ListAvailableSparePartsAsync(CancellationToken cancellationToken = default);
}
