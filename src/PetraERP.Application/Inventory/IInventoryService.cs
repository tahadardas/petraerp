using PetraERP.Domain.Inventory;

namespace PetraERP.Application.Inventory;

public sealed record ItemSearchQuery(
    string? SearchText = null,
    Guid? GroupId = null,
    Guid? WarehouseId = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 50)
{
    public Guid? BranchId { get; init; }
}

public sealed record StockMovementQuery(
    Guid? WarehouseId = null,
    Guid? ItemId = null,
    DateTimeOffset? FromDateUtc = null,
    DateTimeOffset? ToDateUtc = null,
    int Page = 1,
    int PageSize = 50)
{
    public Guid? BranchId { get; init; }
}

public interface IInventoryService
{
    // Items
    Task<ItemPageResult> GetItemsAsync(Guid companyId, ItemSearchQuery query, CancellationToken cancellationToken = default);
    Task<ItemDetailResult> GetItemAsync(Guid companyId, Guid itemId, CancellationToken cancellationToken = default);
    Task<ItemDetailResult> GetItemInBranchAsync(Guid companyId, Guid itemId, Guid? branchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ItemLookupResult>> LookupItemsAsync(Guid companyId, string? searchText, int maxResults = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ItemLookupResult>> LookupItemsInBranchAsync(Guid companyId, string? searchText, int maxResults, Guid? branchId, CancellationToken cancellationToken = default);
    Task<ItemDetailResult> CreateItemAsync(Guid companyId, CreateItemCommand command, CancellationToken cancellationToken = default);
    Task<ItemDetailResult> CreateItemInBranchAsync(Guid companyId, CreateItemCommand command, Guid? branchId, CancellationToken cancellationToken = default);
    Task<ItemDetailResult> UpdateItemAsync(Guid companyId, Guid itemId, UpdateItemCommand command, CancellationToken cancellationToken = default);
    Task<ItemDetailResult> UpdateItemInBranchAsync(Guid companyId, Guid itemId, UpdateItemCommand command, Guid? branchId, CancellationToken cancellationToken = default);
    Task<bool> DeactivateItemAsync(Guid companyId, Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes an item that has no stock movement and no document line; otherwise the caller must deactivate it.</summary>
    Task DeleteItemAsync(Guid companyId, Guid itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<ItemPriceAdjustmentResult> AdjustItemPricesAsync(Guid companyId, ItemPriceAdjustmentCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<bool> ReactivateItemAsync(Guid companyId, Guid itemId, CancellationToken cancellationToken = default);

    // Groups
    Task<IReadOnlyList<ItemGroupResult>> GetItemGroupsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<ItemGroupDetailResult> GetItemGroupAsync(Guid companyId, Guid groupId, CancellationToken cancellationToken = default);
    Task<ItemGroupResult> CreateItemGroupAsync(Guid companyId, CreateItemGroupCommand command, CancellationToken cancellationToken = default);
    Task<ItemGroupResult> UpdateItemGroupAsync(Guid companyId, Guid groupId, UpdateItemGroupCommand command, CancellationToken cancellationToken = default);
    Task<bool> DeactivateItemGroupAsync(Guid companyId, Guid groupId, CancellationToken cancellationToken = default);
    Task<bool> ReactivateItemGroupAsync(Guid companyId, Guid groupId, CancellationToken cancellationToken = default);
    Task<bool> DeleteItemGroupAsync(Guid companyId, Guid groupId, CancellationToken cancellationToken = default);

    // Account Mappings
    Task<IReadOnlyList<ItemAccountMappingResult>> GetAccountMappingsAsync(Guid companyId, int ownerType, Guid ownerId, CancellationToken cancellationToken = default);
    Task SetAccountMappingsAsync(Guid companyId, int ownerType, Guid ownerId, IReadOnlyList<SetItemAccountMappingCommand> mappings, CancellationToken cancellationToken = default);
    Task<EffectiveItemAccountMappingResult> GetEffectiveAccountMappingAsync(Guid companyId, Guid itemId, CancellationToken cancellationToken = default);

    // Units & Conversions
    Task<IReadOnlyList<UnitOfMeasureResult>> GetUnitsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<UnitOfMeasureResult> CreateUnitAsync(Guid companyId, CreateUnitCommand command, CancellationToken cancellationToken = default);
    Task<UnitOfMeasureResult> UpdateUnitAsync(Guid companyId, Guid unitId, UpdateUnitCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UnitConversionResult>> GetUnitConversionsAsync(Guid companyId, Guid? itemId = null, CancellationToken cancellationToken = default);
    Task<UnitConversionResult> CreateUnitConversionAsync(Guid companyId, CreateUnitConversionCommand command, CancellationToken cancellationToken = default);

    // Price lists
    Task<ItemPriceResult> AddItemPriceAsync(Guid companyId, Guid itemId, CreateItemPriceCommand command, CancellationToken cancellationToken = default);
    Task DeleteItemPriceAsync(Guid companyId, Guid itemId, Guid priceId, CancellationToken cancellationToken = default);
    Task<PriceQuoteResult> QuotePriceAsync(Guid companyId, PriceQuoteQuery query, CancellationToken cancellationToken = default);

    // Warehouses
    Task<IReadOnlyList<WarehouseResult>> GetWarehousesAsync(Guid companyId, Guid? branchId = null, CancellationToken cancellationToken = default);
    Task<WarehouseResult> CreateWarehouseAsync(Guid companyId, CreateWarehouseCommand command, CancellationToken cancellationToken = default);
    Task<WarehouseResult> UpdateWarehouseAsync(Guid companyId, Guid warehouseId, UpdateWarehouseCommand command, CancellationToken cancellationToken = default);
    Task<WarehouseResult> UpdateWarehouseInBranchAsync(Guid companyId, Guid warehouseId, UpdateWarehouseCommand command, Guid? branchId, CancellationToken cancellationToken = default);

    // Stock & Movements
    Task<ItemStockSummaryResult> GetItemStockAsync(Guid companyId, Guid itemId, Guid? warehouseId = null, CancellationToken cancellationToken = default);
    Task<ItemStockSummaryResult> GetItemStockInBranchAsync(Guid companyId, Guid itemId, Guid? warehouseId, Guid? branchId, CancellationToken cancellationToken = default);
    Task<StockMovementPageResult> GetStockMovementsAsync(Guid companyId, StockMovementQuery query, CancellationToken cancellationToken = default);
    Task<StockTransferResult> CreateStockTransferAsync(Guid companyId, Guid branchId, Guid userId, CreateStockTransferCommand command, CancellationToken cancellationToken = default);
    Task<StockAdjustmentResult> CreateStockAdjustmentAsync(Guid companyId, Guid branchId, Guid userId, CreateStockAdjustmentCommand command, CancellationToken cancellationToken = default);
    Task<StockAdjustmentResult> ApproveStockAdjustmentAsync(Guid companyId, Guid branchId, Guid adjustmentId, Guid userId, CancellationToken cancellationToken = default);
    Task<StockAdjustmentResult> ApproveStockAdjustmentAsync(Guid companyId, Guid branchId, Guid adjustmentId, Guid userId, InventoryDocumentAction command, CancellationToken cancellationToken = default);
    Task<StockMovementResult> PostInternalIssueAsync(Guid companyId, Guid branchId, Guid userId, PostInternalIssueCommand command, CancellationToken cancellationToken = default);
}
