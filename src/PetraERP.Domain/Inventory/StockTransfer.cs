using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Inventory;

public sealed class StockTransfer
{
    private readonly List<StockTransferLine> lines = [];

    private StockTransfer() { }

    public StockTransfer(
        Guid id,
        Guid companyId,
        string transferNumber,
        Guid sourceBranchId,
        Guid sourceWarehouseId,
        Guid targetBranchId,
        Guid targetWarehouseId,
        DateTimeOffset transferDateUtc,
        StockTransferStatus status = StockTransferStatus.Draft,
        string? notes = null,
        Guid createdByUserId = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("Transfer ID cannot be empty.", nameof(id));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(transferNumber)) throw new ArgumentException("Transfer number cannot be empty.", nameof(transferNumber));
        if (sourceBranchId == Guid.Empty) throw new ArgumentException("Source branch ID cannot be empty.", nameof(sourceBranchId));
        if (sourceWarehouseId == Guid.Empty) throw new ArgumentException("Source warehouse ID cannot be empty.", nameof(sourceWarehouseId));
        if (targetBranchId == Guid.Empty) throw new ArgumentException("Target branch ID cannot be empty.", nameof(targetBranchId));
        if (targetWarehouseId == Guid.Empty) throw new ArgumentException("Target warehouse ID cannot be empty.", nameof(targetWarehouseId));
        if (sourceWarehouseId == targetWarehouseId)
        {
            throw RuleViolation.Create(
                RuleViolation.WarehouseTransferInvalid,
                "Source warehouse and target warehouse cannot be the same.");
        }

        Id = id;
        CompanyId = companyId;
        TransferNumber = transferNumber.Trim();
        SourceBranchId = sourceBranchId;
        SourceWarehouseId = sourceWarehouseId;
        TargetBranchId = targetBranchId;
        TargetWarehouseId = targetWarehouseId;
        TransferDateUtc = transferDateUtc;
        Status = status;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string TransferNumber { get; private set; } = string.Empty;
    public Guid SourceBranchId { get; private set; }
    public Guid SourceWarehouseId { get; private set; }
    public Guid TargetBranchId { get; private set; }
    public Guid TargetWarehouseId { get; private set; }
    public DateTimeOffset TransferDateUtc { get; private set; }
    public StockTransferStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<StockTransferLine> Lines => lines.AsReadOnly();

    public void AddLine(StockTransferLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.TransferId != Id)
            throw new ArgumentException("Line transfer ID must match this transfer.", nameof(line));
        lines.Add(line);
    }

    public void Complete()
    {
        if (Status != StockTransferStatus.Draft)
            throw new InvalidOperationException("Only draft transfers can be completed.");
        if (lines.Count == 0)
            throw new InvalidOperationException("Transfer must have at least one line.");
        Status = StockTransferStatus.Completed;
    }

    public void Cancel()
    {
        if (Status != StockTransferStatus.Draft)
            throw new InvalidOperationException("Only draft transfers can be cancelled.");
        Status = StockTransferStatus.Cancelled;
    }
}

public sealed class StockTransferLine
{
    private StockTransferLine() { }

    public StockTransferLine(
        Guid id,
        Guid transferId,
        Guid itemId,
        Guid unitId,
        decimal quantity,
        decimal baseQuantity,
        decimal unitCost = 0m,
        decimal totalCost = 0m)
    {
        if (id == Guid.Empty) throw new ArgumentException("Line ID cannot be empty.", nameof(id));
        if (transferId == Guid.Empty) throw new ArgumentException("Transfer ID cannot be empty.", nameof(transferId));
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID cannot be empty.", nameof(itemId));
        if (unitId == Guid.Empty) throw new ArgumentException("Unit ID cannot be empty.", nameof(unitId));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        if (baseQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(baseQuantity), "Base quantity must be greater than zero.");
        if (unitCost < 0) throw new ArgumentOutOfRangeException(nameof(unitCost), "Unit cost cannot be negative.");
        if (totalCost < 0) throw new ArgumentOutOfRangeException(nameof(totalCost), "Total cost cannot be negative.");

        Id = id;
        TransferId = transferId;
        ItemId = itemId;
        UnitId = unitId;
        Quantity = quantity;
        BaseQuantity = baseQuantity;
        UnitCost = unitCost;
        TotalCost = totalCost;
    }

    public Guid Id { get; private set; }
    public Guid TransferId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid UnitId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal BaseQuantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal TotalCost { get; private set; }
}
