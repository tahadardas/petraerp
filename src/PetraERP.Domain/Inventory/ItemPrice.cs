namespace PetraERP.Domain.Inventory;

public sealed class ItemPrice
{
    private ItemPrice() { }

    public ItemPrice(
        Guid id,
        Guid itemId,
        Guid unitId,
        string priceListCode,
        decimal price,
        string currencyCode,
        decimal minQuantity = 0m)
    {
        if (id == Guid.Empty) throw new ArgumentException("Price ID cannot be empty.", nameof(id));
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID cannot be empty.", nameof(itemId));
        if (unitId == Guid.Empty) throw new ArgumentException("Unit ID cannot be empty.", nameof(unitId));
        if (string.IsNullOrWhiteSpace(priceListCode)) throw new ArgumentException("Price list code cannot be empty.", nameof(priceListCode));
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
            throw new ArgumentException("Currency code must be exactly 3 characters.", nameof(currencyCode));
        if (minQuantity < 0) throw new ArgumentOutOfRangeException(nameof(minQuantity), "Minimum quantity cannot be negative.");

        Id = id;
        ItemId = itemId;
        UnitId = unitId;
        PriceListCode = priceListCode.Trim().ToUpperInvariant();
        Price = price;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        MinQuantity = minQuantity;
    }

    public Guid Id { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid UnitId { get; private set; }
    public string PriceListCode { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal MinQuantity { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void UpdatePrice(decimal price, decimal minQuantity)
    {
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");
        if (minQuantity < 0) throw new ArgumentOutOfRangeException(nameof(minQuantity), "Minimum quantity cannot be negative.");
        Price = price;
        MinQuantity = minQuantity;
    }
}
