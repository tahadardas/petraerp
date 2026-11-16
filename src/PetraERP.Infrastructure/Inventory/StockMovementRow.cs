namespace PetraERP.Infrastructure.Inventory;

internal sealed class StockMovementRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public int MovementKind { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public DateTimeOffset MovementDateUtc { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }

    /// <summary>The document line that produced this movement, when the document has lines.</summary>
    public Guid? DocumentLineId { get; set; }
    public string? DocumentNumber { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public long RecordedSequence { get; set; }
    public DateTimeOffset? RecordedAtUtc { get; set; }
    public decimal? QuantityBefore { get; set; }
    public decimal? QuantityAfter { get; set; }
    public decimal? ValueBefore { get; set; }
    public decimal? ValueAfter { get; set; }
    public Guid? ReversesMovementId { get; set; }
}
