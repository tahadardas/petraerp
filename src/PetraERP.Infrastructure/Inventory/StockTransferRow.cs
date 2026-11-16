using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class StockTransferRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public Guid SourceBranchId { get; set; }
    public Guid SourceWarehouseId { get; set; }
    public Guid TargetBranchId { get; set; }
    public Guid TargetWarehouseId { get; set; }
    public DateTimeOffset TransferDateUtc { get; set; }
    public int Status { get; set; } = 1; // 1=Draft, 2=Completed, 3=Cancelled
    public string? Notes { get; set; }
    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigations
    public ICollection<StockTransferLineRow> Lines { get; set; } = [];
}

internal sealed class StockTransferLineRow
{
    public Guid Id { get; set; }
    public Guid TransferId { get; set; }
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }

    // Navigations
    public StockTransferRow? Transfer { get; set; }
}
