using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class InventoryDocumentRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public int Kind { get; set; }
    public int State { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? TargetWarehouseId { get; set; }
    public DateTimeOffset DateUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? CostCenterId { get; set; }
    public Guid? InventoryAccountId { get; set; }
    public Guid? OffsetAccountId { get; set; }
    public string? Number { get; set; }
    public Guid? JournalId { get; set; }
    public Guid? ReversalJournalId { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? PostedBy { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public string? PostKey { get; set; }
    public string? CreationFingerprint { get; set; }
    public string? CreationKey { get; set; }
    public string? PostVersion { get; set; }
    public string? PostReason { get; set; }
    public string? ReverseKey { get; set; }
    public string? ReverseVersion { get; set; }
    public string? ReverseReason { get; set; }
    public Guid? ReversedBy { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<InventoryDocumentLineRow> Lines { get; set; } = [];
}

internal sealed class InventoryDocumentLineRow
{
    public decimal? OpeningUnitCost { get; set; }
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public int Position { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal SnapshotQuantity { get; set; }
    public long SnapshotSequence { get; set; }
    public decimal Cost { get; set; }
    public InventoryDocumentRow? Document { get; set; }
}
