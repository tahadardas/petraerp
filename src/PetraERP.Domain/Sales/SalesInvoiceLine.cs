namespace PetraERP.Domain.Sales;

public sealed class SalesInvoiceLine
{
    private SalesInvoiceLine() { }

    public SalesInvoiceLine(
        Guid id,
        Guid salesInvoiceId,
        Guid itemId,
        Guid unitId,
        decimal quantity,
        decimal unitPrice,
        decimal discountPercentage = 0m,
        decimal discountAmount = 0m,
        decimal taxPercentage = 0m,
        decimal taxAmount = 0m,
        decimal pumpCost = 0m,
        decimal transportCost = 0m,
        Guid? costCenterId = null,
        Guid? warehouseId = null,
        string? notes = null,
        int displayOrder = 0,
        string? concreteGrade = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Line ID cannot be empty.", nameof(id));
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID cannot be empty.", nameof(itemId));
        if (unitId == Guid.Empty) throw new ArgumentException("Unit ID cannot be empty.", nameof(unitId));
        if (quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(quantity), "Line quantity must be greater than zero.");
        if (unitPrice < 0m) throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        if (discountPercentage < 0m || discountPercentage > 100m) throw new ArgumentOutOfRangeException(nameof(discountPercentage), "Discount percentage must be between 0 and 100.");
        if (taxPercentage < 0m || taxPercentage > 100m) throw new ArgumentOutOfRangeException(nameof(taxPercentage), "Tax percentage must be between 0 and 100.");
        if (pumpCost < 0m) throw new ArgumentOutOfRangeException(nameof(pumpCost), "Pump cost cannot be negative.");
        if (transportCost < 0m) throw new ArgumentOutOfRangeException(nameof(transportCost), "Transport cost cannot be negative.");

        Id = id;
        SalesInvoiceId = salesInvoiceId;
        ItemId = itemId;
        UnitId = unitId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercentage = discountPercentage;
        TaxPercentage = taxPercentage;
        PumpCost = pumpCost;
        TransportCost = transportCost;
        CostCenterId = costCenterId;
        WarehouseId = warehouseId;
        Notes = notes?.Trim();
        DisplayOrder = displayOrder;
        ConcreteGrade = string.IsNullOrWhiteSpace(concreteGrade) ? null : concreteGrade.Trim();

        CalculateLineTotals(discountAmount, taxAmount);
    }

    public Guid Id { get; private set; }
    public Guid SalesInvoiceId { get; internal set; }
    public Guid ItemId { get; private set; }
    public Guid UnitId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercentage { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxPercentage { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal PumpCost { get; private set; }
    public decimal TransportCost { get; private set; }
    public decimal LineTotal { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public Guid? WarehouseId { get; private set; }
    public string? Notes { get; private set; }
    public int DisplayOrder { get; private set; }
    public string? ConcreteGrade { get; private set; }

    public void Update(
        Guid itemId,
        Guid unitId,
        decimal quantity,
        decimal unitPrice,
        decimal discountPercentage,
        decimal discountAmount,
        decimal taxPercentage,
        decimal taxAmount,
        decimal pumpCost,
        decimal transportCost,
        Guid? costCenterId,
        Guid? warehouseId,
        string? notes,
        string? concreteGrade)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID cannot be empty.", nameof(itemId));
        if (unitId == Guid.Empty) throw new ArgumentException("Unit ID cannot be empty.", nameof(unitId));
        if (quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(quantity), "Line quantity must be greater than zero.");
        if (unitPrice < 0m) throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        if (discountPercentage < 0m || discountPercentage > 100m) throw new ArgumentOutOfRangeException(nameof(discountPercentage), "Discount percentage must be between 0 and 100.");
        if (taxPercentage < 0m || taxPercentage > 100m) throw new ArgumentOutOfRangeException(nameof(taxPercentage), "Tax percentage must be between 0 and 100.");
        if (pumpCost < 0m) throw new ArgumentOutOfRangeException(nameof(pumpCost), "Pump cost cannot be negative.");
        if (transportCost < 0m) throw new ArgumentOutOfRangeException(nameof(transportCost), "Transport cost cannot be negative.");

        ItemId = itemId;
        UnitId = unitId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercentage = discountPercentage;
        TaxPercentage = taxPercentage;
        PumpCost = pumpCost;
        TransportCost = transportCost;
        CostCenterId = costCenterId;
        WarehouseId = warehouseId;
        Notes = notes?.Trim();
        ConcreteGrade = string.IsNullOrWhiteSpace(concreteGrade) ? null : concreteGrade.Trim();

        CalculateLineTotals(discountAmount, taxAmount);
    }

    private void CalculateLineTotals(decimal providedDiscountAmount, decimal providedTaxAmount)
    {
        var baseAmount = Math.Round(Quantity * UnitPrice, 4, MidpointRounding.AwayFromZero);

        if (DiscountPercentage > 0m)
        {
            DiscountAmount = Math.Round(baseAmount * (DiscountPercentage / 100m), 4, MidpointRounding.AwayFromZero);
        }
        else if (providedDiscountAmount > 0m)
        {
            DiscountAmount = Math.Min(baseAmount, Math.Round(providedDiscountAmount, 4, MidpointRounding.AwayFromZero));
        }
        else
        {
            DiscountAmount = 0m;
        }

        var netAmount = baseAmount - DiscountAmount;

        if (TaxPercentage > 0m)
        {
            TaxAmount = Math.Round(netAmount * (TaxPercentage / 100m), 4, MidpointRounding.AwayFromZero);
        }
        else if (providedTaxAmount > 0m)
        {
            TaxAmount = Math.Round(providedTaxAmount, 4, MidpointRounding.AwayFromZero);
        }
        else
        {
            TaxAmount = 0m;
        }

        LineTotal = netAmount + TaxAmount + PumpCost + TransportCost;
    }
}
