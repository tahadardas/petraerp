namespace PetraERP.Application.Inventory;

/// <summary>Optional accounting profile of a warehouse. Null means "inherit branch mapping / none".</summary>
public sealed record WarehouseAccountingResult(Guid WarehouseId, Guid? ParentWarehouseId, Guid? InventoryAccountId, string? InventoryAccountCode,
    Guid? InTransitAccountId, string? InTransitAccountCode, Guid? DefaultCostCenterId, string? DefaultCostCenterCode);

public sealed record SetWarehouseAccountingCommand(Guid? ParentWarehouseId, Guid? InventoryAccountId, Guid? InTransitAccountId, Guid? DefaultCostCenterId);

public interface IWarehouseAccountingService
{
    Task<WarehouseAccountingResult> GetAsync(Guid companyId, Guid branchId, Guid warehouseId, CancellationToken cancellationToken = default);

    /// <summary>Validates: parent in same company+branch, no cycle, not itself; accounts active, postable and of the same company; cost center active.</summary>
    Task<WarehouseAccountingResult> SetAsync(Guid companyId, Guid branchId, Guid warehouseId, SetWarehouseAccountingCommand command, CancellationToken cancellationToken = default);
}
