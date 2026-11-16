using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Sales;

namespace PetraERP.Contracts.Purchases;

public enum PurchaseInvoiceStatus
{
    Draft = 0,
    Approved = 1,
    Posted = 2,
    Reversed = 3,
}

public sealed record PurchaseLineRequest
{
    [Required]
    public Guid ItemId { get; init; }

    /// <summary>The purchase unit (e.g. ton); stock is received in the item's base unit (e.g. kg) via an explicit conversion.</summary>
    [Required]
    public Guid UnitId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Quantity { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string UnitCost { get; init; } = string.Empty;

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? DiscountAmount { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? TaxAmount { get; init; }

    public Guid? CostCenterId { get; init; }

    [MaxLength(500)]
    public string? Notes { get; init; }
}

/// <summary>One row of the purchase invoice's discounts-and-additions table (the kind enum is shared with sales).</summary>
public sealed record PurchaseAdjustmentRequest
{
    public PetraERP.Contracts.Sales.SalesInvoiceAdjustmentKind Kind { get; init; }

    [Required]
    public Guid AccountId { get; init; }

    /// <summary>Optional counter account: the row is booked between the two accounts, outside the supplier's balance and the invoice total.</summary>
    public Guid? CounterAccountId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Amount { get; init; } = "0.0000";

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? Percentage { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }

    public Guid? CostCenterId { get; init; }
    public int DisplayOrder { get; init; }
}

public sealed record PurchaseAdjustmentDto
{
    public Guid Id { get; init; }
    public PetraERP.Contracts.Sales.SalesInvoiceAdjustmentKind Kind { get; init; }
    public Guid AccountId { get; init; }
    public Guid? CounterAccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountNameArabic { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Amount { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Percentage { get; init; } = "0.0000";

    public string? Notes { get; init; }
    public Guid? CostCenterId { get; init; }
    public int DisplayOrder { get; init; }
}

public sealed record SavePurchaseInvoiceRequest
{
    [Required]
    public Guid PatternRevisionId { get; init; }

    [Required]
    public Guid SupplierId { get; init; }

    [Required]
    public Guid WarehouseId { get; init; }

    [Required]
    public DateTimeOffset InvoiceDateUtc { get; init; }

    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Credit;

    /// <summary>Optional: when omitted the server uses the party, then the pattern, then the company base currency.</summary>
    [StringLength(3, MinimumLength = 3)]
    public string? CurrencyCode { get; init; }

    /// <summary>Optional: when omitted the server uses 1 for the base currency or the register rate effective on the document date.</summary>
    [RegularExpression(FinancialDecimalText.RatePattern)]
    public string? ExchangeRate { get; init; }

    [MaxLength(64)]
    public string? SupplierReference { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }
    [Range(0, 999999)]
    public int? SellerCode { get; init; }
    [Range(0, 999999)]
    public int? DistributorCode { get; init; }

    [Required, MinLength(1), MaxLength(200)]
    public List<PurchaseLineRequest> Lines { get; init; } = [];

    /// <summary>Document-level discounts (received) and additions, each with its own account.</summary>
    public List<PurchaseAdjustmentRequest> Adjustments { get; init; } = [];

    /// <summary>Required when updating: the Version last read.</summary>
    [StringLength(12, MinimumLength = 12)]
    public string? ExpectedVersion { get; init; }
}

public sealed record PostPurchaseInvoiceRequest
{
    [MaxLength(128)]
    public string? ManualNumber { get; init; }
    [Required, StringLength(12, MinimumLength = 12)]
    public string ExpectedVersion { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed record ApprovePurchaseInvoiceRequest
{
    [Required, StringLength(12, MinimumLength = 12)]
    public string ExpectedVersion { get; init; } = string.Empty;

    [MaxLength(500)]
    public string? Notes { get; init; }
}

/// <summary>
/// Reverses a posted purchase invoice. The reason is mandatory because the reversal is a linked accounting correction, and the request
/// key makes the call idempotent so a lost response can be retried without posting the reversal twice.
/// </summary>
public sealed record ReversePurchaseInvoiceRequest
{
    [Required, StringLength(12, MinimumLength = 12)]
    public string ExpectedVersion { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; init; } = string.Empty;

    [Required, MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record PurchaseReturnLineRequest
{
    [Required]
    public Guid SourceLineId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Quantity { get; init; } = string.Empty;
}

/// <summary>Returns goods to the supplier at the original price, tax and rate; never more than received or below zero stock.</summary>
public sealed record CreatePurchaseReturnRequest
{
    public Guid? VarianceAccountId { get; init; }
    [Required, MaxLength(128)]
    public string IdempotencyKey { get; init; } = string.Empty;

    [Required, MaxLength(500)]
    public string Reason { get; init; } = string.Empty;

    [Required, MinLength(1), MaxLength(200)]
    public List<PurchaseReturnLineRequest> Lines { get; init; } = [];
}

public sealed record PurchaseLineDto
{
    public Guid Id { get; init; }
    public Guid ItemId { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public Guid UnitId { get; init; }
    public string UnitCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Quantity { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string UnitCost { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string DiscountAmount { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TaxAmount { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string LineTotal { get; init; } = "0.0000";

    public Guid? CostCenterId { get; init; }
    public string? Notes { get; init; }
    public int DisplayOrder { get; init; }
    public Guid? SourceLineId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string ReturnedQuantity { get; init; } = "0.0000";
}

public sealed record PurchaseSummaryDto
{
    public Guid Id { get; init; }
    public string? InvoiceNumber { get; init; }
    public string? SupplierReference { get; init; }
    public DateTimeOffset InvoiceDateUtc { get; init; }
    public Guid SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string GrandTotal { get; init; } = "0.0000";

    public PurchaseInvoiceStatus Status { get; init; }
    public Guid? ReturnOfInvoiceId { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? SettledAmount { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? ReturnedAmount { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? OutstandingAmount { get; init; }

    [Required]
    public string Version { get; init; } = string.Empty;
}

public sealed record PurchaseDetailDto
{
    public Guid? VarianceAccountId { get; init; }
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid BranchId { get; init; }
    public Guid PatternRevisionId { get; init; }
    public string PatternCode { get; init; } = string.Empty;
    public string PatternName { get; init; } = string.Empty;
    public Guid SupplierId { get; init; }
    public string SupplierCode { get; init; } = string.Empty;
    public string SupplierName { get; init; } = string.Empty;
    public Guid WarehouseId { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public string? InvoiceNumber { get; init; }
    public string? SupplierReference { get; init; }
    public DateTimeOffset InvoiceDateUtc { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.RatePattern)]
    public string ExchangeRate { get; init; } = "1.00000000";

    public PurchaseInvoiceStatus Status { get; init; }
    public string? Notes { get; init; }
    [Range(0, 999999)]
    public int? SellerCode { get; init; }
    [Range(0, 999999)]
    public int? DistributorCode { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string SubTotal { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string DiscountTotal { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string TaxTotal { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string GrandTotal { get; init; } = "0.0000";

    public Guid? JournalEntryId { get; init; }
    public Guid? ReturnOfInvoiceId { get; init; }

    /// <summary>Set on the original once it has been reversed, and on the reversal document that points back at its original.</summary>
    public Guid? ReversedByInvoiceId { get; init; }

    public Guid? ReversesInvoiceId { get; init; }
    public DateTimeOffset? PostedAtUtc { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? SettledAmount { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? ReturnedAmount { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? OutstandingAmount { get; init; }

    [Required]
    public string Version { get; init; } = string.Empty;

    public IReadOnlyList<PurchaseLineDto> Lines { get; init; } = [];

    /// <summary>Additions entered as document-level rows (already inside GrandTotal).</summary>
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string AdditionsTotal { get; init; } = "0.0000";

    public IReadOnlyList<PurchaseAdjustmentDto> Adjustments { get; init; } = [];
}

public sealed record PurchasePageResponse
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public IReadOnlyList<PurchaseSummaryDto> Items { get; init; } = [];
}
