using PetraERP.Domain.Inventory;

namespace PetraERP.Application.Inventory;

public sealed record ItemBarcodeResult(
    Guid Id,
    Guid ItemId,
    Guid UnitId,
    string UnitCode,
    string Barcode,
    bool IsDefault);

public sealed record ItemPriceResult(
    Guid Id,
    Guid ItemId,
    Guid UnitId,
    string UnitCode,
    string PriceListCode,
    decimal Price,
    string CurrencyCode,
    decimal MinQuantity,
    Guid? PartyId = null,
    Guid? PatternId = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null);

/// <summary>The server-resolved price for a document line, or Found = false (no price is invented).</summary>
public sealed record PriceQuoteResult(
    bool Found,
    string CurrencyCode,
    decimal? Price = null,
    Guid? PriceId = null,
    string? PriceListCode = null,
    int? Specificity = null,
    Guid? PartyId = null,
    Guid? PatternId = null,
    decimal? MinQuantity = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null);

public sealed record ItemSummaryResult(
    Guid Id,
    Guid CompanyId,
    Guid? GroupId,
    string? GroupName,
    string Code,
    string NameArabic,
    string? NameEnglish,
    Guid BaseUnitId,
    string BaseUnitCode,
    string BaseUnitSymbol,
    CostMethod CostMethod,
    decimal? MinStock,
    decimal? MaxStock,
    decimal? ReorderPoint,
    bool IsActive,
    decimal CurrentStock);

public sealed record ItemDetailResult(
    Guid Id,
    Guid CompanyId,
    Guid? GroupId,
    string Code,
    string NameArabic,
    string? NameEnglish,
    Guid BaseUnitId,
    Guid? DefaultWarehouseId,
    CostMethod CostMethod,
    decimal? MinStock,
    decimal? MaxStock,
    decimal? ReorderPoint,
    bool IsActive,
    decimal CurrentStock,
    IReadOnlyList<ItemBarcodeResult> Barcodes,
    IReadOnlyList<ItemPriceResult> Prices,
    IReadOnlyList<UnitConversionResult> Conversions,
    ItemProfileData? Profile = null);

public sealed record ItemLookupResult(
    Guid Id,
    string Code,
    string NameArabic,
    string? NameEnglish,
    Guid BaseUnitId,
    string BaseUnitCode,
    string BaseUnitSymbol,
    decimal CurrentStock);

public sealed record ItemPageResult(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<ItemSummaryResult> Items);

public sealed record ItemGroupResult(
    Guid Id,
    Guid CompanyId,
    Guid? ParentId,
    string Code,
    string NameArabic,
    string? NameEnglish,
    bool IsActive,
    string? Description = null);

public sealed record ItemGroupDetailResult(
    Guid Id,
    Guid CompanyId,
    Guid? ParentId,
    string? ParentNameArabic,
    string Code,
    string NameArabic,
    string? NameEnglish,
    string? Description,
    bool IsActive,
    int SubgroupsCount,
    int ItemsCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    byte[] RowVersion);

public sealed record ItemAccountMappingResult(
    Guid Id,
    Guid CompanyId,
    int OwnerType,
    Guid OwnerId,
    string Role,
    Guid AccountId,
    string AccountCode,
    string AccountNameArabic,
    bool IsActive);

public sealed record EffectiveAccountDetailResult(
    string Role,
    Guid? AccountId,
    string? AccountCode,
    string? AccountNameArabic,
    string Source,
    string? SourceDescription);

public sealed record EffectiveItemAccountMappingResult(
    Guid ItemId,
    string ItemCode,
    string ItemNameArabic,
    Guid? GroupId,
    string? GroupNameArabic,
    IReadOnlyList<EffectiveAccountDetailResult> Accounts);

public sealed record UnitOfMeasureResult(
    Guid Id,
    Guid CompanyId,
    string Code,
    string NameArabic,
    string? NameEnglish,
    string Symbol,
    int DecimalPlaces,
    bool IsActive,
    UnitDimension Dimension = UnitDimension.Unspecified);

public sealed record UnitConversionResult(
    Guid Id,
    Guid CompanyId,
    Guid? ItemId,
    Guid FromUnitId,
    string FromUnitCode,
    string FromUnitSymbol,
    Guid ToUnitId,
    string ToUnitCode,
    string ToUnitSymbol,
    decimal Factor);

public sealed record WarehouseResult(
    Guid Id,
    Guid CompanyId,
    Guid BranchId,
    string Code,
    string NameArabic,
    string? NameEnglish,
    string? Location,
    bool IsActive);

public sealed record WarehouseStockResult(
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    decimal Stock,
    decimal Value);

public sealed record ItemStockSummaryResult(
    Guid ItemId,
    string ItemCode,
    string ItemNameArabic,
    string BaseUnitSymbol,
    decimal TotalStock,
    decimal TotalValue,
    decimal AverageCost,
    IReadOnlyList<WarehouseStockResult> Warehouses);

public sealed record StockMovementResult(
    Guid Id,
    Guid CompanyId,
    Guid BranchId,
    Guid WarehouseId,
    string WarehouseCode,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid UnitId,
    string UnitCode,
    StockMovementKind MovementKind,
    decimal Quantity,
    decimal BaseQuantity,
    decimal UnitCost,
    decimal TotalCost,
    DateTimeOffset MovementDateUtc,
    string DocumentType,
    string? DocumentNumber,
    Guid? CostCenterId,
    string? CostCenterCode,
    string? Notes);

public sealed record StockMovementPageResult(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<StockMovementResult> Items);

public sealed record StockTransferLineResult(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid UnitId,
    string UnitCode,
    decimal Quantity,
    decimal BaseQuantity,
    decimal UnitCost,
    decimal TotalCost);

public sealed record StockTransferResult(
    Guid Id,
    string TransferNumber,
    Guid SourceBranchId,
    Guid SourceWarehouseId,
    Guid TargetBranchId,
    Guid TargetWarehouseId,
    DateTimeOffset TransferDateUtc,
    StockTransferStatus Status,
    string? Notes,
    IReadOnlyList<StockTransferLineResult> Lines);

public sealed record StockAdjustmentLineResult(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid UnitId,
    string UnitCode,
    decimal SystemQuantity,
    decimal CountedQuantity,
    decimal DifferenceQuantity,
    decimal UnitCost);

public sealed record StockAdjustmentResult(
    Guid Id,
    string AdjustmentNumber,
    Guid BranchId,
    Guid WarehouseId,
    DateTimeOffset AdjustmentDateUtc,
    string Reason,
    StockAdjustmentStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAtUtc,
    string? Notes,
    IReadOnlyList<StockAdjustmentLineResult> Lines)
{
    public string Version { get; init; } = string.Empty;
}
