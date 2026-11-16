using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Sales;

public enum SalesInvoiceStatus
{
    Draft = 0,
    Approved = 1,
    Posted = 2,
    Reversed = 3,
    Cancelled = 4,
}

public enum PaymentMethod
{
    Credit = 1,
    Cash = 2,
}

public sealed record SalesInvoiceLineDto
{
    public Guid Id { get; init; }
    public Guid SalesInvoiceId { get; init; }
    public Guid ItemId { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string ItemNameArabic { get; init; } = string.Empty;
    public Guid UnitId { get; init; }
    public string UnitCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Quantity { get; init; } = "1.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string UnitPrice { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string DiscountPercentage { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string DiscountAmount { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TaxPercentage { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TaxAmount { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string PumpCost { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TransportCost { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string LineTotal { get; init; } = "0.0000";

    public Guid? CostCenterId { get; init; }
    public string? CostCenterCode { get; init; }
    public Guid? WarehouseId { get; init; }
    public string? WarehouseCode { get; init; }
    public string? Notes { get; init; }
    public int DisplayOrder { get; init; }
    public string? ConcreteGrade { get; init; }

    /// <summary>On a return line, the original invoice line it returns.</summary>
    public Guid? SourceLineId { get; init; }

    /// <summary>On an original line, the quantity already returned by posted returns.</summary>
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string ReturnedQuantity { get; init; } = "0.0000";
}

public sealed record SalesInvoiceSummaryDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid BranchId { get; init; }
    public Guid PatternRevisionId { get; init; }
    public string PatternCode { get; init; } = string.Empty;
    public string PatternName { get; init; } = string.Empty;
    public Guid CustomerId { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public Guid WarehouseId { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public string? InvoiceNumber { get; init; }
    public DateTimeOffset InvoiceDateUtc { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.RatePattern)]
    public string ExchangeRate { get; init; } = "1.00000000";

    public SalesInvoiceStatus Status { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string SubTotal { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string DiscountTotal { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TaxTotal { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string AdditionsTotal { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string GrandTotal { get; init; } = "0.0000";

    public Guid? JournalEntryId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? PostedAtUtc { get; init; }

    /// <summary>Set when this row is a posted return of the named original invoice.</summary>
    public Guid? ReturnOfInvoiceId { get; init; }

    /// <summary>Posted credit originals only, in the invoice currency: receipts allocated, posted returns, and the remainder.</summary>
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? SettledAmount { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? ReturnedAmount { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? OutstandingAmount { get; init; }

    /// <summary>Opaque concurrency version of this row.</summary>
    [Required]
    public string Version { get; init; } = string.Empty;
}

public sealed record SalesInvoiceDetailDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid BranchId { get; init; }
    public Guid PatternRevisionId { get; init; }
    public string PatternCode { get; init; } = string.Empty;
    public string PatternName { get; init; } = string.Empty;
    public Guid CustomerId { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public Guid WarehouseId { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public string? InvoiceNumber { get; init; }
    public DateTimeOffset InvoiceDateUtc { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.RatePattern)]
    public string ExchangeRate { get; init; } = "1.00000000";

    public SalesInvoiceStatus Status { get; init; }
    public Guid? CostCenterId { get; init; }
    /// <summary>Account that carries the invoice total instead of the default (cash account or customer receivable); null keeps the default.</summary>
    public Guid? CustomerAccountId { get; init; }
    [Range(0, 999999)]
    public int? SellerCode { get; init; }
    [Range(0, 999999)]
    public int? DistributorCode { get; init; }
    public string? CostCenterCode { get; init; }
    public string? Notes { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string SubTotal { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string DiscountTotal { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TaxTotal { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string AdditionsTotal { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string GrandTotal { get; init; } = "0.0000";

    public Guid? JournalEntryId { get; init; }
    public Guid? ReversedByInvoiceId { get; init; }
    public Guid? ReversesInvoiceId { get; init; }

    /// <summary>Set when this document is a posted return of the named original invoice.</summary>
    public Guid? ReturnOfInvoiceId { get; init; }

    /// <summary>Posted credit originals only, in the invoice currency: receipts allocated, posted returns, and the remainder.</summary>
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? SettledAmount { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? ReturnedAmount { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? OutstandingAmount { get; init; }

    public bool CustomerIsCashCustomer { get; init; }
    public bool CreditBlocked { get; init; }
    public int CreditPolicyKind { get; init; }
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? CreditExposureBase { get; init; }
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? CreditLimitBase { get; init; }
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? CreditProjectedExposureBase { get; init; }
    public bool CreditPolicyRequiresAction { get; init; }
    public bool CreditOverrideApplied { get; init; }
    public string? CreditOverrideReason { get; init; }
    public Guid? CreditPolicyActorUserId { get; init; }
    public DateTimeOffset? CreditPolicyRecordedAtUtc { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public Guid? ApprovedByUserId { get; init; }
    public DateTimeOffset? ApprovedAtUtc { get; init; }
    public Guid? PostedByUserId { get; init; }
    public DateTimeOffset? PostedAtUtc { get; init; }

    /// <summary>Opaque concurrency version; send it back as ExpectedVersion when updating or approving.</summary>
    [Required]
    public string Version { get; init; } = string.Empty;

    public IReadOnlyList<SalesInvoiceLineDto> Lines { get; init; } = [];

    /// <summary>Document-level discounts and additions (their amounts are already inside DiscountTotal/AdditionsTotal).</summary>
    public IReadOnlyList<SalesInvoiceAdjustmentDto> Adjustments { get; init; } = [];
}

public sealed record CreateSalesInvoiceLineRequest
{
    [Required]
    public Guid ItemId { get; init; }

    [Required]
    public Guid UnitId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Quantity { get; init; } = "1.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string UnitPrice { get; init; } = "0.0000";

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? DiscountPercentage { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? DiscountAmount { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? TaxPercentage { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? TaxAmount { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? PumpCost { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? TransportCost { get; init; }

    public Guid? CostCenterId { get; init; }
    public Guid? WarehouseId { get; init; }
    public string? Notes { get; init; }
    public string? ConcreteGrade { get; init; }
    public int DisplayOrder { get; init; }
}

/// <summary>The kind of a document-level adjustment row.</summary>
public enum SalesInvoiceAdjustmentKind
{
    Discount = 1,
    Addition = 2,
}

/// <summary>One row of the invoice's discounts-and-additions table; booked to its own account when the invoice is posted.</summary>
public sealed record SalesInvoiceAdjustmentRequest
{
    public SalesInvoiceAdjustmentKind Kind { get; init; }

    [Required]
    public Guid AccountId { get; init; }

    /// <summary>Optional counter account: the row is booked between the two accounts, outside the party's balance and the invoice total.</summary>
    public Guid? CounterAccountId { get; init; }

    /// <summary>A positive amount in the invoice currency.</summary>
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Amount { get; init; } = "0.0000";

    /// <summary>Informational share of the lines' subtotal (0–100) when the row was entered as a percentage.</summary>
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? Percentage { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }

    public Guid? CostCenterId { get; init; }
    public int DisplayOrder { get; init; }
}

public sealed record SalesInvoiceAdjustmentDto
{
    public Guid Id { get; init; }
    public SalesInvoiceAdjustmentKind Kind { get; init; }
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountNameArabic { get; init; } = string.Empty;
    public Guid? CounterAccountId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Amount { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Percentage { get; init; } = "0.0000";

    public string? Notes { get; init; }
    public Guid? CostCenterId { get; init; }
    public int DisplayOrder { get; init; }
}

public sealed record CreateSalesInvoiceRequest
{
    [Required]
    public Guid PatternRevisionId { get; init; }

    /// <summary>Required for credit. Omit for the system walk-in cash customer.</summary>
    public Guid? CustomerId { get; init; }

    [Required]
    public Guid WarehouseId { get; init; }

    public DateTimeOffset? InvoiceDateUtc { get; init; }
    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Credit;

    /// <summary>Optional: when omitted the server uses the customer, then the pattern, then the company base currency.</summary>
    [StringLength(3, MinimumLength = 3)]
    public string? CurrencyCode { get; init; }

    [RegularExpression(FinancialDecimalText.RatePattern)]
    public string? ExchangeRate { get; init; }

    public Guid? CostCenterId { get; init; }
    /// <summary>Account that carries the invoice total instead of the default (cash account or customer receivable); null keeps the default.</summary>
    public Guid? CustomerAccountId { get; init; }
    [Range(0, 999999)]
    public int? SellerCode { get; init; }
    [Range(0, 999999)]
    public int? DistributorCode { get; init; }
    public string? Notes { get; init; }

    public List<CreateSalesInvoiceLineRequest> Lines { get; init; } = [];

    /// <summary>Document-level discounts and additions, each with its own account.</summary>
    public List<SalesInvoiceAdjustmentRequest> Adjustments { get; init; } = [];
}

public sealed record UpdateSalesInvoiceRequest
{
    /// <summary>The Version last read; a stale value is rejected with 409 and nothing is overwritten.</summary>
    [Required, StringLength(12, MinimumLength = 12)]
    public string ExpectedVersion { get; init; } = string.Empty;

    /// <summary>Required for credit. Omit for the system walk-in cash customer.</summary>
    public Guid? CustomerId { get; init; }

    [Required]
    public Guid WarehouseId { get; init; }

    public DateTimeOffset? InvoiceDateUtc { get; init; }
    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Credit;

    /// <summary>Optional: when omitted the server uses the customer, then the pattern, then the company base currency.</summary>
    [StringLength(3, MinimumLength = 3)]
    public string? CurrencyCode { get; init; }

    [RegularExpression(FinancialDecimalText.RatePattern)]
    public string? ExchangeRate { get; init; }

    public Guid? CostCenterId { get; init; }
    /// <summary>Account that carries the invoice total instead of the default (cash account or customer receivable); null keeps the default.</summary>
    public Guid? CustomerAccountId { get; init; }
    [Range(0, 999999)]
    public int? SellerCode { get; init; }
    [Range(0, 999999)]
    public int? DistributorCode { get; init; }
    public string? Notes { get; init; }

    public List<CreateSalesInvoiceLineRequest> Lines { get; init; } = [];

    /// <summary>Document-level discounts and additions; replaces the stored rows.</summary>
    public List<SalesInvoiceAdjustmentRequest> Adjustments { get; init; } = [];
}

public sealed record ApproveSalesInvoiceRequest
{
    /// <summary>The Version last read; a stale value is rejected with 409.</summary>
    [Required, StringLength(12, MinimumLength = 12)]
    public string ExpectedVersion { get; init; } = string.Empty;
}

public sealed record PostSalesInvoiceRequest
{
    [MaxLength(128)]
    public string? ManualNumber { get; init; }
    [Required, MaxLength(128)]
    public string IdempotencyKey { get; init; } = string.Empty;

    public string? Notes { get; init; }
    public bool ConfirmCreditWarning { get; init; }
    public bool OverrideCreditPolicy { get; init; }
    [MaxLength(500)]
    public string? CreditOverrideReason { get; init; }

    /// <summary>Required only when the company policy allows a negative stock issue and the line exceeds the available quantity.</summary>
    [MaxLength(500)]
    public string? NegativeStockReason { get; init; }
}

public sealed record ReverseSalesInvoiceRequest
{
    [Required, MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record SalesReturnLineRequest
{
    [Required]
    public Guid SourceLineId { get; init; }

    /// <summary>Quantity in the original line's unit; it may not exceed the quantity still returnable.</summary>
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Quantity { get; init; } = string.Empty;
}

/// <summary>
/// Posts a return of an original posted invoice at its original price, rate and stock cost. A cash invoice return credits
/// the cash account; a credit invoice return reduces the customer's receivable. Retrying the same key and lines is safe.
/// </summary>
public sealed record CreateSalesReturnRequest
{
    [Required, MaxLength(128)]
    public string IdempotencyKey { get; init; } = string.Empty;

    [Required, MaxLength(500)]
    public string Reason { get; init; } = string.Empty;

    [Required, MinLength(1), MaxLength(200)]
    public List<SalesReturnLineRequest> Lines { get; init; } = [];
}

public sealed record SalesInvoiceNavigationResponse
{
    /// <summary>The adjacent invoice, or null when there is none in the requested direction.</summary>
    public Guid? InvoiceId { get; init; }
}

public sealed record SalesInvoicePageResponse
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public IReadOnlyList<SalesInvoiceSummaryDto> Items { get; init; } = [];
}
