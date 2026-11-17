using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Inventory;

public enum CostMethod
{
    WeightedAverage = 1,
    Fifo = 2
}

public enum StockMovementKind
{
    OpeningBalance = 1,
    PurchaseReceipt = 2,
    PurchaseReturn = 3,
    SalesDelivery = 4,
    SalesReturn = 5,
    TransferIn = 6,
    TransferOut = 7,
    AdjustmentIncrease = 8,
    AdjustmentDecrease = 9,
    InternalIssue = 10,
    AssemblyIssue = 11,
    AssemblyReceipt = 12,
    DisassemblyIssue = 13,
    DisassemblyReceipt = 14
}

public enum StockTransferStatus
{
    Draft = 1,
    Completed = 2,
    Cancelled = 3
}

public enum StockAdjustmentStatus
{
    Draft = 1,
    Approved = 2
}

public sealed record ItemGroupDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid? ParentId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; }
}

public sealed record ItemGroupDetailDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid? ParentId { get; init; }
    public string? ParentNameArabic { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public int SubgroupsCount { get; init; }
    public int ItemsCount { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public byte[] RowVersion { get; init; } = [];
}

public sealed record ItemGroupTreeItemDto
{
    public Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public bool IsActive { get; init; }
    public int ItemCount { get; init; }
    public List<ItemGroupTreeItemDto> Children { get; init; } = [];
}

public sealed record ItemAccountMappingDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public int OwnerType { get; init; } // 1 = Item, 2 = ItemGroup
    public Guid OwnerId { get; init; }
    public string Role { get; init; } = string.Empty;
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountNameArabic { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

public sealed record SetItemAccountMappingRequest
{
    [Required, MaxLength(64)]
    public string Role { get; init; } = string.Empty;

    public Guid AccountId { get; init; }
}

public sealed record SetAccountMappingsRequest
{
    public int OwnerType { get; init; }
    public Guid OwnerId { get; init; }
    public required IReadOnlyList<SetItemAccountMappingRequest> Mappings { get; init; }
}

public sealed record EffectiveAccountDetailDto
{
    public string Role { get; init; } = string.Empty;
    public Guid? AccountId { get; init; }
    public string? AccountCode { get; init; }
    public string? AccountNameArabic { get; init; }
    public string Source { get; init; } = string.Empty; // "ItemOverride", "ImmediateGroup", "AncestorGroup", "PatternDefault", "CompanyDefault", "Unresolved"
    public string? SourceDescription { get; init; }
}

public sealed record EffectiveItemAccountMappingDto
{
    public Guid ItemId { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string ItemNameArabic { get; init; } = string.Empty;
    public Guid? GroupId { get; init; }
    public string? GroupNameArabic { get; init; }
    public List<EffectiveAccountDetailDto> Accounts { get; init; } = [];
}

public sealed record CreateItemGroupRequest
{
    [Required, MaxLength(64)]
    public string Code { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string NameArabic { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? NameEnglish { get; init; }

    [MaxLength(1000)]
    public string? Description { get; init; }

    public Guid? ParentId { get; init; }
}

public sealed record UpdateItemGroupRequest
{
    [Required, MaxLength(200)]
    public string NameArabic { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? NameEnglish { get; init; }

    [MaxLength(1000)]
    public string? Description { get; init; }

    public Guid? ParentId { get; init; }
}

/// <summary>The physical dimension of a unit. Company-wide conversions never cross dimensions; no density is assumed.</summary>
public enum UnitDimension
{
    Unspecified = 0,
    Mass = 1,
    Volume = 2,
    Length = 3,
    Area = 4,
    Count = 5,
    Time = 6,
    Other = 7,
}

public sealed record UnitOfMeasureDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public int DecimalPlaces { get; init; }
    public bool IsActive { get; init; }
    public UnitDimension Dimension { get; init; }
}

public sealed record CreateUnitRequest
{
    [Required, MaxLength(64)]
    public string Code { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string NameArabic { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? NameEnglish { get; init; }

    [Required, MaxLength(32)]
    public string Symbol { get; init; } = string.Empty;

    [Range(0, 4)]
    public int DecimalPlaces { get; init; }

    /// <summary>When classified, document quantities in this unit must respect <see cref="DecimalPlaces"/> (for example whole pieces).</summary>
    public UnitDimension Dimension { get; init; } = UnitDimension.Unspecified;
}

public sealed record UpdateUnitRequest
{
    [Required, MaxLength(200)]
    public string NameArabic { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? NameEnglish { get; init; }

    [Required, MaxLength(32)]
    public string Symbol { get; init; } = string.Empty;

    [Range(0, 4)]
    public int DecimalPlaces { get; init; }

    /// <summary>Omitted keeps the current dimension; a classified unit used by conversions cannot be reclassified.</summary>
    public UnitDimension? Dimension { get; init; }
}

public sealed record UnitConversionDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid? ItemId { get; init; }
    public Guid FromUnitId { get; init; }
    public string FromUnitCode { get; init; } = string.Empty;
    public string FromUnitSymbol { get; init; } = string.Empty;
    public Guid ToUnitId { get; init; }
    public string ToUnitCode { get; init; } = string.Empty;
    public string ToUnitSymbol { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Factor { get; init; } = "1.0000";
}

public sealed record CreateUnitConversionRequest
{
    public Guid? ItemId { get; init; }
    public Guid FromUnitId { get; init; }
    public Guid ToUnitId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Factor { get; init; } = "1.0000";
}

public sealed record WarehouseDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid BranchId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public string? Location { get; init; }
    public bool IsActive { get; init; }
}

public sealed record CreateWarehouseRequest
{
    public Guid BranchId { get; init; }

    [Required, MaxLength(64)]
    public string Code { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string NameArabic { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? NameEnglish { get; init; }

    [MaxLength(200)]
    public string? Location { get; init; }
}

public sealed record UpdateWarehouseRequest
{
    [Required, MaxLength(200)]
    public string NameArabic { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? NameEnglish { get; init; }

    [MaxLength(200)]
    public string? Location { get; init; }
}

public sealed record ItemBarcodeDto
{
    public Guid Id { get; init; }
    public Guid ItemId { get; init; }
    public Guid UnitId { get; init; }
    public string UnitCode { get; init; } = string.Empty;
    public string Barcode { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
}

public sealed record ItemPriceDto
{
    public Guid Id { get; init; }
    public Guid ItemId { get; init; }
    public Guid UnitId { get; init; }
    public string UnitCode { get; init; } = string.Empty;
    public string PriceListCode { get; init; } = "DEFAULT";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Price { get; init; } = "0.0000";

    public string CurrencyCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string MinQuantity { get; init; } = "0.0000";

    public Guid? PartyId { get; init; }
    public Guid? PatternId { get; init; }
    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
}

/// <summary>
/// The server-resolved price for a document line. Priority: customer + pattern, customer, pattern, general; then the
/// requested list (or DEFAULT), the highest reached quantity break, the latest validity start. Found = false means no
/// price applies in that unit and currency; nothing is converted or invented.
/// </summary>
public sealed record PriceQuoteResponse
{
    public bool Found { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? Price { get; init; }

    public Guid? PriceId { get; init; }
    public string? PriceListCode { get; init; }

    /// <summary>3 customer + pattern, 2 customer, 1 pattern, 0 general.</summary>
    public int? Specificity { get; init; }

    public Guid? PartyId { get; init; }
    public Guid? PatternId { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? MinQuantity { get; init; }

    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
}

public sealed record ItemSummaryDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid? GroupId { get; init; }
    public string? GroupName { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public Guid BaseUnitId { get; init; }
    public string BaseUnitCode { get; init; } = string.Empty;
    public string BaseUnitSymbol { get; init; } = string.Empty;
    public CostMethod CostMethod { get; init; }
    public string? MinStock { get; init; }
    public string? MaxStock { get; init; }
    public string? ReorderPoint { get; init; }
    public bool IsActive { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CurrentStock { get; init; } = "0.0000";
}

public sealed record ItemDetailDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid? GroupId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public Guid BaseUnitId { get; init; }
    public Guid? DefaultWarehouseId { get; init; }
    public CostMethod CostMethod { get; init; }
    public string? MinStock { get; init; }
    public string? MaxStock { get; init; }
    public string? ReorderPoint { get; init; }
    public bool IsActive { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CurrentStock { get; init; } = "0.0000";

    public IReadOnlyList<ItemBarcodeDto> Barcodes { get; init; } = [];
    public IReadOnlyList<ItemPriceDto> Prices { get; init; } = [];
    public IReadOnlyList<UnitConversionDto> Conversions { get; init; } = [];
    public ItemProfileDto? Profile { get; init; }
}

public sealed record CreateItemBarcodeRequest
{
    public Guid UnitId { get; init; }

    [Required, MaxLength(64)]
    public string Barcode { get; init; } = string.Empty;

    public bool IsDefault { get; init; }
}

public sealed record CreateItemPriceRequest
{
    public Guid UnitId { get; init; }

    [Required, MaxLength(64)]
    public string PriceListCode { get; init; } = "DEFAULT";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Price { get; init; } = "0.0000";

    [Required, StringLength(3, MinimumLength = 3)]
    public string CurrencyCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string MinQuantity { get; init; } = "0.0000";

    /// <summary>Customer-specific price; omitted applies to every customer.</summary>
    public Guid? PartyId { get; init; }

    /// <summary>Pattern-specific price (stable pattern id); omitted applies to every pattern.</summary>
    public Guid? PatternId { get; init; }

    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
}

public sealed record CreateItemRequest
{
    [Required, MaxLength(64)]
    public string Code { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string NameArabic { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? NameEnglish { get; init; }

    public Guid? GroupId { get; init; }
    public Guid BaseUnitId { get; init; }
    public Guid? DefaultWarehouseId { get; init; }
    public CostMethod CostMethod { get; init; } = CostMethod.WeightedAverage;

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? MinStock { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? MaxStock { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? ReorderPoint { get; init; }

    public IReadOnlyList<CreateItemBarcodeRequest> Barcodes { get; init; } = [];
    public IReadOnlyList<CreateItemPriceRequest> Prices { get; init; } = [];
    public ItemProfileDto? Profile { get; init; }
}

public sealed record UpdateItemRequest
{
    [Required, MaxLength(200)]
    public string NameArabic { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? NameEnglish { get; init; }

    public Guid? GroupId { get; init; }
    public Guid BaseUnitId { get; init; }
    public Guid? DefaultWarehouseId { get; init; }
    public CostMethod CostMethod { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? MinStock { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? MaxStock { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? ReorderPoint { get; init; }

    public ItemProfileDto? Profile { get; init; }

    /// <summary>When present the item's barcodes become exactly this list; null leaves them untouched.</summary>
    public IReadOnlyList<CreateItemBarcodeRequest>? Barcodes { get; init; }
}

public sealed record ItemLookupDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public Guid BaseUnitId { get; init; }
    public string BaseUnitCode { get; init; } = string.Empty;
    public string BaseUnitSymbol { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CurrentStock { get; init; } = "0.0000";
}

public sealed record ItemPageResponse
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int TotalCount { get; init; }
    public IReadOnlyList<ItemSummaryDto> Items { get; init; } = [];
}

public sealed record WarehouseStockDto
{
    public Guid WarehouseId { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public string WarehouseName { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Quantity { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string UnitCost { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TotalCost { get; init; } = "0.0000";
}

public sealed record ItemStockSummaryDto
{
    public Guid ItemId { get; init; }
    public string BaseUnitSymbol { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TotalQuantity { get; init; } = "0.0000";

    public IReadOnlyList<WarehouseStockDto> Warehouses { get; init; } = [];
}

public sealed record StockMovementDto
{
    public Guid Id { get; init; }
    public Guid WarehouseId { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public Guid ItemId { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public Guid UnitId { get; init; }
    public string UnitCode { get; init; } = string.Empty;
    public StockMovementKind MovementKind { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Quantity { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string BaseQuantity { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string UnitCost { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TotalCost { get; init; } = "0.0000";

    public DateTimeOffset MovementDateUtc { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string? DocumentNumber { get; init; }
    public Guid? CostCenterId { get; init; }
    public string? CostCenterCode { get; init; }
    public string? Notes { get; init; }
}

public sealed record StockMovementPageResponse
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int TotalCount { get; init; }
    public IReadOnlyList<StockMovementDto> Items { get; init; } = [];
}

public sealed record CreateStockTransferLineRequest
{
    public Guid ItemId { get; init; }
    public Guid UnitId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Quantity { get; init; } = "0.0000";

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string UnitCost { get; init; } = "0.0000";
}

public sealed record CreateStockTransferRequest
{
    [MaxLength(128)] public string? IdempotencyKey { get; init; }
    // Legacy external reference/idempotency fallback, never the official server number.
    [MaxLength(64)]
    public string TransferNumber { get; init; } = string.Empty;

    public Guid SourceWarehouseId { get; init; }
    public Guid TargetBranchId { get; init; }
    public Guid TargetWarehouseId { get; init; }
    public DateTimeOffset TransferDateUtc { get; init; }

    [MaxLength(500)]
    public string? Notes { get; init; }

    public IReadOnlyList<CreateStockTransferLineRequest> Lines { get; init; } = [];
}

public sealed record StockTransferLineDto
{
    public Guid Id { get; init; }
    public Guid ItemId { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public Guid UnitId { get; init; }
    public string UnitCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Quantity { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string BaseQuantity { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string UnitCost { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TotalCost { get; init; } = "0.0000";
}

public sealed record StockTransferDto
{
    public Guid Id { get; init; }
    public string TransferNumber { get; init; } = string.Empty;
    public Guid SourceBranchId { get; init; }
    public Guid SourceWarehouseId { get; init; }
    public Guid TargetBranchId { get; init; }
    public Guid TargetWarehouseId { get; init; }
    public DateTimeOffset TransferDateUtc { get; init; }
    public StockTransferStatus Status { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<StockTransferLineDto> Lines { get; init; } = [];
}

public sealed record CreateStockAdjustmentLineRequest
{
    public Guid ItemId { get; init; }
    public Guid UnitId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CountedQuantity { get; init; } = "0.0000";

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string UnitCost { get; init; } = "0.0000";
}

public sealed record CreateStockAdjustmentRequest
{
    public Guid? InventoryAccountId { get; init; }
    public Guid? OffsetAccountId { get; init; }
    // Retained for wire compatibility; official numbering happens only at posting.
    [MaxLength(64)]
    public string AdjustmentNumber { get; init; } = string.Empty;

    public Guid WarehouseId { get; init; }
    public DateTimeOffset AdjustmentDateUtc { get; init; }

    [Required, MaxLength(200)]
    public string Reason { get; init; } = string.Empty;

    [MaxLength(500)]
    public string? Notes { get; init; }

    public IReadOnlyList<CreateStockAdjustmentLineRequest> Lines { get; init; } = [];
}

public sealed record StockAdjustmentLineDto
{
    public Guid Id { get; init; }
    public Guid ItemId { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public Guid UnitId { get; init; }
    public string UnitCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string SystemQuantity { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string CountedQuantity { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string DifferenceQuantity { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string UnitCost { get; init; } = "0.0000";
}

public sealed record StockAdjustmentDto
{
    public string Version { get; init; } = string.Empty;
    public Guid Id { get; init; }
    public string AdjustmentNumber { get; init; } = string.Empty;
    public Guid BranchId { get; init; }
    public Guid WarehouseId { get; init; }
    public DateTimeOffset AdjustmentDateUtc { get; init; }
    public string Reason { get; init; } = string.Empty;
    public StockAdjustmentStatus Status { get; init; }
    public Guid? ApprovedByUserId { get; init; }
    public DateTimeOffset? ApprovedAtUtc { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<StockAdjustmentLineDto> Lines { get; init; } = [];
}

public sealed record PostInternalIssueRequest
{
    [Required, MaxLength(128)] public string IdempotencyKey { get; init; } = string.Empty;
    public Guid? InventoryAccountId { get; init; }
    public Guid? OffsetAccountId { get; init; }
    public Guid WarehouseId { get; init; }
    public Guid ItemId { get; init; }
    public Guid UnitId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Quantity { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string UnitCost { get; init; } = "0.0000";

    [Required]
    public Guid CostCenterId { get; init; }

    [MaxLength(64)]
    public string? DocumentNumber { get; init; }

    [MaxLength(500)]
    public string? Notes { get; init; }
}

public sealed record WarehouseAccountingDto
{
    public Guid WarehouseId { get; init; }
    public Guid? ParentWarehouseId { get; init; }
    public Guid? InventoryAccountId { get; init; }
    public string? InventoryAccountCode { get; init; }
    public Guid? InTransitAccountId { get; init; }
    public string? InTransitAccountCode { get; init; }
    public Guid? DefaultCostCenterId { get; init; }
    public string? DefaultCostCenterCode { get; init; }
}

public sealed record SetWarehouseAccountingRequest
{
    public Guid? ParentWarehouseId { get; init; }
    public Guid? InventoryAccountId { get; init; }
    public Guid? InTransitAccountId { get; init; }
    public Guid? DefaultCostCenterId { get; init; }
}
