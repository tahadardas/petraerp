using PetraERP.Infrastructure.Inventory;

namespace PetraERP.Infrastructure.Sales;

internal sealed class SalesInvoiceLineRow
{
    public Guid Id { get; set; }
    public Guid SalesInvoiceId { get; set; }
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal PumpCost { get; set; }
    public decimal TransportCost { get; set; }
    public decimal LineTotal { get; set; }
    public Guid? CostCenterId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? Notes { get; set; }
    public int DisplayOrder { get; set; }
    public string? ConcreteGrade { get; set; }

    /// <summary>On a return line, the original invoice line being returned.</summary>
    public Guid? SourceLineId { get; set; }

    // Navigations
    public SalesInvoiceRow? Invoice { get; set; }
    public ItemRow? Item { get; set; }
    public UnitOfMeasureRow? Unit { get; set; }
}
