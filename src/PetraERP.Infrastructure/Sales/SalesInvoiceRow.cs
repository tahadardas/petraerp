using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Sales;

internal sealed class SalesInvoiceRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid PatternRevisionId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid WarehouseId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTimeOffset InvoiceDateUtc { get; set; }
    public int PaymentMethod { get; set; } = 1; // 1=Credit, 2=Cash
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; } = 1.0m;
    public int Status { get; set; } // 0=Draft, 1=Approved, 2=Posted, 3=Reversed, 4=Cancelled
    public Guid? CostCenterId { get; set; }
    public string? Notes { get; set; }
    /// <summary>Invoice-level override of the account that carries the total; null means the rule-resolved default.</summary>
    public Guid? CustomerAccountId { get; set; }
    public int? SellerCode { get; set; }
    public int? DistributorCode { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal AdditionsTotal { get; set; }
    public decimal GrandTotal { get; set; }

    public Guid? JournalEntryId { get; set; }
    public Guid? ReversedByInvoiceId { get; set; }
    public Guid? ReversesInvoiceId { get; set; }

    /// <summary>Set on a posted partial-return document; it names the original posted invoice.</summary>
    public Guid? ReturnOfInvoiceId { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigations
    public ICollection<SalesInvoiceLineRow> Lines { get; set; } = [];
    public ICollection<SalesInvoiceAdjustmentRow> Adjustments { get; set; } = [];
    public PartyRow? Customer { get; set; }
    public WarehouseRow? Warehouse { get; set; }
    public PatternRevisionRow? PatternRevision { get; set; }
}
