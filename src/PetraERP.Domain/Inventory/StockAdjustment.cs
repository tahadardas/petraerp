namespace PetraERP.Domain.Inventory;

public sealed class StockAdjustment
{
    private readonly List<StockAdjustmentLine> lines = [];

    private StockAdjustment() { }

    public StockAdjustment(
        Guid id,
        Guid companyId,
        Guid branchId,
        Guid warehouseId,
        string adjustmentNumber,
        DateTimeOffset adjustmentDateUtc,
        string reason,
        StockAdjustmentStatus status = StockAdjustmentStatus.Draft,
        string? notes = null,
        Guid createdByUserId = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("Adjustment ID cannot be empty.", nameof(id));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID cannot be empty.", nameof(branchId));
        if (warehouseId == Guid.Empty) throw new ArgumentException("Warehouse ID cannot be empty.", nameof(warehouseId));
        if (string.IsNullOrWhiteSpace(adjustmentNumber)) throw new ArgumentException("Adjustment number cannot be empty.", nameof(adjustmentNumber));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason is required for inventory adjustment.", nameof(reason));

        Id = id;
        CompanyId = companyId;
        BranchId = branchId;
        WarehouseId = warehouseId;
        AdjustmentNumber = adjustmentNumber.Trim();
        AdjustmentDateUtc = adjustmentDateUtc;
        Reason = reason.Trim();
        Status = status;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string AdjustmentNumber { get; private set; } = string.Empty;
    public DateTimeOffset AdjustmentDateUtc { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public StockAdjustmentStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<StockAdjustmentLine> Lines => lines.AsReadOnly();

    public void AddLine(StockAdjustmentLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.AdjustmentId != Id)
            throw new ArgumentException("Line adjustment ID must match this adjustment.", nameof(line));
        lines.Add(line);
    }

    public void Approve(Guid approvedByUserId)
    {
        if (Status != StockAdjustmentStatus.Draft)
            throw new InvalidOperationException("Only draft adjustments can be approved.");
        if (lines.Count == 0)
            throw new InvalidOperationException("Adjustment must have at least one line.");
        if (approvedByUserId == Guid.Empty)
            throw new ArgumentException("Approving user ID cannot be empty.", nameof(approvedByUserId));

        Status = StockAdjustmentStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAtUtc = DateTimeOffset.UtcNow;
    }
}

public sealed class StockAdjustmentLine
{
    private StockAdjustmentLine() { }

    public StockAdjustmentLine(
        Guid id,
        Guid adjustmentId,
        Guid itemId,
        Guid unitId,
        decimal systemQuantity,
        decimal countedQuantity,
        decimal unitCost = 0m)
    {
        if (id == Guid.Empty) throw new ArgumentException("Line ID cannot be empty.", nameof(id));
        if (adjustmentId == Guid.Empty) throw new ArgumentException("Adjustment ID cannot be empty.", nameof(adjustmentId));
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID cannot be empty.", nameof(itemId));
        if (unitId == Guid.Empty) throw new ArgumentException("Unit ID cannot be empty.", nameof(unitId));
        if (countedQuantity < 0) throw new ArgumentOutOfRangeException(nameof(countedQuantity), "Counted quantity cannot be negative.");
        if (unitCost < 0) throw new ArgumentOutOfRangeException(nameof(unitCost), "Unit cost cannot be negative.");

        Id = id;
        AdjustmentId = adjustmentId;
        ItemId = itemId;
        UnitId = unitId;
        SystemQuantity = systemQuantity;
        CountedQuantity = countedQuantity;
        DifferenceQuantity = countedQuantity - systemQuantity;
        UnitCost = unitCost;
    }

    public Guid Id { get; private set; }
    public Guid AdjustmentId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid UnitId { get; private set; }
    public decimal SystemQuantity { get; private set; }
    public decimal CountedQuantity { get; private set; }
    public decimal DifferenceQuantity { get; private set; }
    public decimal UnitCost { get; private set; }
}
