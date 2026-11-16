using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class ItemRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? GroupId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameArabic { get; set; } = string.Empty;
    public string? NameEnglish { get; set; }
    public string NormalizedSearchText { get; set; } = string.Empty;
    public Guid BaseUnitId { get; set; }
    public Guid? DefaultWarehouseId { get; set; }
    public int CostMethod { get; set; } = 1; // 1=WeightedAverage, 2=Fifo
    public decimal? MinStock { get; set; }
    public decimal? MaxStock { get; set; }
    public decimal? ReorderPoint { get; set; }
    public bool IsActive { get; set; } = true;

    // Al-Ameen item card: units 2 and 3 (unit 1 is BaseUnitId), gift rule, specification lists and item options.
    public Guid? Unit2Id { get; set; }
    public decimal? Unit2Factor { get; set; }
    public bool Unit2FactorVariable { get; set; }
    public Guid? Unit3Id { get; set; }
    public decimal? Unit3Factor { get; set; }
    public bool Unit3FactorVariable { get; set; }
    public int DefaultUnitIndex { get; set; } = 1;
    public decimal? GiftQuantity { get; set; }
    public decimal? GiftPerQuantity { get; set; }
    public string? Specifications { get; set; }
    public string? Measure { get; set; }
    public string? Source { get; set; }
    public string? Location { get; set; }
    public string? Manufacturer { get; set; }
    public string? Color { get; set; }
    public string? OriginCountry { get; set; }
    public string? Quality { get; set; }
    public string? Model { get; set; }
    public int ItemKind { get; set; } = 1;
    public bool IsAssembly { get; set; }
    public bool ComponentPricing { get; set; }
    public bool TracksSerialNumber { get; set; }
    public bool EnforceSerialOnReceipt { get; set; }
    public bool EnforceSerialOnIssue { get; set; }
    public bool TracksProductionDate { get; set; }
    public bool TracksExpiryDate { get; set; }
    public bool EnforceDatesOnReceipt { get; set; }
    public bool EnforceDatesOnIssue { get; set; }
    public bool HideInSearch { get; set; }
    public bool IntegerQuantityOnInvoice { get; set; }
    public string? ImagePath { get; set; }
    public int PricingPolicy { get; set; } = 1;
    public decimal? LastPurchaseRate { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigations
    public ItemGroupRow? Group { get; set; }
    public UnitOfMeasureRow? BaseUnit { get; set; }
    public WarehouseRow? DefaultWarehouse { get; set; }
    public ICollection<ItemBarcodeRow> Barcodes { get; set; } = [];
    public ICollection<ItemPriceRow> Prices { get; set; } = [];
    public ICollection<UnitConversionRow> Conversions { get; set; } = [];
}
