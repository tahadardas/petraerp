using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class StockAdjustmentRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public string AdjustmentNumber { get; set; } = string.Empty;
    public DateTimeOffset AdjustmentDateUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Status { get; set; } = 1; // 1=Draft, 2=Approved
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigations
    public ICollection<StockAdjustmentLineRow> Lines { get; set; } = [];
}

internal sealed class StockAdjustmentLineRow
{
    public Guid Id { get; set; }
    public Guid AdjustmentId { get; set; }
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
    public decimal DifferenceQuantity { get; set; }
    public decimal UnitCost { get; set; }

    // Navigations
    public StockAdjustmentRow? Adjustment { get; set; }
}
