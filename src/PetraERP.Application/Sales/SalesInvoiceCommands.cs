using PetraERP.Application.Common;
using PetraERP.Domain.Sales;

namespace PetraERP.Application.Sales;

public sealed record CreateSalesInvoiceLineCommand(
    Guid ItemId,
    Guid UnitId,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercentage = 0m,
    decimal DiscountAmount = 0m,
    decimal TaxPercentage = 0m,
    decimal TaxAmount = 0m,
    decimal PumpCost = 0m,
    decimal TransportCost = 0m,
    Guid? CostCenterId = null,
    Guid? WarehouseId = null,
    string? Notes = null,
    string? ConcreteGrade = null,
    int DisplayOrder = 0);

/// <summary>One row of the document-level discounts and additions table. The amount is in the invoice currency and is always positive.</summary>
public sealed record CreateSalesInvoiceAdjustmentCommand(
    SalesInvoiceAdjustmentKind Kind,
    Guid AccountId,
    decimal Amount,
    decimal Percentage = 0m,
    string? Notes = null,
    Guid? CostCenterId = null,
    int DisplayOrder = 0,
    Guid? CounterAccountId = null);

public sealed record CreateSalesInvoiceCommand(
    Guid PatternRevisionId,
    Guid CustomerId,
    Guid WarehouseId,
    DateTimeOffset? InvoiceDateUtc = null,
    PaymentMethod PaymentMethod = PaymentMethod.Credit,
    string? CurrencyCode = null,
    decimal? ExchangeRate = null,
    Guid? CostCenterId = null,
    string? Notes = null,
    IReadOnlyList<CreateSalesInvoiceLineCommand>? Lines = null)
{
    /// <summary>Optional caller-supplied stable identity (derived from the request key) so a retried create resolves to the same row.</summary>
    public Guid? DocumentId { get; init; }

    /// <summary>
    /// The account that carries the invoice total instead of the default one (the pattern cash account for a cash invoice, the
    /// customer's receivable account for a credit invoice). Null keeps the default. Accepted only where the pattern permits a
    /// document override for that role.
    /// </summary>
    public Guid? CustomerAccountId { get; init; }

    /// <summary>Al-Ameen header seller code (البائع); optional.</summary>
    public int? SellerCode { get; init; }

    /// <summary>Al-Ameen header distributor code (الموزع); optional.</summary>
    public int? DistributorCode { get; init; }

    /// <summary>Document-level discounts and additions, each booked to its own account.</summary>
    public IReadOnlyList<CreateSalesInvoiceAdjustmentCommand>? Adjustments { get; init; }
}

public sealed record UpdateSalesInvoiceCommand(
    Guid CustomerId,
    Guid WarehouseId,
    DateTimeOffset? InvoiceDateUtc = null,
    PaymentMethod PaymentMethod = PaymentMethod.Credit,
    string? CurrencyCode = null,
    decimal? ExchangeRate = null,
    Guid? CostCenterId = null,
    string? Notes = null,
    IReadOnlyList<CreateSalesInvoiceLineCommand>? Lines = null)
{
    /// <summary>The version the caller read; required so a stale draft never overwrites newer changes.</summary>
    public required ConcurrencyToken ExpectedVersion { get; init; }

    /// <summary>
    /// The account that carries the invoice total instead of the default one (the pattern cash account for a cash invoice, the
    /// customer's receivable account for a credit invoice). Null keeps the default. Accepted only where the pattern permits a
    /// document override for that role.
    /// </summary>
    public Guid? CustomerAccountId { get; init; }

    public int? SellerCode { get; init; }

    public int? DistributorCode { get; init; }

    /// <summary>Document-level discounts and additions; replaces the stored rows.</summary>
    public IReadOnlyList<CreateSalesInvoiceAdjustmentCommand>? Adjustments { get; init; }
}

public sealed record PostSalesInvoiceCommand(
    string IdempotencyKey,
    string? Notes = null)
{
    public string? ManualNumber { get; init; }
    public bool ConfirmCreditWarning { get; init; }
    public bool OverrideCreditPolicy { get; init; }
    public string? CreditOverrideReason { get; init; }

    /// <summary>Required only when the company policy authorizes a negative issue and the warehouse cannot cover the line.</summary>
    public string? NegativeStockReason { get; init; }
}

public sealed record ReverseSalesInvoiceCommand(
    string Reason);

/// <summary>One original invoice line and the quantity (in that line's unit) being returned.</summary>
public sealed record SalesReturnLineCommand(Guid SourceLineId, decimal Quantity);

/// <summary>
/// A partial or full return of a posted invoice, posted atomically at original price and cost. A cash invoice
/// return credits the cash account; a credit invoice return credits the customer's receivable.
/// </summary>
public sealed record CreateSalesReturnCommand(
    string IdempotencyKey,
    string Reason,
    IReadOnlyList<SalesReturnLineCommand> Lines);

public sealed record SalesInvoiceSearchQuery(
    Guid? CustomerId = null,
    Guid? WarehouseId = null,
    Guid? PatternRevisionId = null,
    SalesInvoiceStatus? Status = null,
    DateTimeOffset? FromDateUtc = null,
    DateTimeOffset? ToDateUtc = null,
    int Page = 1,
    int PageSize = 50)
{
    /// <summary>When set, lists only the posted returns of this original invoice.</summary>
    public Guid? ReturnOfInvoiceId { get; init; }

    /// <summary>Server-side text search over invoice number, customer code/name and notes.</summary>
    public string? Search { get; init; }

    /// <summary>
    /// Requested order. Null keeps the historical behaviour: the list is newest first (date descending) and navigation walks
    /// date ascending. Whatever is requested, ties are always broken by (date, created-at, id) in the same direction,
    /// so the order is total and pagination/navigation never skip or repeat a row.
    /// </summary>
    public SalesInvoiceSort? Sort { get; init; }
}

/// <summary>Columns the invoice list can be ordered by on the server (over the full filtered result, not only the loaded page).</summary>
public enum SalesInvoiceSortField
{
    Date = 0,
    InvoiceNumber = 1,
    Pattern = 2,
    Customer = 3,
    Warehouse = 4,
    PaymentMethod = 5,
    GrandTotal = 6,
    Currency = 7,
    Status = 8,
}

public sealed record SalesInvoiceSort(SalesInvoiceSortField Field, bool Descending);

/// <summary>
/// Direction of a stable, server-side move between invoices in the requested sort order (default: date, created-at, id ascending):
/// First/Last are the ends of that order, Next/Previous the adjacent rows after/before the origin.
/// </summary>
public enum SalesInvoiceNavigationDirection
{
    First = 0,
    Previous = 1,
    Next = 2,
    Last = 3,
}
