using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class ItemGroupRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameArabic { get; set; } = string.Empty;
    public string? NameEnglish { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigations
    public ItemGroupRow? Parent { get; set; }
    public ICollection<ItemGroupRow> Children { get; set; } = [];
    public ICollection<ItemRow> Items { get; set; } = [];
}
