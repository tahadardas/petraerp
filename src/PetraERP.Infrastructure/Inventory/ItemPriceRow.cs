using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class ItemPriceRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public string PriceListCode { get; set; } = "DEFAULT";
    public decimal Price { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal MinQuantity { get; set; }
    /// <summary>Customer-specific price when set; otherwise the row applies to every customer.</summary>
    public Guid? PartyId { get; set; }
    /// <summary>Pattern-specific price when set (the stable pattern id, not a revision).</summary>
    public Guid? PatternId { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigations
    public ItemRow? Item { get; set; }
    public UnitOfMeasureRow? Unit { get; set; }
}
