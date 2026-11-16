namespace PetraERP.Domain.Inventory;

public sealed class ItemBarcode
{
    private ItemBarcode() { }

    public ItemBarcode(
        Guid id,
        Guid itemId,
        Guid unitId,
        string barcode,
        bool isDefault = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("Barcode ID cannot be empty.", nameof(id));
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID cannot be empty.", nameof(itemId));
        if (unitId == Guid.Empty) throw new ArgumentException("Unit ID cannot be empty.", nameof(unitId));
        if (string.IsNullOrWhiteSpace(barcode)) throw new ArgumentException("Barcode value cannot be empty.", nameof(barcode));

        Id = id;
        ItemId = itemId;
        UnitId = unitId;
        Barcode = barcode.Trim();
        IsDefault = isDefault;
    }

    public Guid Id { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid UnitId { get; private set; }
    public string Barcode { get; private set; } = string.Empty;
    public bool IsDefault { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void SetDefault(bool isDefault) => IsDefault = isDefault;
}
