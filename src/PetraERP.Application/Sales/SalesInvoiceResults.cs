using PetraERP.Domain.Sales;

namespace PetraERP.Application.Sales;

public sealed record SalesInvoiceLineResult(
    Guid Id,
    Guid SalesInvoiceId,
    Guid ItemId,
    string ItemCode,
    string ItemNameArabic,
    Guid UnitId,
    string UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercentage,
    decimal DiscountAmount,
    decimal TaxPercentage,
    decimal TaxAmount,
    decimal PumpCost,
    decimal TransportCost,
    decimal LineTotal,
    Guid? CostCenterId,
    string? CostCenterCode,
    Guid? WarehouseId,
    string? WarehouseCode,
    string? Notes,
    int DisplayOrder,
    string? ConcreteGrade)
{
    /// <summary>On a return line, the original invoice line it returns.</summary>
    public Guid? SourceLineId { get; init; }

    /// <summary>On an original line, the quantity already returned by posted partial returns.</summary>
    public decimal ReturnedQuantity { get; init; }
}

public sealed record SalesInvoiceAdjustmentResult(
    Guid Id,
    SalesInvoiceAdjustmentKind Kind,
    Guid AccountId,
    string AccountCode,
    string AccountNameArabic,
    decimal Amount,
    decimal Percentage,
    string? Notes,
    Guid? CostCenterId,
    int DisplayOrder)
{
    /// <summary>When set, the row is booked between its account and this one and leaves the customer's balance and the grand total unchanged.</summary>
    public Guid? CounterAccountId { get; init; }
}

public sealed record SalesInvoiceSummaryResult(
    Guid Id,
    Guid CompanyId,
    Guid BranchId,
    Guid PatternRevisionId,
    string PatternCode,
    string PatternName,
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    Guid WarehouseId,
    string WarehouseCode,
    string? InvoiceNumber,
    DateTimeOffset InvoiceDateUtc,
    PaymentMethod PaymentMethod,
    string CurrencyCode,
    decimal ExchangeRate,
    SalesInvoiceStatus Status,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal AdditionsTotal,
    decimal GrandTotal,
    Guid? JournalEntryId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PostedAtUtc,
    byte[] RowVersion)
{
    public Guid? ReturnOfInvoiceId { get; init; }
    public decimal? SettledAmount { get; init; }
    public decimal? ReturnedAmount { get; init; }
    public decimal? OutstandingAmount { get; init; }
}

public sealed record SalesInvoiceDetailResult(
    Guid Id,
    Guid CompanyId,
    Guid BranchId,
    Guid PatternRevisionId,
    string PatternCode,
    string PatternName,
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    Guid WarehouseId,
    string WarehouseCode,
    string? InvoiceNumber,
    DateTimeOffset InvoiceDateUtc,
    PaymentMethod PaymentMethod,
    string CurrencyCode,
    decimal ExchangeRate,
    SalesInvoiceStatus Status,
    Guid? CostCenterId,
    string? CostCenterCode,
    string? Notes,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal AdditionsTotal,
    decimal GrandTotal,
    Guid? JournalEntryId,
    Guid? ReversedByInvoiceId,
    Guid? ReversesInvoiceId,
    Guid? CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAtUtc,
    Guid? PostedByUserId,
    DateTimeOffset? PostedAtUtc,
    byte[] RowVersion,
    IReadOnlyList<SalesInvoiceLineResult> Lines)
{
    /// <summary>Set when this document is a partial return of the named original invoice.</summary>
    public Guid? ReturnOfInvoiceId { get; init; }

    /// <summary>The invoice-level account override; null when the default account applies.</summary>
    public Guid? CustomerAccountId { get; init; }

    public int? SellerCode { get; init; }

    public int? DistributorCode { get; init; }

    /// <summary>The document-level discounts and additions (their amounts are already part of the discount and additions totals).</summary>
    public IReadOnlyList<SalesInvoiceAdjustmentResult> Adjustments { get; init; } = [];

    /// <summary>
    /// Posted credit originals only (null otherwise), in the invoice currency: posted receipt allocations, posted returns,
    /// and what remains = total − returns − receipts. Computed from authoritative rows, never stored.
    /// </summary>
    public decimal? SettledAmount { get; init; }
    public decimal? ReturnedAmount { get; init; }
    public decimal? OutstandingAmount { get; init; }
    public bool CustomerIsCashCustomer { get; init; }
    public bool CreditBlocked { get; init; }
    public int CreditPolicyKind { get; init; }
    public decimal? CreditExposureBase { get; init; }
    public decimal? CreditLimitBase { get; init; }
    public decimal? CreditProjectedExposureBase { get; init; }
    public bool CreditPolicyRequiresAction { get; init; }
    public bool CreditOverrideApplied { get; init; }
    public string? CreditOverrideReason { get; init; }
    public Guid? CreditPolicyActorUserId { get; init; }
    public DateTimeOffset? CreditPolicyRecordedAtUtc { get; init; }

}

public sealed record SalesInvoicePageResult(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<SalesInvoiceSummaryResult> Items);
