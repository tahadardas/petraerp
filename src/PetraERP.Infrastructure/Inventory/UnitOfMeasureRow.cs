using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class UnitOfMeasureRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameArabic { get; set; } = string.Empty;
    public string? NameEnglish { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; }
    /// <summary><see cref="PetraERP.Domain.Inventory.UnitDimension"/>; 0 = unclassified legacy unit.</summary>
    public byte Dimension { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
