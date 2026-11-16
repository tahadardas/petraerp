using PetraERP.Application.Common;
using PetraERP.Domain.Purchases;
using PetraERP.Domain.Sales;

namespace PetraERP.Application.Purchases;

public sealed record PurchaseLineCommand(Guid ItemId, Guid UnitId, decimal Quantity, decimal UnitCost, decimal DiscountAmount = 0m,
    decimal TaxAmount = 0m, Guid? CostCenterId = null, string? Notes = null);

/// <summary>One row of a purchase invoice's discounts-and-additions table; the amount is positive and in the invoice currency.</summary>
public sealed record PurchaseAdjustmentCommand(SalesInvoiceAdjustmentKind Kind, Guid AccountId, decimal Amount, decimal Percentage = 0m, string? Notes = null,
    Guid? CostCenterId = null, int DisplayOrder = 0, Guid? CounterAccountId = null);

public sealed record PurchaseAdjustmentResult(Guid Id, SalesInvoiceAdjustmentKind Kind, Guid AccountId, string AccountCode, string AccountNameArabic, decimal Amount,
    decimal Percentage, string? Notes, Guid? CostCenterId, int DisplayOrder, Guid? CounterAccountId);

public sealed record SavePurchaseInvoiceCommand(
    Guid PatternRevisionId,
    Guid SupplierId,
    Guid WarehouseId,
    DateTimeOffset InvoiceDateUtc,
    PaymentMethod PaymentMethod,
    string? CurrencyCode,
    decimal? ExchangeRate,
    string? SupplierReference,
    string? Notes,
    IReadOnlyList<PurchaseLineCommand> Lines)
{
    /// <summary>Required on update: the version the caller read.</summary>
    public ConcurrencyToken? ExpectedVersion { get; init; }

    /// <summary>Optional caller-supplied stable identity (derived from the request key) so a retried create resolves to the same row.</summary>
    public Guid? DocumentId { get; init; }

    public int? SellerCode { get; init; }

    public int? DistributorCode { get; init; }

    /// <summary>Document-level discounts (received) and additions, each booked to its own account.</summary>
    public IReadOnlyList<PurchaseAdjustmentCommand>? Adjustments { get; init; }
}

public sealed record PostPurchaseInvoiceCommand(ConcurrencyToken ExpectedVersion, string IdempotencyKey, string? ManualNumber = null);

public sealed record ApprovePurchaseInvoiceCommand(ConcurrencyToken ExpectedVersion, string? Notes = null);

/// <summary>
/// Reverses a posted purchase invoice: a linked reversal document, a mirrored journal, and stock movements that give the goods back at
/// their original cost. The request key makes the call idempotent, exactly as posting and returning are.
/// </summary>
public sealed record ReversePurchaseInvoiceCommand(ConcurrencyToken ExpectedVersion, string IdempotencyKey, string Reason);

public sealed record PurchaseReturnLineCommand(Guid SourceLineId, decimal Quantity);

public sealed record CreatePurchaseReturnCommand(string IdempotencyKey, string Reason, IReadOnlyList<PurchaseReturnLineCommand> Lines)
{
    public Guid? VarianceAccountId { get; init; }
}

public sealed record PurchaseSearchQuery(Guid? SupplierId = null, PurchaseInvoiceStatus? Status = null, DateTimeOffset? FromDateUtc = null,
    DateTimeOffset? ToDateUtc = null, Guid? ReturnOfInvoiceId = null, int Page = 1, int PageSize = 50);

public sealed record PurchaseLineResult(Guid Id, Guid ItemId, string ItemCode, string ItemName, Guid UnitId, string UnitCode, decimal Quantity,
    decimal UnitCost, decimal DiscountAmount, decimal TaxAmount, decimal LineTotal, Guid? CostCenterId, string? Notes, int DisplayOrder,
    Guid? SourceLineId, decimal ReturnedQuantity);

public sealed record PurchaseSummaryResult(Guid Id, string? InvoiceNumber, string? SupplierReference, DateTimeOffset InvoiceDateUtc, Guid SupplierId,
    string SupplierName, string CurrencyCode, decimal GrandTotal, PurchaseInvoiceStatus Status, Guid? ReturnOfInvoiceId, byte[] RowVersion)
{
    public decimal? SettledAmount { get; init; }
    public decimal? ReturnedAmount { get; init; }
    public decimal? OutstandingAmount { get; init; }
}

public sealed record PurchaseDetailResult(
    Guid Id,
    Guid CompanyId,
    Guid BranchId,
    Guid PatternRevisionId,
    string PatternCode,
    string PatternName,
    Guid SupplierId,
    string SupplierCode,
    string SupplierName,
    Guid WarehouseId,
    string WarehouseCode,
    string? InvoiceNumber,
    string? SupplierReference,
    DateTimeOffset InvoiceDateUtc,
    PaymentMethod PaymentMethod,
    string CurrencyCode,
    decimal ExchangeRate,
    PurchaseInvoiceStatus Status,
    string? Notes,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    Guid? JournalEntryId,
    Guid? ReturnOfInvoiceId,
    DateTimeOffset? PostedAtUtc,
    byte[] RowVersion,
    IReadOnlyList<PurchaseLineResult> Lines)
{
    public Guid? VarianceAccountId { get; init; }
    public Guid? ReversedByInvoiceId { get; init; }
    public Guid? ReversesInvoiceId { get; init; }
    public decimal? SettledAmount { get; init; }
    public decimal? ReturnedAmount { get; init; }
    public decimal? OutstandingAmount { get; init; }

    /// <summary>Additions entered as document-level rows (already inside the grand total).</summary>
    public decimal AdditionsTotal { get; init; }
    public int? SellerCode { get; init; }
    public int? DistributorCode { get; init; }

    public IReadOnlyList<PurchaseAdjustmentResult> Adjustments { get; init; } = [];
}

public sealed record PurchasePageResult(int Page, int PageSize, int TotalCount, IReadOnlyList<PurchaseSummaryResult> Items);

/// <summary>
/// Purchase invoices post stock-in at cost and credit the supplier payable (or cash); returns reverse part or all of a
/// posted invoice at its original price and rate, never more than was received and never below zero stock.
/// </summary>
public interface IPurchaseInvoiceService
{
    Task<PurchasePageResult> ListAsync(Guid companyId, Guid branchId, PurchaseSearchQuery query, CancellationToken cancellationToken = default);

    Task<PurchaseDetailResult> GetAsync(Guid companyId, Guid branchId, Guid invoiceId, CancellationToken cancellationToken = default);

    Task<PurchaseDetailResult> CreateDraftAsync(Guid companyId, Guid branchId, Guid userId, SavePurchaseInvoiceCommand command,
        CancellationToken cancellationToken = default);

    Task<PurchaseDetailResult> UpdateDraftAsync(Guid companyId, Guid branchId, Guid invoiceId, SavePurchaseInvoiceCommand command,
        CancellationToken cancellationToken = default);

    Task DeleteDraftAsync(Guid companyId, Guid branchId, Guid invoiceId, ConcurrencyToken expectedVersion, CancellationToken cancellationToken = default);

    Task<PurchaseDetailResult> ApproveAsync(Guid companyId, Guid branchId, Guid invoiceId, Guid userId, ApprovePurchaseInvoiceCommand command,
        CancellationToken cancellationToken = default);

    Task<PurchaseDetailResult> PostAsync(Guid companyId, Guid branchId, Guid invoiceId, Guid userId, PostPurchaseInvoiceCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Posts a return of the original invoice and returns the new return document.</summary>
    Task<PurchaseDetailResult> ReturnAsync(Guid companyId, Guid branchId, Guid invoiceId, Guid userId, CreatePurchaseReturnCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses a posted original purchase invoice: a linked reversal document, a mirrored journal, and stock movements that give the
    /// goods back at their original cost. The posted original is never edited; it is marked Reversed and points at its reversal.
    /// </summary>
    Task<PurchaseDetailResult> ReverseInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, Guid userId, ReversePurchaseInvoiceCommand command,
        CancellationToken cancellationToken = default);
}
