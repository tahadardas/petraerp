using PetraERP.ApiClient;
using PetraERP.Desktop.Inventory;

namespace PetraERP.Desktop.Connection;

public sealed class InventoryClient(DesktopSession session) : IInventoryClient
{
    public Task<ItemPageResponse> ListItemsAsync(Guid companyId, Guid branchId, int page, int pageSize, string? search, Guid? groupId, Guid? warehouseId, bool? isActive, CancellationToken cancellationToken) =>
        session.Api.ListItemsAsync(companyId, branchId, page, pageSize, search, groupId, warehouseId, isActive, cancellationToken);

    public Task<ItemDetailDto> GetItemAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken) =>
        session.Api.GetItemAsync(companyId, branchId, itemId, cancellationToken);

    public Task<IReadOnlyList<ItemLookupDto>> LookupItemsAsync(Guid companyId, Guid branchId, int maxResults, string? search, CancellationToken cancellationToken) =>
        session.Api.LookupItemsAsync(companyId, branchId, maxResults, search, cancellationToken);

    public Task<ItemDetailDto> CreateItemAsync(Guid companyId, Guid branchId, CreateItemRequest request, CancellationToken cancellationToken) =>
        session.Api.CreateItemAsync(companyId, branchId, request, cancellationToken);

    public Task<ItemDetailDto> UpdateItemAsync(Guid companyId, Guid branchId, Guid itemId, UpdateItemRequest request, CancellationToken cancellationToken) =>
        session.Api.UpdateItemAsync(companyId, branchId, itemId, request, cancellationToken);

    public Task DeactivateItemAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken) =>
        session.Api.DeactivateItemAsync(companyId, branchId, itemId, cancellationToken);

    public Task DeleteItemAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken) =>
        session.Api.DeleteItemAsync(companyId, branchId, itemId, cancellationToken);

    public Task<AdjustItemPricesResponse> AdjustItemPricesAsync(Guid companyId, Guid branchId, AdjustItemPricesRequest request, CancellationToken cancellationToken) =>
        session.Api.AdjustItemPricesAsync(companyId, branchId, request, cancellationToken);

    public Task ReactivateItemAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken) =>
        session.Api.ReactivateItemAsync(companyId, branchId, itemId, cancellationToken);

    public Task<ItemStockSummaryDto> GetItemStockAsync(Guid companyId, Guid branchId, Guid itemId, Guid? warehouseId, CancellationToken cancellationToken) =>
        session.Api.GetItemStockAsync(companyId, branchId, itemId, warehouseId, cancellationToken);

    public Task<IReadOnlyList<ItemGroupDto>> ListItemGroupsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.CachedAsync(branchId, "ItemGroups", $"{companyId:N}",
            ct => session.Api.ListItemGroupsAsync(companyId, branchId, ct),
            TimeSpan.FromMinutes(15), cancellationToken);

    public async Task<ItemGroupDto> CreateItemGroupAsync(Guid companyId, Guid branchId, CreateItemGroupRequest request, CancellationToken cancellationToken)
    {
        var res = await session.Api.CreateItemGroupAsync(companyId, branchId, request, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached("ItemGroups");
        return res;
    }

    public async Task<ItemGroupDto> UpdateItemGroupAsync(Guid companyId, Guid branchId, Guid groupId, UpdateItemGroupRequest request, CancellationToken cancellationToken)
    {
        var res = await session.Api.UpdateItemGroupAsync(companyId, branchId, groupId, request, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached("ItemGroups");
        return res;
    }

    public Task<ItemGroupDetailDto> GetItemGroupAsync(Guid companyId, Guid branchId, Guid groupId, CancellationToken cancellationToken) =>
        session.Api.GetItemGroupAsync(companyId, branchId, groupId, cancellationToken);

    public async Task<bool> DeactivateItemGroupAsync(Guid companyId, Guid branchId, Guid groupId, CancellationToken cancellationToken)
    {
        var res = await session.Api.DeactivateItemGroupAsync(companyId, branchId, groupId, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached("ItemGroups");
        return res;
    }

    public async Task<bool> ReactivateItemGroupAsync(Guid companyId, Guid branchId, Guid groupId, CancellationToken cancellationToken)
    {
        var res = await session.Api.ReactivateItemGroupAsync(companyId, branchId, groupId, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached("ItemGroups");
        return res;
    }

    public async Task<bool> DeleteItemGroupAsync(Guid companyId, Guid branchId, Guid groupId, CancellationToken cancellationToken)
    {
        var res = await session.Api.DeleteItemGroupAsync(companyId, branchId, groupId, cancellationToken).ConfigureAwait(false);
        session.InvalidateCached("ItemGroups");
        return res;
    }

    public Task<IReadOnlyList<ItemAccountMappingDto>> GetAccountMappingsAsync(Guid companyId, Guid branchId, int ownerType, Guid ownerId, CancellationToken cancellationToken) =>
        session.Api.GetAccountMappingsAsync(companyId, branchId, ownerType, ownerId, cancellationToken);

    public Task<bool> SetAccountMappingsAsync(Guid companyId, Guid branchId, SetAccountMappingsRequest request, CancellationToken cancellationToken) =>
        session.Api.SetAccountMappingsAsync(companyId, branchId, request, cancellationToken);

    public Task<EffectiveItemAccountMappingDto> GetEffectiveAccountMappingAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken) =>
        session.Api.GetEffectiveAccountMappingAsync(companyId, branchId, itemId, cancellationToken);

    public Task<IReadOnlyList<UnitOfMeasureDto>> ListUnitsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.Api.ListUnitsAsync(companyId, branchId, cancellationToken);

    public Task<UnitOfMeasureDto> CreateUnitAsync(Guid companyId, Guid branchId, CreateUnitRequest request, CancellationToken cancellationToken) =>
        session.Api.CreateUnitAsync(companyId, branchId, request, cancellationToken);

    public Task<IReadOnlyList<UnitConversionDto>> ListUnitConversionsAsync(Guid companyId, Guid branchId, Guid? itemId, CancellationToken cancellationToken) =>
        session.Api.ListUnitConversionsAsync(companyId, branchId, itemId, cancellationToken);

    public Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(Guid companyId, Guid branchId, Guid? filterBranchId, CancellationToken cancellationToken) =>
        session.Api.ListWarehousesAsync(companyId, branchId, filterBranchId, cancellationToken);

    public Task<StockMovementPageResponse> ListStockMovementsAsync(Guid companyId, Guid branchId, int page, int pageSize, Guid? warehouseId, Guid? itemId, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken cancellationToken) =>
        session.Api.ListStockMovementsAsync(companyId, branchId, page, pageSize, warehouseId, itemId, fromDate, toDate, cancellationToken);

    public Task<StockTransferDto> CreateStockTransferAsync(Guid companyId, Guid branchId, CreateStockTransferRequest request, CancellationToken cancellationToken) =>
        session.Api.CreateStockTransferAsync(companyId, branchId, request, cancellationToken);

    public Task<StockAdjustmentDto> CreateStockAdjustmentAsync(Guid companyId, Guid branchId, CreateStockAdjustmentRequest request, CancellationToken cancellationToken) =>
        session.Api.CreateStockAdjustmentAsync(companyId, branchId, request, cancellationToken);

    public Task<StockAdjustmentDto> ApproveStockAdjustmentAsync(Guid companyId, Guid branchId, Guid adjustmentId, InventoryDocumentActionRequest request, CancellationToken cancellationToken) =>
        session.Api.ApproveStockAdjustmentAsync(companyId, branchId, adjustmentId, request, cancellationToken);

    public Task<StockMovementDto> PostInternalIssueAsync(Guid companyId, Guid branchId, PostInternalIssueRequest request, CancellationToken cancellationToken) =>
        session.Api.PostInternalIssueAsync(companyId, branchId, request, cancellationToken);
}
