using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Purchases;

/// <summary>
/// A purchase invoice; with <see cref="ReturnOfInvoiceId"/> it is a posted return of one, and with
/// <see cref="ReversesInvoiceId"/> it is the posted reversal of one. A document is never two kinds at once.
/// </summary>
internal sealed class PurchaseInvoiceRow : ITrackedStorageRow
{
    public Guid? VarianceAccountId { get; set; }
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid PatternRevisionId { get; set; }
    public Guid SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? SupplierReference { get; set; }
    public int? SellerCode { get; set; }
    public int? DistributorCode { get; set; }
    public DateTimeOffset InvoiceDateUtc { get; set; }
    public int PaymentMethod { get; set; } = 1; // 1=Credit, 2=Cash
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; } = 1m;
    public int Status { get; set; }
    public string? Notes { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }

    /// <summary>Additions entered as document-level rows (those without a counter account).</summary>
    public decimal AdditionsTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? ReturnOfInvoiceId { get; set; }
    public Guid? ReversedByInvoiceId { get; set; }
    public Guid? ReversesInvoiceId { get; set; }
    public string? PostingKey { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? PostedByUserId { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<PurchaseInvoiceLineRow> Lines { get; set; } = [];
    public ICollection<PurchaseInvoiceAdjustmentRow> Adjustments { get; set; } = [];
    public PartyRow? Supplier { get; set; }
    public WarehouseRow? Warehouse { get; set; }
    public PatternRevisionRow? PatternRevision { get; set; }
}

internal sealed class PurchaseInvoiceLineRow
{
    public Guid Id { get; set; }
    public Guid PurchaseInvoiceId { get; set; }
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? Notes { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>On a return line, the original purchase line being returned.</summary>
    public Guid? SourceLineId { get; set; }

    public PurchaseInvoiceRow? Invoice { get; set; }
    public ItemRow? Item { get; set; }
    public UnitOfMeasureRow? Unit { get; set; }
}

/// <summary>A document-level discount (received) or addition with its own account (Al-Ameen's discounts-and-additions table).</summary>
internal sealed class PurchaseInvoiceAdjustmentRow
{
    public Guid Id { get; set; }
    public Guid PurchaseInvoiceId { get; set; }

    /// <summary>1 = Discount, 2 = Addition.</summary>
    public int Kind { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>Optional: the row is then booked between <see cref="AccountId"/> and this account, outside the supplier's balance and the totals.</summary>
    public Guid? CounterAccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal Percentage { get; set; }
    public string? Notes { get; set; }
    public Guid? CostCenterId { get; set; }
    public int DisplayOrder { get; set; }

    public PurchaseInvoiceRow? Invoice { get; set; }
    public PetraERP.Infrastructure.GeneralLedger.AccountRow? Account { get; set; }
}
