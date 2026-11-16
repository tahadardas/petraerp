using PetraERP.ApiClient;

namespace PetraERP.Desktop.Inventory;

/// <summary>
/// Client contract for Inventory &amp; Items operations through the generated API client.
/// Server is authoritative for stock calculations, negative-stock prevention, and movement ledgers.
/// </summary>
public interface IInventoryClient
{
    Task<ItemPageResponse> ListItemsAsync(Guid companyId, Guid branchId, int page, int pageSize, string? search, Guid? groupId, Guid? warehouseId, bool? isActive, CancellationToken cancellationToken);
    Task<ItemDetailDto> GetItemAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ItemLookupDto>> LookupItemsAsync(Guid companyId, Guid branchId, int maxResults, string? search, CancellationToken cancellationToken);
    Task<ItemDetailDto> CreateItemAsync(Guid companyId, Guid branchId, CreateItemRequest request, CancellationToken cancellationToken);
    Task<ItemDetailDto> UpdateItemAsync(Guid companyId, Guid branchId, Guid itemId, UpdateItemRequest request, CancellationToken cancellationToken);
    Task DeactivateItemAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken);

    Task DeleteItemAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken) => throw new NotSupportedException();
    Task<AdjustItemPricesResponse> AdjustItemPricesAsync(Guid companyId, Guid branchId, AdjustItemPricesRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    Task ReactivateItemAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken);
    Task<ItemStockSummaryDto> GetItemStockAsync(Guid companyId, Guid branchId, Guid itemId, Guid? warehouseId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ItemGroupDto>> ListItemGroupsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<ItemGroupDto> CreateItemGroupAsync(Guid companyId, Guid branchId, CreateItemGroupRequest request, CancellationToken cancellationToken);
    Task<ItemGroupDto> UpdateItemGroupAsync(Guid companyId, Guid branchId, Guid groupId, UpdateItemGroupRequest request, CancellationToken cancellationToken);
    Task<ItemGroupDetailDto> GetItemGroupAsync(Guid companyId, Guid branchId, Guid groupId, CancellationToken cancellationToken);
    Task<bool> DeactivateItemGroupAsync(Guid companyId, Guid branchId, Guid groupId, CancellationToken cancellationToken);
    Task<bool> ReactivateItemGroupAsync(Guid companyId, Guid branchId, Guid groupId, CancellationToken cancellationToken);
    Task<bool> DeleteItemGroupAsync(Guid companyId, Guid branchId, Guid groupId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ItemAccountMappingDto>> GetAccountMappingsAsync(Guid companyId, Guid branchId, int ownerType, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> SetAccountMappingsAsync(Guid companyId, Guid branchId, SetAccountMappingsRequest request, CancellationToken cancellationToken);
    Task<EffectiveItemAccountMappingDto> GetEffectiveAccountMappingAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<UnitOfMeasureDto>> ListUnitsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);
    Task<UnitOfMeasureDto> CreateUnitAsync(Guid companyId, Guid branchId, CreateUnitRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<UnitConversionDto>> ListUnitConversionsAsync(Guid companyId, Guid branchId, Guid? itemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(Guid companyId, Guid branchId, Guid? filterBranchId, CancellationToken cancellationToken);
    Task<StockMovementPageResponse> ListStockMovementsAsync(Guid companyId, Guid branchId, int page, int pageSize, Guid? warehouseId, Guid? itemId, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken cancellationToken);
    Task<StockTransferDto> CreateStockTransferAsync(Guid companyId, Guid branchId, CreateStockTransferRequest request, CancellationToken cancellationToken);
    Task<StockAdjustmentDto> CreateStockAdjustmentAsync(Guid companyId, Guid branchId, CreateStockAdjustmentRequest request, CancellationToken cancellationToken);
    Task<StockAdjustmentDto> ApproveStockAdjustmentAsync(Guid companyId, Guid branchId, Guid adjustmentId, InventoryDocumentActionRequest request, CancellationToken cancellationToken);
    Task<StockMovementDto> PostInternalIssueAsync(Guid companyId, Guid branchId, PostInternalIssueRequest request, CancellationToken cancellationToken);
}
