using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Inventory;

public sealed class Item
{
    private readonly List<ItemBarcode> barcodes = [];
    private readonly List<ItemPrice> prices = [];

    private Item() { }

    public Item(
        Guid id,
        Guid companyId,
        EntityCode code,
        BilingualName name,
        Guid baseUnitId,
        CostMethod costMethod = CostMethod.WeightedAverage,
        Guid? groupId = null,
        Guid? defaultWarehouseId = null,
        decimal? minStock = null,
        decimal? maxStock = null,
        decimal? reorderPoint = null,
        bool isActive = true)
    {
        if (id == Guid.Empty) throw new ArgumentException("Item ID cannot be empty.", nameof(id));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        if (baseUnitId == Guid.Empty) throw new ArgumentException("Base unit ID cannot be empty.", nameof(baseUnitId));
        if (!Enum.IsDefined(costMethod)) throw new ArgumentOutOfRangeException(nameof(costMethod), "Unknown cost method.");
        ValidateStockLevels(minStock, maxStock, reorderPoint);

        Id = id;
        CompanyId = companyId;
        Code = code;
        Name = name;
        BaseUnitId = baseUnitId;
        CostMethod = costMethod;
        GroupId = groupId;
        DefaultWarehouseId = defaultWarehouseId;
        MinStock = minStock;
        MaxStock = maxStock;
        ReorderPoint = reorderPoint;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public EntityCode Code { get; private set; } = null!;
    public BilingualName Name { get; private set; } = null!;
    public Guid BaseUnitId { get; private set; }
    public CostMethod CostMethod { get; private set; }
    public Guid? GroupId { get; private set; }
    public Guid? DefaultWarehouseId { get; private set; }
    public decimal? MinStock { get; private set; }
    public decimal? MaxStock { get; private set; }
    public decimal? ReorderPoint { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<ItemBarcode> Barcodes => barcodes.AsReadOnly();
    public IReadOnlyList<ItemPrice> Prices => prices.AsReadOnly();

    public void Update(
        BilingualName name,
        Guid? groupId,
        Guid baseUnitId,
        CostMethod costMethod,
        Guid? defaultWarehouseId,
        decimal? minStock,
        decimal? maxStock,
        decimal? reorderPoint)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (baseUnitId == Guid.Empty) throw new ArgumentException("Base unit ID cannot be empty.", nameof(baseUnitId));
        if (!Enum.IsDefined(costMethod)) throw new ArgumentOutOfRangeException(nameof(costMethod), "Unknown cost method.");
        ValidateStockLevels(minStock, maxStock, reorderPoint);

        Name = name;
        GroupId = groupId;
        BaseUnitId = baseUnitId;
        CostMethod = costMethod;
        DefaultWarehouseId = defaultWarehouseId;
        MinStock = minStock;
        MaxStock = maxStock;
        ReorderPoint = reorderPoint;
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;

    public void AddBarcode(ItemBarcode barcode)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        if (barcode.ItemId != Id)
            throw new ArgumentException("Barcode item ID must match this item.", nameof(barcode));
        if (barcode.IsDefault)
        {
            foreach (var existing in barcodes) existing.SetDefault(false);
        }
        barcodes.Add(barcode);
    }

    public void RemoveBarcode(Guid barcodeId) =>
        barcodes.RemoveAll(b => b.Id == barcodeId);

    public void AddPrice(ItemPrice price)
    {
        ArgumentNullException.ThrowIfNull(price);
        if (price.ItemId != Id)
            throw new ArgumentException("Price item ID must match this item.", nameof(price));
        prices.Add(price);
    }

    public void RemovePrice(Guid priceId) =>
        prices.RemoveAll(p => p.Id == priceId);

    private static void ValidateStockLevels(decimal? min, decimal? max, decimal? reorder)
    {
        if (min.HasValue && min.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(min), "Minimum stock cannot be negative.");
        if (max.HasValue && max.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(max), "Maximum stock cannot be negative.");
        if (reorder.HasValue && reorder.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(reorder), "Reorder point cannot be negative.");
        if (min.HasValue && max.HasValue && min.Value > max.Value)
            throw new ArgumentException("Minimum stock cannot exceed maximum stock.");
    }
}
