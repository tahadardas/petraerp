using Microsoft.AspNetCore.Mvc;

using PetraERP.Api.Errors;

using PetraERP.Api.Security;

using PetraERP.Application.Common;

using PetraERP.Application.Inventory;

using PetraERP.Application.Security;

using PetraERP.Contracts.Financial;

using PetraERP.Contracts.Inventory;

using AppCostMethod = PetraERP.Domain.Inventory.CostMethod;

using AppMovementKind = PetraERP.Domain.Inventory.StockMovementKind;

using AppTransferStatus = PetraERP.Domain.Inventory.StockTransferStatus;

using AppAdjustmentStatus = PetraERP.Domain.Inventory.StockAdjustmentStatus;

using ContractCostMethod = PetraERP.Contracts.Inventory.CostMethod;

using ContractMovementKind = PetraERP.Contracts.Inventory.StockMovementKind;

using ContractTransferStatus = PetraERP.Contracts.Inventory.StockTransferStatus;

using ContractAdjustmentStatus = PetraERP.Contracts.Inventory.StockAdjustmentStatus;



namespace PetraERP.Api.Endpoints;



internal static class InventoryEndpoints

{

    internal static void MapInventoryEndpoints(this RouteGroupBuilder api)

    {

        var inventory = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/inventory")

            .WithTags("Inventory");



        var viewItems = PetraPolicies.Permission(PermissionCatalog.InventoryItemsView);

        var manageItems = PetraPolicies.Permission(PermissionCatalog.InventoryItemsManage);

        var viewWarehouses = PetraPolicies.Permission(PermissionCatalog.InventoryWarehousesView);

        var manageWarehouses = PetraPolicies.Permission(PermissionCatalog.InventoryWarehousesManage);

        var viewMovements = PetraPolicies.Permission(PermissionCatalog.InventoryMovementsView);

        var manageTransfers = PetraPolicies.Permission(PermissionCatalog.InventoryTransfersManage);

        var manageAdjustments = PetraPolicies.Permission(PermissionCatalog.InventoryAdjustmentsManage);



        // Items

        inventory.MapGet("/items", ListItemsAsync)

            .WithName("ListItems")

            .WithSummary("Lists inventory items with tokenized search and filtering.")

            .RequireAuthorization(viewItems)

            .Produces<ItemPageResponse>()

            .ProducesStandardProblems();



        inventory.MapGet("/items/lookup", LookupItemsAsync)

            .WithName("LookupItems")

            .WithSummary("Type-ahead item search by token prefix and barcode.")

            .RequireAuthorization(viewItems)

            .Produces<IReadOnlyList<ItemLookupDto>>()

            .ProducesStandardProblems();



        inventory.MapGet("/items/{itemId:guid}", GetItemAsync)

            .WithName("GetItem")

            .WithSummary("Gets item details including barcodes, prices, and conversions.")

            .RequireAuthorization(viewItems)

            .Produces<ItemDetailDto>()

            .ProducesStandardProblems();



        inventory.MapPost("/items", CreateItemAsync)

            .WithName("CreateItem")

            .WithSummary("Creates a new inventory item with optional barcodes and price lists.")

            .RequireAuthorization(manageItems)

            .Validate<CreateItemRequest>()

            .Produces<ItemDetailDto>(StatusCodes.Status201Created)

            .ProducesStandardProblems();



        inventory.MapPut("/items/{itemId:guid}", UpdateItemAsync)

            .WithName("UpdateItem")

            .WithSummary("Updates item details.")

            .RequireAuthorization(manageItems)

            .Validate<UpdateItemRequest>()

            .Produces<ItemDetailDto>()

            .ProducesStandardProblems();



        inventory.MapDelete("/items/{itemId:guid}", DeleteItemAsync)
            .WithName("DeleteItem")
            .WithSummary("Deletes an item that has no stock movement or document line.")
            .RequireAuthorization(manageItems)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesStandardProblems();
        inventory.MapPost("/price-adjustments", AdjustItemPricesAsync)
            .WithName("AdjustItemPrices")
            .WithSummary("Raises or lowers the general prices of active items by a percentage; preview first.")
            .RequireAuthorization(manageItems)
            .Produces<AdjustItemPricesResponse>()
            .ProducesStandardProblems();

        inventory.MapPost("/items/{itemId:guid}/deactivate", DeactivateItemAsync)

            .WithName("DeactivateItem")

            .WithSummary("Deactivates an item preserving historical movements.")

            .RequireAuthorization(manageItems)

            .Produces(StatusCodes.Status204NoContent)

            .ProducesStandardProblems();



        inventory.MapPost("/items/{itemId:guid}/reactivate", ReactivateItemAsync)

            .WithName("ReactivateItem")

            .WithSummary("Reactivates a deactivated item.")

            .RequireAuthorization(manageItems)

            .Produces(StatusCodes.Status204NoContent)

            .ProducesStandardProblems();



        inventory.MapGet("/items/{itemId:guid}/stock", GetItemStockAsync)

            .WithName("GetItemStock")

            .WithSummary("Calculates current stock and valuation for an item.")

            .RequireAuthorization(viewItems)

            .Produces<ItemStockSummaryDto>()

            .ProducesStandardProblems();



        // Groups

        inventory.MapGet("/groups", ListItemGroupsAsync)

            .WithName("ListItemGroups")

            .WithSummary("Lists all item groups for the company.")

            .RequireAuthorization(viewItems)

            .Produces<IReadOnlyList<ItemGroupDto>>()

            .ProducesStandardProblems();



        inventory.MapPost("/groups", CreateItemGroupAsync)

            .WithName("CreateItemGroup")

            .WithSummary("Creates a new item group.")

            .RequireAuthorization(manageItems)

            .Validate<CreateItemGroupRequest>()

            .Produces<ItemGroupDto>(StatusCodes.Status201Created)

            .ProducesStandardProblems();



        inventory.MapPut("/groups/{groupId:guid}", UpdateItemGroupAsync)

            .WithName("UpdateItemGroup")

            .WithSummary("Updates an item group.")

            .RequireAuthorization(manageItems)

            .Validate<UpdateItemGroupRequest>()

            .Produces<ItemGroupDto>()

            .ProducesStandardProblems();



        inventory.MapGet("/groups/{groupId:guid}", GetItemGroupAsync)

            .WithName("GetItemGroup")

            .WithSummary("Gets item group details including child counts.")

            .RequireAuthorization(viewItems)

            .Produces<ItemGroupDetailDto>()

            .ProducesStandardProblems();



        inventory.MapPost("/groups/{groupId:guid}/deactivate", DeactivateItemGroupAsync)

            .WithName("DeactivateItemGroup")

            .WithSummary("Deactivates an item group.")

            .RequireAuthorization(manageItems)

            .Produces<bool>()

            .ProducesStandardProblems();



        inventory.MapPost("/groups/{groupId:guid}/reactivate", ReactivateItemGroupAsync)

            .WithName("ReactivateItemGroup")

            .WithSummary("Reactivates an item group.")

            .RequireAuthorization(manageItems)

            .Produces<bool>()

            .ProducesStandardProblems();



        inventory.MapDelete("/groups/{groupId:guid}", DeleteItemGroupAsync)

            .WithName("DeleteItemGroup")

            .WithSummary("Deletes an unused item group.")

            .RequireAuthorization(manageItems)

            .Produces<bool>()

            .ProducesStandardProblems();



        inventory.MapGet("/mappings", GetAccountMappingsAsync)

            .WithName("GetAccountMappings")

            .WithSummary("Gets accounting account mappings for an item or item group.")

            .RequireAuthorization(viewItems)

            .Produces<IReadOnlyList<ItemAccountMappingDto>>()

            .ProducesStandardProblems();



        inventory.MapPut("/mappings", SetAccountMappingsAsync)

            .WithName("SetAccountMappings")

            .WithSummary("Sets accounting account mappings for an item or item group.")

            .RequireAuthorization(manageItems)

            .Produces<bool>()

            .ProducesStandardProblems();



        inventory.MapGet("/items/{itemId:guid}/effective-mapping", GetEffectiveAccountMappingAsync)

            .WithName("GetEffectiveAccountMapping")

            .WithSummary("Gets effective account mappings for an item with inheritance traceability.")

            .RequireAuthorization(viewItems)

            .Produces<EffectiveItemAccountMappingDto>()

            .ProducesStandardProblems();



        // Units & Conversions

        inventory.MapGet("/units", ListUnitsAsync)

            .WithName("ListUnits")

            .WithSummary("Lists units of measure.")

            .RequireAuthorization(viewItems)

            .Produces<IReadOnlyList<UnitOfMeasureDto>>()

            .ProducesStandardProblems();



        inventory.MapPost("/units", CreateUnitAsync)

            .WithName("CreateUnit")

            .WithSummary("Creates a unit of measure.")

            .RequireAuthorization(manageItems)

            .Validate<CreateUnitRequest>()

            .Produces<UnitOfMeasureDto>(StatusCodes.Status201Created)

            .ProducesStandardProblems();



        inventory.MapPut("/units/{unitId:guid}", UpdateUnitAsync)

            .WithName("UpdateUnit")

            .WithSummary("Updates a unit of measure.")

            .RequireAuthorization(manageItems)

            .Validate<UpdateUnitRequest>()

            .Produces<UnitOfMeasureDto>()

            .ProducesStandardProblems();



        // Price lists: rows per item, and the one server-side resolver every client uses.

        inventory.MapPost("/items/{itemId:guid}/prices", AddItemPriceAsync)

            .WithName("AddItemPrice")

            .WithSummary("Adds a price row (list, unit, enabled currency, optional customer, pattern, quantity break and validity window).")

            .RequireAuthorization(manageItems)

            .Validate<CreateItemPriceRequest>()

            .Produces<ItemPriceDto>(StatusCodes.Status201Created)

            .ProducesStandardProblems();



        inventory.MapDelete("/items/{itemId:guid}/prices/{priceId:guid}", DeleteItemPriceAsync)

            .WithName("DeleteItemPrice")

            .WithSummary("Removes a price row. Documents keep the price they copied.")

            .RequireAuthorization(manageItems)

            .Produces(StatusCodes.Status204NoContent)

            .ProducesStandardProblems();



        inventory.MapGet("/items/{itemId:guid}/price-quote", QuotePriceAsync)

            .WithName("QuoteItemPrice")

            .WithSummary("Resolves the applicable price for a document line by the server priority; found=false when none applies.")

            .RequireAuthorization(viewItems)

            .Produces<PriceQuoteResponse>()

            .ProducesStandardProblems();



        inventory.MapGet("/conversions", ListConversionsAsync)

            .WithName("ListUnitConversions")

            .WithSummary("Lists unit conversions.")

            .RequireAuthorization(viewItems)

            .Produces<IReadOnlyList<UnitConversionDto>>()

            .ProducesStandardProblems();



        inventory.MapPost("/conversions", CreateConversionAsync)

            .WithName("CreateUnitConversion")

            .WithSummary("Creates a unit conversion ratio.")

            .RequireAuthorization(manageItems)

            .Validate<CreateUnitConversionRequest>()

            .Produces<UnitConversionDto>(StatusCodes.Status201Created)

            .ProducesStandardProblems();



        // Warehouses

        inventory.MapGet("/warehouses", ListWarehousesAsync)

            .WithName("ListWarehouses")

            .WithSummary("Lists warehouses for the company or branch.")

            .RequireAuthorization(viewWarehouses)

            .Produces<IReadOnlyList<WarehouseDto>>()

            .ProducesStandardProblems();



        inventory.MapPost("/warehouses", CreateWarehouseAsync)

            .WithName("CreateWarehouse")

            .WithSummary("Creates a new warehouse.")

            .RequireAuthorization(manageWarehouses)

            .Validate<CreateWarehouseRequest>()

            .Produces<WarehouseDto>(StatusCodes.Status201Created)

            .ProducesStandardProblems();



        inventory.MapPut("/warehouses/{warehouseId:guid}", UpdateWarehouseAsync)

            .WithName("UpdateWarehouse")

            .WithSummary("Updates a warehouse.")

            .RequireAuthorization(manageWarehouses)

            .Validate<UpdateWarehouseRequest>()

            .Produces<WarehouseDto>()

            .ProducesStandardProblems();



        // Movements

        inventory.MapGet("/movements", ListMovementsAsync)

            .WithName("ListStockMovements")

            .WithSummary("Lists stock ledger movements with date and warehouse filtering.")

            .RequireAuthorization(viewMovements)

            .Produces<StockMovementPageResponse>()

            .ProducesStandardProblems();



        // Transfers

        inventory.MapPost("/transfers", CreateTransferAsync)

            .WithName("CreateStockTransfer")

            .WithSummary("Executes an atomic warehouse transfer with paired TransferOut and TransferIn movements.")

            .RequireAuthorization(manageTransfers).RequireAuthorization(viewMovements)

            .Validate<CreateStockTransferRequest>()

            .Produces<StockTransferDto>(StatusCodes.Status201Created)

            .ProducesStandardProblems();



        // Adjustments

        inventory.MapPost("/adjustments", CreateAdjustmentAsync)

            .WithName("CreateStockAdjustment")

            .WithSummary("Creates a stock adjustment draft with counted quantities and mandatory reason.")

            .RequireAuthorization(manageAdjustments).RequireAuthorization(viewMovements)

            .Validate<CreateStockAdjustmentRequest>()

            .Produces<StockAdjustmentDto>(StatusCodes.Status201Created)

            .ProducesStandardProblems();



        inventory.MapPost("/adjustments/{adjustmentId:guid}/approve", ApproveAdjustmentAsync)

            .WithName("ApproveStockAdjustment")

            .WithSummary("Approves an adjustment and posts difference movements to the stock ledger.")

            .RequireAuthorization(manageAdjustments).RequireAuthorization(viewMovements)

            .Validate<InventoryDocumentActionRequest>()

            .Produces<StockAdjustmentDto>()

            .ProducesStandardProblems();



        // Internal Issues

        inventory.MapPost("/internal-issues", PostInternalIssueAsync)

            .WithName("PostInternalIssue")

            .WithSummary("Posts an internal stock issue (e.g. diesel consumption) requiring a CostCenterId.")

            .RequireAuthorization(manageAdjustments).RequireAuthorization(viewMovements)

            .Validate<PostInternalIssueRequest>()

            .Produces<StockMovementDto>(StatusCodes.Status201Created)

            .ProducesStandardProblems();

    }



    // ==========================================

    // Items Handlers

    // ==========================================



    private static async Task<IResult> ListItemsAsync(

        Guid companyId,

        Guid branchId,

        [FromQuery] string? search,

        [FromQuery] Guid? groupId,

        [FromQuery] Guid? warehouseId,

        [FromQuery] bool? isActive,

        [FromQuery] int page,

        [FromQuery] int pageSize,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var query = new ItemSearchQuery(

            SearchText: search,

            GroupId: groupId,

            WarehouseId: warehouseId,

            IsActive: isActive,

            Page: page <= 0 ? 1 : page,

            PageSize: pageSize <= 0 ? 50 : pageSize) { BranchId = branchId };



        var result = await service.GetItemsAsync(companyId, query, cancellationToken).ConfigureAwait(false);



        var response = new ItemPageResponse

        {

            Page = result.Page,

            PageSize = result.PageSize,

            TotalCount = result.TotalCount,

            Items = result.Items.Select(MapItemSummary).ToList()

        };



        return TypedResults.Ok(response);

    }



    private static async Task<IResult> LookupItemsAsync(

        Guid companyId,

        Guid branchId,

        [FromQuery] string? search,

        [FromQuery] int maxResults,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var limit = maxResults <= 0 ? 20 : maxResults;

        var items = await service.LookupItemsInBranchAsync(companyId, search, limit, branchId, cancellationToken).ConfigureAwait(false);

        var dtos = items.Select(MapItemLookup).ToList();

        return TypedResults.Ok((IReadOnlyList<ItemLookupDto>)dtos);

    }



    private static async Task<IResult> GetItemAsync(

        Guid companyId,

        Guid branchId,

        Guid itemId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var item = await service.GetItemInBranchAsync(companyId, itemId, branchId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(MapItemDetail(item));

    }



    private static async Task<IResult> CreateItemAsync(

        Guid companyId,

        Guid branchId,

        [FromBody] CreateItemRequest request,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var command = new CreateItemCommand(

            Code: request.Code,

            NameArabic: request.NameArabic,

            NameEnglish: request.NameEnglish,

            GroupId: request.GroupId,

            BaseUnitId: request.BaseUnitId,

            DefaultWarehouseId: request.DefaultWarehouseId,

            CostMethod: (AppCostMethod)request.CostMethod,

            MinStock: ParseNullableDecimal(request.MinStock),

            MaxStock: ParseNullableDecimal(request.MaxStock),

            ReorderPoint: ParseNullableDecimal(request.ReorderPoint),

            Barcodes: request.Barcodes?.Select(b => new CreateItemBarcodeCommand(b.UnitId, b.Barcode, b.IsDefault)).ToList(),

            Prices: request.Prices?.Select(ToPriceCommand).ToList(),

            Profile: ItemProfileMapping.ToData(request.Profile));



        var item = await service.CreateItemInBranchAsync(companyId, command, branchId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(

            $"/api/v1/companies/{companyId}/branches/{branchId}/inventory/items/{item.Id}",

            MapItemDetail(item));

    }



    private static async Task<IResult> UpdateItemAsync(

        Guid companyId,

        Guid branchId,

        Guid itemId,

        [FromBody] UpdateItemRequest request,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var command = new UpdateItemCommand(

            NameArabic: request.NameArabic,

            NameEnglish: request.NameEnglish,

            GroupId: request.GroupId,

            BaseUnitId: request.BaseUnitId,

            DefaultWarehouseId: request.DefaultWarehouseId,

            CostMethod: (AppCostMethod)request.CostMethod,

            MinStock: ParseNullableDecimal(request.MinStock),

            MaxStock: ParseNullableDecimal(request.MaxStock),

            ReorderPoint: ParseNullableDecimal(request.ReorderPoint),

            Profile: ItemProfileMapping.ToData(request.Profile),

            Barcodes: request.Barcodes?.Select(b => new CreateItemBarcodeCommand(b.UnitId, b.Barcode, b.IsDefault)).ToList());



        var item = await service.UpdateItemInBranchAsync(companyId, itemId, command, branchId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(MapItemDetail(item));

    }



    private static async Task<IResult> DeleteItemAsync(
        Guid companyId,
        Guid branchId,
        Guid itemId,
        IInventoryService service,
        CancellationToken cancellationToken)
    {
        await service.DeleteItemAsync(companyId, itemId, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> AdjustItemPricesAsync(
        Guid companyId,
        Guid branchId,
        AdjustItemPricesRequest request,
        IInventoryService service,
        CancellationToken cancellationToken)
    {
        if (!FinancialDecimalText.TryParse(request.Percent, 4, 4, out var percent))
        {
            throw new RequestValidationException("inventory.price_adjust_percent", "النسبة غير صالحة / The percentage is not valid.", "percent");
        }

        var result = await service.AdjustItemPricesAsync(companyId,
            new ItemPriceAdjustmentCommand(request.GroupId, request.PriceListCode, percent.Value, request.RoundingScale, request.PreviewOnly), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new AdjustItemPricesResponse { ItemCount = result.ItemCount, RowCount = result.RowCount, Applied = result.Applied });
    }

    private static async Task<IResult> DeactivateItemAsync(

        Guid companyId,

        Guid branchId,

        Guid itemId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        await service.DeactivateItemAsync(companyId, itemId, cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();

    }



    private static async Task<IResult> ReactivateItemAsync(

        Guid companyId,

        Guid branchId,

        Guid itemId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        await service.ReactivateItemAsync(companyId, itemId, cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();

    }



    private static async Task<IResult> GetItemStockAsync(

        Guid companyId,

        Guid branchId,

        Guid itemId,

        [FromQuery] Guid? warehouseId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var stock = await service.GetItemStockInBranchAsync(companyId, itemId, warehouseId, branchId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(MapItemStockSummary(stock));

    }



    // ==========================================

    // Groups Handlers

    // ==========================================



    private static async Task<IResult> ListItemGroupsAsync(

        Guid companyId,

        Guid branchId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var groups = await service.GetItemGroupsAsync(companyId, cancellationToken).ConfigureAwait(false);

        var dtos = groups.Select(MapItemGroup).ToList();

        return TypedResults.Ok((IReadOnlyList<ItemGroupDto>)dtos);

    }



    private static async Task<IResult> CreateItemGroupAsync(

        Guid companyId,

        Guid branchId,

        [FromBody] CreateItemGroupRequest request,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var command = new CreateItemGroupCommand(request.Code, request.NameArabic, request.NameEnglish, request.ParentId, request.Description);

        var group = await service.CreateItemGroupAsync(companyId, command, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(

            $"/api/v1/companies/{companyId}/branches/{branchId}/inventory/groups/{group.Id}",

            MapItemGroup(group));

    }



    private static async Task<IResult> UpdateItemGroupAsync(

        Guid companyId,

        Guid branchId,

        Guid groupId,

        [FromBody] UpdateItemGroupRequest request,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var command = new UpdateItemGroupCommand(request.NameArabic, request.NameEnglish, request.ParentId, request.Description);

        var group = await service.UpdateItemGroupAsync(companyId, groupId, command, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(MapItemGroup(group));

    }



    private static async Task<IResult> GetItemGroupAsync(

        Guid companyId,

        Guid branchId,

        Guid groupId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var res = await service.GetItemGroupAsync(companyId, groupId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new ItemGroupDetailDto

        {

            Id = res.Id,

            CompanyId = res.CompanyId,

            ParentId = res.ParentId,

            ParentNameArabic = res.ParentNameArabic,

            Code = res.Code,

            NameArabic = res.NameArabic,

            NameEnglish = res.NameEnglish,

            Description = res.Description,

            IsActive = res.IsActive,

            SubgroupsCount = res.SubgroupsCount,

            ItemsCount = res.ItemsCount,

            CreatedAtUtc = res.CreatedAtUtc,

            UpdatedAtUtc = res.UpdatedAtUtc,

            RowVersion = res.RowVersion

        });

    }



    private static async Task<IResult> DeactivateItemGroupAsync(

        Guid companyId,

        Guid branchId,

        Guid groupId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var success = await service.DeactivateItemGroupAsync(companyId, groupId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(success);

    }



    private static async Task<IResult> ReactivateItemGroupAsync(

        Guid companyId,

        Guid branchId,

        Guid groupId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var success = await service.ReactivateItemGroupAsync(companyId, groupId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(success);

    }



    private static async Task<IResult> DeleteItemGroupAsync(

        Guid companyId,

        Guid branchId,

        Guid groupId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var success = await service.DeleteItemGroupAsync(companyId, groupId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(success);

    }



    private static async Task<IResult> GetAccountMappingsAsync(

        Guid companyId,

        Guid branchId,

        [FromQuery] int ownerType,

        [FromQuery] Guid ownerId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var mappings = await service.GetAccountMappingsAsync(companyId, ownerType, ownerId, cancellationToken).ConfigureAwait(false);

        var dtos = mappings.Select(m => new ItemAccountMappingDto

        {

            Id = m.Id,

            CompanyId = m.CompanyId,

            OwnerType = m.OwnerType,

            OwnerId = m.OwnerId,

            Role = m.Role,

            AccountId = m.AccountId,

            AccountCode = m.AccountCode,

            AccountNameArabic = m.AccountNameArabic,

            IsActive = m.IsActive

        }).ToList();

        return TypedResults.Ok((IReadOnlyList<ItemAccountMappingDto>)dtos);

    }



    private static async Task<IResult> SetAccountMappingsAsync(

        Guid companyId,

        Guid branchId,

        [FromBody] SetAccountMappingsRequest request,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var commands = request.Mappings.Select(m => new SetItemAccountMappingCommand(m.Role, m.AccountId)).ToList();

        await service.SetAccountMappingsAsync(companyId, request.OwnerType, request.OwnerId, commands, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(true);

    }



    private static async Task<IResult> GetEffectiveAccountMappingAsync(

        Guid companyId,

        Guid branchId,

        Guid itemId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var res = await service.GetEffectiveAccountMappingAsync(companyId, itemId, cancellationToken).ConfigureAwait(false);

        var dto = new EffectiveItemAccountMappingDto

        {

            ItemId = res.ItemId,

            ItemCode = res.ItemCode,

            ItemNameArabic = res.ItemNameArabic,

            GroupId = res.GroupId,

            GroupNameArabic = res.GroupNameArabic,

            Accounts = res.Accounts.Select(r => new EffectiveAccountDetailDto

            {

                Role = r.Role,

                AccountId = r.AccountId,

                AccountCode = r.AccountCode,

                AccountNameArabic = r.AccountNameArabic,

                Source = r.Source,

                SourceDescription = r.SourceDescription

            }).ToList()

        };

        return TypedResults.Ok(dto);

    }



    // ==========================================

    // Units & Conversions Handlers

    // ==========================================



    private static async Task<IResult> ListUnitsAsync(

        Guid companyId,

        Guid branchId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var units = await service.GetUnitsAsync(companyId, cancellationToken).ConfigureAwait(false);

        var dtos = units.Select(MapUnitOfMeasure).ToList();

        return TypedResults.Ok((IReadOnlyList<UnitOfMeasureDto>)dtos);

    }



    private static async Task<IResult> CreateUnitAsync(

        Guid companyId,

        Guid branchId,

        [FromBody] CreateUnitRequest request,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var command = new CreateUnitCommand(request.Code, request.NameArabic, request.NameEnglish, request.Symbol, request.DecimalPlaces,

            (PetraERP.Domain.Inventory.UnitDimension)request.Dimension);

        var unit = await service.CreateUnitAsync(companyId, command, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(

            $"/api/v1/companies/{companyId}/branches/{branchId}/inventory/units/{unit.Id}",

            MapUnitOfMeasure(unit));

    }



    private static async Task<IResult> UpdateUnitAsync(

        Guid companyId,

        Guid branchId,

        Guid unitId,

        [FromBody] UpdateUnitRequest request,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var command = new UpdateUnitCommand(request.NameArabic, request.NameEnglish, request.Symbol, request.DecimalPlaces,

            request.Dimension is { } dimension ? (PetraERP.Domain.Inventory.UnitDimension)dimension : null);

        var unit = await service.UpdateUnitAsync(companyId, unitId, command, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(MapUnitOfMeasure(unit));

    }



    private static async Task<IResult> ListConversionsAsync(

        Guid companyId,

        Guid branchId,

        [FromQuery] Guid? itemId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var conversions = await service.GetUnitConversionsAsync(companyId, itemId, cancellationToken).ConfigureAwait(false);

        var dtos = conversions.Select(MapUnitConversion).ToList();

        return TypedResults.Ok((IReadOnlyList<UnitConversionDto>)dtos);

    }



    private static async Task<IResult> CreateConversionAsync(

        Guid companyId,

        Guid branchId,

        [FromBody] CreateUnitConversionRequest request,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var command = new CreateUnitConversionCommand(request.ItemId, request.FromUnitId, request.ToUnitId, ParseDecimal(request.Factor, 1m));

        var conversion = await service.CreateUnitConversionAsync(companyId, command, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(

            $"/api/v1/companies/{companyId}/branches/{branchId}/inventory/conversions/{conversion.Id}",

            MapUnitConversion(conversion));

    }



    // ==========================================

    // Warehouses Handlers

    // ==========================================



    private static async Task<IResult> ListWarehousesAsync(

        Guid companyId,

        Guid branchId,

        [FromQuery] Guid? filterBranchId,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var targetBranch = filterBranchId ?? branchId;

        if (targetBranch != branchId) throw new RequestValidationException("inventory.branch_scope", "الفرع مختلف عن نطاق الطلب / Branch differs from request scope.", "filterBranchId");

        var warehouses = await service.GetWarehousesAsync(companyId, targetBranch, cancellationToken).ConfigureAwait(false);

        var dtos = warehouses.Select(MapWarehouse).ToList();

        return TypedResults.Ok((IReadOnlyList<WarehouseDto>)dtos);

    }



    private static async Task<IResult> CreateWarehouseAsync(

        Guid companyId,

        Guid branchId,

        [FromBody] CreateWarehouseRequest request,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var effectiveBranchId = request.BranchId == Guid.Empty ? branchId : request.BranchId;

        if (effectiveBranchId != branchId) throw new RequestValidationException("inventory.branch_scope", "الفرع مختلف عن نطاق الطلب / Branch differs from request scope.", "branchId");

        var command = new CreateWarehouseCommand(effectiveBranchId, request.Code, request.NameArabic, request.NameEnglish, request.Location);

        var warehouse = await service.CreateWarehouseAsync(companyId, command, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(

            $"/api/v1/companies/{companyId}/branches/{branchId}/inventory/warehouses/{warehouse.Id}",

            MapWarehouse(warehouse));

    }



    private static async Task<IResult> UpdateWarehouseAsync(

        Guid companyId,

        Guid branchId,

        Guid warehouseId,

        [FromBody] UpdateWarehouseRequest request,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var command = new UpdateWarehouseCommand(request.NameArabic, request.NameEnglish, request.Location);

        var warehouse = await service.UpdateWarehouseInBranchAsync(companyId, warehouseId, command, branchId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(MapWarehouse(warehouse));

    }



    // ==========================================

    // Movements Handlers

    // ==========================================



    private static async Task<IResult> ListMovementsAsync(

        Guid companyId,

        Guid branchId,

        [FromQuery] Guid? warehouseId,

        [FromQuery] Guid? itemId,

        [FromQuery] DateTimeOffset? fromDate,

        [FromQuery] DateTimeOffset? toDate,

        [FromQuery] int page,

        [FromQuery] int pageSize,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var query = new StockMovementQuery(

            WarehouseId: warehouseId,

            ItemId: itemId,

            FromDateUtc: fromDate,

            ToDateUtc: toDate,

            Page: page <= 0 ? 1 : page,

            PageSize: pageSize <= 0 ? 50 : pageSize) { BranchId = branchId };



        var result = await service.GetStockMovementsAsync(companyId, query, cancellationToken).ConfigureAwait(false);



        var response = new StockMovementPageResponse

        {

            Page = result.Page,

            PageSize = result.PageSize,

            TotalCount = result.TotalCount,

            Items = result.Items.Select(MapStockMovement).ToList()

        };



        return TypedResults.Ok(response);

    }



    // ==========================================

    // Transfers Handlers

    // ==========================================



    private static async Task<IResult> CreateTransferAsync(

        Guid companyId,

        Guid branchId,

        [FromBody] CreateStockTransferRequest request,

        IInventoryService service,

        HttpContext httpContext,

        CancellationToken cancellationToken)

    {

        var userId = httpContext.RequireSession().UserId;

        var command = new CreateStockTransferCommand(

            TransferNumber: request.TransferNumber,

            SourceWarehouseId: request.SourceWarehouseId,

            TargetBranchId: request.TargetBranchId,

            TargetWarehouseId: request.TargetWarehouseId,

            TransferDateUtc: request.TransferDateUtc,

            Notes: request.Notes,

            Lines: request.Lines.Select(l => new CreateStockTransferLineCommand(l.ItemId, l.UnitId, ParseDecimal(l.Quantity), 0m)).ToList()) { IdempotencyKey = request.IdempotencyKey };



        var transfer = await service.CreateStockTransferAsync(companyId, branchId, userId, command, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(

            $"/api/v1/companies/{companyId}/branches/{branchId}/inventory/transfers/{transfer.Id}",

            MapStockTransfer(transfer));

    }



    // ==========================================

    // Adjustments Handlers

    // ==========================================



    private static async Task<IResult> CreateAdjustmentAsync(

        Guid companyId,

        Guid branchId,

        [FromBody] CreateStockAdjustmentRequest request,

        IInventoryService service,

        HttpContext httpContext,

        CancellationToken cancellationToken)

    {

        var userId = httpContext.RequireSession().UserId;

        var command = new CreateStockAdjustmentCommand(

            AdjustmentNumber: request.AdjustmentNumber,

            WarehouseId: request.WarehouseId,

            AdjustmentDateUtc: request.AdjustmentDateUtc,

            Reason: request.Reason,

            Notes: request.Notes,

            Lines: request.Lines.Select(l => new CreateStockAdjustmentLineCommand(l.ItemId, l.UnitId, ParseDecimal(l.CountedQuantity), 0m)).ToList())

            { InventoryAccountId = request.InventoryAccountId, OffsetAccountId = request.OffsetAccountId };



        var adjustment = await service.CreateStockAdjustmentAsync(companyId, branchId, userId, command, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(

            $"/api/v1/companies/{companyId}/branches/{branchId}/inventory/adjustments/{adjustment.Id}",

            MapStockAdjustment(adjustment));

    }



    private static async Task<IResult> ApproveAdjustmentAsync(

        Guid companyId,

        Guid branchId,

        Guid adjustmentId,

        [FromBody] InventoryDocumentActionRequest request,

        IInventoryService service,

        HttpContext httpContext,

        CancellationToken cancellationToken)

    {

        var userId = httpContext.RequireSession().UserId;

        var adjustment = await service.ApproveStockAdjustmentAsync(companyId, branchId, adjustmentId, userId,

            new InventoryDocumentAction(request.ExpectedVersion, request.IdempotencyKey, request.Reason), cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(MapStockAdjustment(adjustment));

    }



    // ==========================================

    // Internal Issues Handlers

    // ==========================================



    private static async Task<IResult> PostInternalIssueAsync(

        Guid companyId,

        Guid branchId,

        [FromBody] PostInternalIssueRequest request,

        IInventoryService service,

        HttpContext httpContext,

        CancellationToken cancellationToken)

    {

        var userId = httpContext.RequireSession().UserId;

        var command = new PostInternalIssueCommand(

            WarehouseId: request.WarehouseId,

            ItemId: request.ItemId,

            UnitId: request.UnitId,

            Quantity: ParseDecimal(request.Quantity),

            UnitCost: 0m,

            CostCenterId: request.CostCenterId,

            DocumentNumber: request.DocumentNumber,

            Notes: request.Notes) { IdempotencyKey = request.IdempotencyKey, InventoryAccountId = request.InventoryAccountId, OffsetAccountId = request.OffsetAccountId };



        var movement = await service.PostInternalIssueAsync(companyId, branchId, userId, command, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(

            $"/api/v1/companies/{companyId}/branches/{branchId}/inventory/movements/{movement.Id}",

            MapStockMovement(movement));

    }



    // ==========================================

    // Mapping Helpers

    // ==========================================



    private static ItemSummaryDto MapItemSummary(ItemSummaryResult r) =>

        new()

        {

            Id = r.Id,

            CompanyId = r.CompanyId,

            GroupId = r.GroupId,

            GroupName = r.GroupName,

            Code = r.Code,

            NameArabic = r.NameArabic,

            NameEnglish = r.NameEnglish,

            BaseUnitId = r.BaseUnitId,

            BaseUnitCode = r.BaseUnitCode,

            BaseUnitSymbol = r.BaseUnitSymbol,

            CostMethod = (ContractCostMethod)r.CostMethod,

            MinStock = r.MinStock.HasValue ? FinancialDecimalText.Format(r.MinStock.Value, FinancialDecimalText.MoneyScale) : null,

            MaxStock = r.MaxStock.HasValue ? FinancialDecimalText.Format(r.MaxStock.Value, FinancialDecimalText.MoneyScale) : null,

            ReorderPoint = r.ReorderPoint.HasValue ? FinancialDecimalText.Format(r.ReorderPoint.Value, FinancialDecimalText.MoneyScale) : null,

            IsActive = r.IsActive,

            CurrentStock = FinancialDecimalText.Format(r.CurrentStock, FinancialDecimalText.MoneyScale)

        };



    private static ItemDetailDto MapItemDetail(ItemDetailResult r) =>

        new()

        {

            Id = r.Id,

            CompanyId = r.CompanyId,

            GroupId = r.GroupId,

            Code = r.Code,

            NameArabic = r.NameArabic,

            NameEnglish = r.NameEnglish,

            BaseUnitId = r.BaseUnitId,

            DefaultWarehouseId = r.DefaultWarehouseId,

            CostMethod = (ContractCostMethod)r.CostMethod,

            MinStock = r.MinStock.HasValue ? FinancialDecimalText.Format(r.MinStock.Value, FinancialDecimalText.MoneyScale) : null,

            MaxStock = r.MaxStock.HasValue ? FinancialDecimalText.Format(r.MaxStock.Value, FinancialDecimalText.MoneyScale) : null,

            ReorderPoint = r.ReorderPoint.HasValue ? FinancialDecimalText.Format(r.ReorderPoint.Value, FinancialDecimalText.MoneyScale) : null,

            IsActive = r.IsActive,

            CurrentStock = FinancialDecimalText.Format(r.CurrentStock, FinancialDecimalText.MoneyScale),

            Barcodes = r.Barcodes.Select(b => new ItemBarcodeDto

            {

                Id = b.Id,

                ItemId = b.ItemId,

                UnitId = b.UnitId,

                UnitCode = b.UnitCode,

                Barcode = b.Barcode,

                IsDefault = b.IsDefault

            }).ToList(),

            Prices = r.Prices.Select(p => new ItemPriceDto

            {

                Id = p.Id,

                ItemId = p.ItemId,

                UnitId = p.UnitId,

                UnitCode = p.UnitCode,

                PriceListCode = p.PriceListCode,

                Price = FinancialDecimalText.Format(p.Price, FinancialDecimalText.MoneyScale),

                CurrencyCode = p.CurrencyCode,

                MinQuantity = FinancialDecimalText.Format(p.MinQuantity, FinancialDecimalText.MoneyScale),

                PartyId = p.PartyId,

                PatternId = p.PatternId,

                ValidFrom = p.ValidFrom,

                ValidTo = p.ValidTo

            }).ToList(),

            Conversions = r.Conversions.Select(MapUnitConversion).ToList(),

            Profile = ItemProfileMapping.ToDto(r.Profile)

        };



    private static ItemLookupDto MapItemLookup(ItemLookupResult r) =>

        new()

        {

            Id = r.Id,

            Code = r.Code,

            NameArabic = r.NameArabic,

            NameEnglish = r.NameEnglish,

            BaseUnitId = r.BaseUnitId,

            BaseUnitCode = r.BaseUnitCode,

            BaseUnitSymbol = r.BaseUnitSymbol,

            CurrentStock = FinancialDecimalText.Format(r.CurrentStock, FinancialDecimalText.MoneyScale)

        };



    private static ItemGroupDto MapItemGroup(ItemGroupResult r) =>

        new()

        {

            Id = r.Id,

            CompanyId = r.CompanyId,

            ParentId = r.ParentId,

            Code = r.Code,

            NameArabic = r.NameArabic,

            NameEnglish = r.NameEnglish,

            Description = r.Description,

            IsActive = r.IsActive

        };



    private static UnitOfMeasureDto MapUnitOfMeasure(UnitOfMeasureResult r) =>

        new()

        {

            Id = r.Id,

            CompanyId = r.CompanyId,

            Code = r.Code,

            NameArabic = r.NameArabic,

            NameEnglish = r.NameEnglish,

            Symbol = r.Symbol,

            DecimalPlaces = r.DecimalPlaces,

            IsActive = r.IsActive,

            Dimension = (UnitDimension)r.Dimension

        };



    private static UnitConversionDto MapUnitConversion(UnitConversionResult r) =>

        new()

        {

            Id = r.Id,

            CompanyId = r.CompanyId,

            ItemId = r.ItemId,

            FromUnitId = r.FromUnitId,

            FromUnitCode = r.FromUnitCode,

            FromUnitSymbol = r.FromUnitSymbol,

            ToUnitId = r.ToUnitId,

            ToUnitCode = r.ToUnitCode,

            ToUnitSymbol = r.ToUnitSymbol,

            Factor = FinancialDecimalText.Format(r.Factor, FinancialDecimalText.MoneyScale)

        };



    private static WarehouseDto MapWarehouse(WarehouseResult r) =>

        new()

        {

            Id = r.Id,

            CompanyId = r.CompanyId,

            BranchId = r.BranchId,

            Code = r.Code,

            NameArabic = r.NameArabic,

            NameEnglish = r.NameEnglish,

            Location = r.Location,

            IsActive = r.IsActive

        };



    private static ItemStockSummaryDto MapItemStockSummary(ItemStockSummaryResult r) =>

        new()

        {

            ItemId = r.ItemId,

            BaseUnitSymbol = r.BaseUnitSymbol,

            TotalQuantity = FinancialDecimalText.Format(r.TotalStock, FinancialDecimalText.MoneyScale),

            Warehouses = r.Warehouses.Select(w => new WarehouseStockDto

            {

                WarehouseId = w.WarehouseId,

                WarehouseCode = w.WarehouseCode,

                WarehouseName = w.WarehouseName,

                Quantity = FinancialDecimalText.Format(w.Stock, FinancialDecimalText.MoneyScale),

                UnitCost = FinancialDecimalText.Format(w.Stock != 0 ? decimal.Round(w.Value / w.Stock, 4, MidpointRounding.ToEven) : 0m, FinancialDecimalText.MoneyScale),

                TotalCost = FinancialDecimalText.Format(w.Value, FinancialDecimalText.MoneyScale)

            }).ToList()

        };



    private static StockMovementDto MapStockMovement(StockMovementResult r) =>

        new()

        {

            Id = r.Id,

            WarehouseId = r.WarehouseId,

            WarehouseCode = r.WarehouseCode,

            ItemId = r.ItemId,

            ItemCode = r.ItemCode,

            ItemName = r.ItemName,

            UnitId = r.UnitId,

            UnitCode = r.UnitCode,

            MovementKind = (ContractMovementKind)r.MovementKind,

            Quantity = FinancialDecimalText.Format(r.Quantity, FinancialDecimalText.MoneyScale),

            BaseQuantity = FinancialDecimalText.Format(r.BaseQuantity, FinancialDecimalText.MoneyScale),

            UnitCost = FinancialDecimalText.Format(r.UnitCost, FinancialDecimalText.MoneyScale),

            TotalCost = FinancialDecimalText.Format(r.TotalCost, FinancialDecimalText.MoneyScale),

            MovementDateUtc = r.MovementDateUtc,

            DocumentType = r.DocumentType,

            DocumentNumber = r.DocumentNumber,

            CostCenterId = r.CostCenterId,

            CostCenterCode = r.CostCenterCode,

            Notes = r.Notes

        };



    private static StockTransferDto MapStockTransfer(StockTransferResult r) =>

        new()

        {

            Id = r.Id,

            TransferNumber = r.TransferNumber,

            SourceBranchId = r.SourceBranchId,

            SourceWarehouseId = r.SourceWarehouseId,

            TargetBranchId = r.TargetBranchId,

            TargetWarehouseId = r.TargetWarehouseId,

            TransferDateUtc = r.TransferDateUtc,

            Status = (ContractTransferStatus)r.Status,

            Notes = r.Notes,

            Lines = r.Lines.Select(l => new StockTransferLineDto

            {

                Id = l.Id,

                ItemId = l.ItemId,

                ItemCode = l.ItemCode,

                ItemName = l.ItemName,

                UnitId = l.UnitId,

                UnitCode = l.UnitCode,

                Quantity = FinancialDecimalText.Format(l.Quantity, FinancialDecimalText.MoneyScale),

                BaseQuantity = FinancialDecimalText.Format(l.BaseQuantity, FinancialDecimalText.MoneyScale),

                UnitCost = FinancialDecimalText.Format(l.UnitCost, FinancialDecimalText.MoneyScale),

                TotalCost = FinancialDecimalText.Format(l.TotalCost, FinancialDecimalText.MoneyScale)

            }).ToList()

        };



    private static StockAdjustmentDto MapStockAdjustment(StockAdjustmentResult r) =>

        new()

        {

            Version = r.Version,

            Id = r.Id,

            AdjustmentNumber = r.AdjustmentNumber,

            BranchId = r.BranchId,

            WarehouseId = r.WarehouseId,

            AdjustmentDateUtc = r.AdjustmentDateUtc,

            Reason = r.Reason,

            Status = (ContractAdjustmentStatus)r.Status,

            ApprovedByUserId = r.ApprovedByUserId,

            ApprovedAtUtc = r.ApprovedAtUtc,

            Notes = r.Notes,

            Lines = r.Lines.Select(l => new StockAdjustmentLineDto

            {

                Id = l.Id,

                ItemId = l.ItemId,

                ItemCode = l.ItemCode,

                ItemName = l.ItemName,

                UnitId = l.UnitId,

                UnitCode = l.UnitCode,

                SystemQuantity = FinancialDecimalText.Format(l.SystemQuantity, FinancialDecimalText.MoneyScale),

                CountedQuantity = FinancialDecimalText.Format(l.CountedQuantity, FinancialDecimalText.MoneyScale),

                DifferenceQuantity = FinancialDecimalText.Format(l.DifferenceQuantity, FinancialDecimalText.MoneyScale),

                UnitCost = FinancialDecimalText.Format(l.UnitCost, FinancialDecimalText.MoneyScale)

            }).ToList()

        };



    private static async Task<IResult> AddItemPriceAsync(

        Guid companyId,

        Guid branchId,

        Guid itemId,

        [FromBody] CreateItemPriceRequest request,

        IInventoryService service,

        CancellationToken cancellationToken)

    {

        var p = await service.AddItemPriceAsync(companyId, itemId, ToPriceCommand(request), cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/companies/{companyId}/branches/{branchId}/inventory/items/{itemId}/prices/{p.Id}", MapPrice(p));

    }



    private static async Task<IResult> DeleteItemPriceAsync(Guid companyId, Guid branchId, Guid itemId, Guid priceId, IInventoryService service,

        CancellationToken cancellationToken)

    {

        await service.DeleteItemPriceAsync(companyId, itemId, priceId, cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();

    }



    private static async Task<IResult> QuotePriceAsync(Guid companyId, Guid branchId, Guid itemId, Guid? unitId, string? quantity, DateOnly? date,

        string? currencyCode, Guid? partyId, Guid? patternRevisionId, string? priceListCode, IInventoryService service, CancellationToken cancellationToken)

    {

        if (unitId is null || date is null)

            throw new PetraERP.Application.Common.RequestValidationException("inventory.quote_invalid", "'unitId' and 'date' are required.", "unitId");

        var amount = 0m;

        if (!string.IsNullOrEmpty(quantity))

        {

            if (!FinancialDecimalText.TryParse(quantity, FinancialDecimalText.MoneyScale, 15, out var parsed) || parsed.Value < 0)

                throw new PetraERP.Application.Common.RequestValidationException("inventory.quantity_precision", "Quantity must be a non-negative decimal string.", "quantity");

            amount = parsed.Value;

        }

        var r = await service.QuotePriceAsync(companyId, new PriceQuoteQuery(itemId, unitId.Value, amount, date.Value, currencyCode, partyId, patternRevisionId, priceListCode) { BranchId = branchId },

            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new PriceQuoteResponse

        {

            Found = r.Found,

            CurrencyCode = r.CurrencyCode,

            Price = r.Price is { } price ? FinancialDecimalText.Format(price, FinancialDecimalText.MoneyScale) : null,

            PriceId = r.PriceId,

            PriceListCode = r.PriceListCode,

            Specificity = r.Specificity,

            PartyId = r.PartyId,

            PatternId = r.PatternId,

            MinQuantity = r.MinQuantity is { } min ? FinancialDecimalText.Format(min, FinancialDecimalText.MoneyScale) : null,

            ValidFrom = r.ValidFrom,

            ValidTo = r.ValidTo,

        });

    }



    /// <summary>Strict: an unparsable price is a 400, never a silent zero.</summary>

    private static CreateItemPriceCommand ToPriceCommand(CreateItemPriceRequest p)

    {

        if (!FinancialDecimalText.TryParse(p.Price, FinancialDecimalText.MoneyScale, 15, out var price) || price.Value < 0)

            throw new PetraERP.Application.Common.RequestValidationException("inventory.price_invalid", "Price must be a non-negative decimal string.", "price");

        if (!FinancialDecimalText.TryParse(p.MinQuantity, FinancialDecimalText.MoneyScale, 15, out var minimum) || minimum.Value < 0)

            throw new PetraERP.Application.Common.RequestValidationException("inventory.price_invalid", "MinQuantity must be a non-negative decimal string.", "minQuantity");

        return new CreateItemPriceCommand(p.UnitId, p.PriceListCode, price.Value, p.CurrencyCode, minimum.Value, p.PartyId, p.PatternId, p.ValidFrom, p.ValidTo);

    }



    private static ItemPriceDto MapPrice(ItemPriceResult p) => new()

    {

        Id = p.Id,

        ItemId = p.ItemId,

        UnitId = p.UnitId,

        UnitCode = p.UnitCode,

        PriceListCode = p.PriceListCode,

        Price = FinancialDecimalText.Format(p.Price, FinancialDecimalText.MoneyScale),

        CurrencyCode = p.CurrencyCode,

        MinQuantity = FinancialDecimalText.Format(p.MinQuantity, FinancialDecimalText.MoneyScale),

        PartyId = p.PartyId,

        PatternId = p.PatternId,

        ValidFrom = p.ValidFrom,

        ValidTo = p.ValidTo,

    };



    private static decimal ParseDecimal(string text, decimal defaultValue = 0m)

    {

        return FinancialDecimalText.TryParse(text, FinancialDecimalText.MoneyScale, 15, out var val)

            ? val ?? defaultValue

            : defaultValue;

    }



    private static decimal? ParseNullableDecimal(string? text)

    {

        if (string.IsNullOrWhiteSpace(text)) return null;

        return FinancialDecimalText.TryParse(text, FinancialDecimalText.MoneyScale, 15, out var val) ? val : null;

    }

}

