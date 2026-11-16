using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Inventory;

public sealed class StockMovement
{
    private StockMovement() { }

    public StockMovement(
        Guid id,
        Guid companyId,
        Guid branchId,
        Guid warehouseId,
        Guid itemId,
        Guid unitId,
        StockMovementKind movementKind,
        decimal quantity,
        decimal baseQuantity,
        decimal unitCost,
        decimal totalCost,
        DateTimeOffset movementDateUtc,
        string documentType,
        Guid? documentId = null,
        string? documentNumber = null,
        Guid? costCenterId = null,
        string? notes = null,
        Guid createdByUserId = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("Movement ID cannot be empty.", nameof(id));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID cannot be empty.", nameof(branchId));
        if (warehouseId == Guid.Empty) throw new ArgumentException("Warehouse ID cannot be empty.", nameof(warehouseId));
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID cannot be empty.", nameof(itemId));
        if (unitId == Guid.Empty) throw new ArgumentException("Unit ID cannot be empty.", nameof(unitId));
        if (!Enum.IsDefined(movementKind)) throw new ArgumentOutOfRangeException(nameof(movementKind), "Unknown movement kind.");
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        if (baseQuantity == 0) throw new ArgumentOutOfRangeException(nameof(baseQuantity), "Base quantity cannot be zero.");
        if (unitCost < 0) throw new ArgumentOutOfRangeException(nameof(unitCost), "Unit cost cannot be negative.");
        if (totalCost < 0) throw new ArgumentOutOfRangeException(nameof(totalCost), "Total cost cannot be negative.");
        if (string.IsNullOrWhiteSpace(documentType)) throw new ArgumentException("Document type cannot be empty.", nameof(documentType));

        // Invariant PT-091: Internal issue MUST be tied to a cost center (truck/plant/machine/job), not a customer
        if (movementKind == StockMovementKind.InternalIssue && (!costCenterId.HasValue || costCenterId.Value == Guid.Empty))
        {
            throw RuleViolation.Create(
                RuleViolation.CostCenterRequiredForIssue,
                "A cost center is strictly required for internal inventory issues.");
        }

        Id = id;
        CompanyId = companyId;
        BranchId = branchId;
        WarehouseId = warehouseId;
        ItemId = itemId;
        UnitId = unitId;
        MovementKind = movementKind;
        Quantity = quantity;
        BaseQuantity = baseQuantity;
        UnitCost = unitCost;
        TotalCost = totalCost;
        MovementDateUtc = movementDateUtc;
        DocumentType = documentType.Trim();
        DocumentId = documentId;
        DocumentNumber = string.IsNullOrWhiteSpace(documentNumber) ? null : documentNumber.Trim();
        CostCenterId = costCenterId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid UnitId { get; private set; }
    public StockMovementKind MovementKind { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal BaseQuantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal TotalCost { get; private set; }
    public DateTimeOffset MovementDateUtc { get; private set; }
    public string DocumentType { get; private set; } = string.Empty;
    public Guid? DocumentId { get; private set; }
    public string? DocumentNumber { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
