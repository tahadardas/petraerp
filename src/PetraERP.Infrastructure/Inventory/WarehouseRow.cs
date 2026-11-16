using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class WarehouseRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameArabic { get; set; } = string.Empty;
    public string? NameEnglish { get; set; }
    public string? Location { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Optional hierarchy and accounting hints; validated by the service (same company/branch, postable accounts), not by foreign keys.</summary>
    public Guid? ParentWarehouseId { get; set; }
    public Guid? InventoryAccountId { get; set; }
    public Guid? InTransitAccountId { get; set; }
    public Guid? DefaultCostCenterId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
