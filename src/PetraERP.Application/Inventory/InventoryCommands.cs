using PetraERP.Domain.Inventory;

namespace PetraERP.Application.Inventory;

public sealed record CreateItemBarcodeCommand(
    Guid UnitId,
    string Barcode,
    bool IsDefault);

/// <summary>
/// A price row. Optional scope: one customer, one pattern (stable pattern id) and a validity window; the server resolves
/// the applicable row with <see cref="PetraERP.Domain.Inventory.PriceListPolicy"/>.
/// </summary>
public sealed record CreateItemPriceCommand(
    Guid UnitId,
    string PriceListCode,
    decimal Price,
    string CurrencyCode,
    decimal MinQuantity,
    Guid? PartyId = null,
    Guid? PatternId = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null);

/// <summary>What a document line asks the price resolver. A null currency resolves like the document would.</summary>
public sealed record PriceQuoteQuery(
    Guid ItemId,
    Guid UnitId,
    decimal Quantity,
    DateOnly Date,
    string? CurrencyCode = null,
    Guid? PartyId = null,
    Guid? PatternRevisionId = null,
    string? PriceListCode = null)
{
    public Guid? BranchId { get; init; }
}

public sealed record CreateItemCommand(
    string Code,
    string NameArabic,
    string? NameEnglish,
    Guid? GroupId,
    Guid BaseUnitId,
    Guid? DefaultWarehouseId,
    CostMethod CostMethod,
    decimal? MinStock,
    decimal? MaxStock,
    decimal? ReorderPoint,
    IReadOnlyList<CreateItemBarcodeCommand>? Barcodes = null,
    IReadOnlyList<CreateItemPriceCommand>? Prices = null,
    ItemProfileData? Profile = null);

public sealed record UpdateItemCommand(
    string NameArabic,
    string? NameEnglish,
    Guid? GroupId,
    Guid BaseUnitId,
    Guid? DefaultWarehouseId,
    CostMethod CostMethod,
    decimal? MinStock,
    decimal? MaxStock,
    decimal? ReorderPoint,
    ItemProfileData? Profile = null,
    IReadOnlyList<CreateItemBarcodeCommand>? Barcodes = null);

public sealed record CreateItemGroupCommand(
    string Code,
    string NameArabic,
    string? NameEnglish,
    Guid? ParentId,
    string? Description = null);

public sealed record UpdateItemGroupCommand(
    string NameArabic,
    string? NameEnglish,
    Guid? ParentId,
    string? Description = null);

public sealed record SetItemAccountMappingCommand(
    string Role,
    Guid AccountId);

public sealed record CreateUnitCommand(
    string Code,
    string NameArabic,
    string? NameEnglish,
    string Symbol,
    int DecimalPlaces,
    UnitDimension Dimension = UnitDimension.Unspecified);

public sealed record UpdateUnitCommand(
    string NameArabic,
    string? NameEnglish,
    string Symbol,
    int DecimalPlaces,
    UnitDimension? Dimension = null);

public sealed record CreateUnitConversionCommand(
    Guid? ItemId,
    Guid FromUnitId,
    Guid ToUnitId,
    decimal Factor);

public sealed record CreateWarehouseCommand(
    Guid BranchId,
    string Code,
    string NameArabic,
    string? NameEnglish,
    string? Location);

public sealed record UpdateWarehouseCommand(
    string NameArabic,
    string? NameEnglish,
    string? Location);

public sealed record CreateStockTransferLineCommand(
    Guid ItemId,
    Guid UnitId,
    decimal Quantity,
    decimal UnitCost);

public sealed record CreateStockTransferCommand(
    string TransferNumber,
    Guid SourceWarehouseId,
    Guid TargetBranchId,
    Guid TargetWarehouseId,
    DateTimeOffset TransferDateUtc,
    string? Notes,
    IReadOnlyList<CreateStockTransferLineCommand> Lines)
{
    public string? IdempotencyKey { get; init; }
}

public sealed record CreateStockAdjustmentLineCommand(
    Guid ItemId,
    Guid UnitId,
    decimal CountedQuantity,
    decimal UnitCost);

public sealed record CreateStockAdjustmentCommand(
    string AdjustmentNumber,
    Guid WarehouseId,
    DateTimeOffset AdjustmentDateUtc,
    string Reason,
    string? Notes,
    IReadOnlyList<CreateStockAdjustmentLineCommand> Lines)
{
    public Guid? InventoryAccountId { get; init; }
    public Guid? OffsetAccountId { get; init; }
}

public sealed record PostInternalIssueCommand(
    Guid WarehouseId,
    Guid ItemId,
    Guid UnitId,
    decimal Quantity,
    decimal UnitCost,
    Guid CostCenterId,
    string? DocumentNumber,
    string? Notes)
{
    public string? IdempotencyKey { get; init; }
    public Guid? InventoryAccountId { get; init; }
    public Guid? OffsetAccountId { get; init; }
}
