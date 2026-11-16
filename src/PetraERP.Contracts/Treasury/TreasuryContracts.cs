using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Treasury;

public enum VoucherKind
{
    Receipt = 1,
    Payment = 2,
}

public enum VoucherStatus
{
    Draft = 0,
    Approved = 1,
    Posted = 2,
    Reversed = 3,
}

public enum SettlementMethod
{
    Cash = 1,
    Bank = 2,
}

public sealed record VoucherAllocationRequest
{
    [Required]
    public Guid SalesInvoiceId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Amount { get; init; } = string.Empty;
}

public sealed record VoucherLineRequest
{
    public Guid? PartyId { get; init; }
    public Guid? AccountId { get; init; }
    public Guid? CostCenterId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Amount { get; init; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; init; }
}

/// <summary>
/// Creates or replaces a draft voucher. A receipt credits the customer and may allocate to posted credit invoices;
/// any unallocated amount is a customer advance. A payment distributes its amount over supplier/account lines.
/// </summary>
public sealed record SaveVoucherRequest
{
    public VoucherKind Kind { get; init; }

    [Required]
    public Guid PatternRevisionId { get; init; }

    public Guid? PartyId { get; init; }
    public SettlementMethod SettlementMethod { get; init; } = SettlementMethod.Cash;

    [Required]
    public DateTimeOffset VoucherDateUtc { get; init; }

    /// <summary>Optional: when omitted the server uses the party, then the pattern, then the company base currency.</summary>
    [StringLength(3, MinimumLength = 3)]
    public string? CurrencyCode { get; init; }

    /// <summary>Optional: when omitted the server uses 1 for the base currency or the register rate effective on the document date.</summary>
    [RegularExpression(FinancialDecimalText.RatePattern)]
    public string? ExchangeRate { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Amount { get; init; } = string.Empty;

    [MaxLength(1000)]
    public string? Notes { get; init; }

    /// <summary>Optional: the number and date printed on the source (paper) document.</summary>
    [MaxLength(64)]
    public string? SourceNumber { get; init; }
    public DateOnly? SourceDate { get; init; }

    [MaxLength(200)]
    public List<VoucherAllocationRequest> Allocations { get; init; } = [];
    [MaxLength(200)] public IReadOnlyList<InvoiceSettlementInputDto> InvoiceAllocations { get; init; } = [];

    [MaxLength(200)]
    public List<VoucherLineRequest> Lines { get; init; } = [];

    /// <summary>Required when updating: the Version last read.</summary>
    [StringLength(12, MinimumLength = 12)]
    public string? ExpectedVersion { get; init; }
}

public sealed record PostVoucherRequest
{
    [MaxLength(128)]
    public string? ManualNumber { get; init; }
    [Required, StringLength(12, MinimumLength = 12)]
    public string ExpectedVersion { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed record ReverseVoucherRequest
{
    [Required, StringLength(12, MinimumLength = 12)]
    public string ExpectedVersion { get; init; } = string.Empty;

    [Required, MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record VoucherAllocationDto
{
    public Guid Id { get; init; }
    public Guid SalesInvoiceId { get; init; }
    public string? InvoiceNumber { get; init; }
    public DateTimeOffset InvoiceDateUtc { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Amount { get; init; } = "0.0000";
}

public sealed record VoucherLineDto
{
    public Guid Id { get; init; }
    public Guid? PartyId { get; init; }
    public string? PartyName { get; init; }
    public Guid? AccountId { get; init; }
    public string? AccountCode { get; init; }
    public Guid? CostCenterId { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Amount { get; init; } = "0.0000";

    public string? Description { get; init; }
    public int DisplayOrder { get; init; }
}

public sealed record VoucherSummaryDto
{
    public Guid Id { get; init; }
    public VoucherKind Kind { get; init; }
    public string? VoucherNumber { get; init; }
    public DateTimeOffset VoucherDateUtc { get; init; }
    public Guid? PartyId { get; init; }
    public string? PartyName { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Amount { get; init; } = "0.0000";

    public VoucherStatus Status { get; init; }
    public Guid? JournalEntryId { get; init; }

    [Required]
    public string Version { get; init; } = string.Empty;
}

/// <summary>Who created, last changed, posted and reversed a voucher, with Arabic display names.</summary>
public sealed record VoucherAuditDto
{
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string? CreatedBy { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public string? PostedBy { get; init; }
    public DateTimeOffset? PostedAtUtc { get; init; }
    public string? ReversedBy { get; init; }
    public DateTimeOffset? ReversedAtUtc { get; init; }
}

public sealed record VoucherDetailDto
{
    public VoucherAuditDto? Audit { get; init; }
    public string? SourceNumber { get; init; }
    public DateOnly? SourceDate { get; init; }
    public IReadOnlyList<InvoiceSettlementDto> InvoiceAllocations { get; init; } = [];
    public IReadOnlyList<SettlementBatchDto> SettlementBatches { get; init; } = [];
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid BranchId { get; init; }
    public VoucherKind Kind { get; init; }
    public Guid PatternRevisionId { get; init; }
    public string PatternCode { get; init; } = string.Empty;
    public string PatternName { get; init; } = string.Empty;
    public Guid? PartyId { get; init; }
    public string? PartyCode { get; init; }
    public string? PartyName { get; init; }
    public SettlementMethod SettlementMethod { get; init; }
    public string? VoucherNumber { get; init; }
    public DateTimeOffset VoucherDateUtc { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.RatePattern)]
    public string ExchangeRate { get; init; } = "1.00000000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Amount { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string AllocatedAmount { get; init; } = "0.0000";

    /// <summary>Receipt money not allocated to invoices: a customer advance on the receivable.</summary>
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string AdvanceAmount { get; init; } = "0.0000";

    public VoucherStatus Status { get; init; }
    public string? Notes { get; init; }
    public Guid? JournalEntryId { get; init; }
    public Guid? ReversalJournalEntryId { get; init; }
    public string? ReversalReason { get; init; }
    public DateTimeOffset? PostedAtUtc { get; init; }
    public DateTimeOffset? ReversedAtUtc { get; init; }

    [Required]
    public string Version { get; init; } = string.Empty;

    public IReadOnlyList<VoucherAllocationDto> Allocations { get; init; } = [];
    public IReadOnlyList<VoucherLineDto> Lines { get; init; } = [];
}

public sealed record VoucherPageResponse
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public IReadOnlyList<VoucherSummaryDto> Items { get; init; } = [];
}

public sealed record OutstandingInvoiceDto
{
    public Guid InvoiceId { get; init; }
    public string? InvoiceNumber { get; init; }
    public DateTimeOffset InvoiceDateUtc { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string GrandTotal { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string ReturnedAmount { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string PaidAmount { get; init; } = "0.0000";

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Outstanding { get; init; } = "0.0000";
}
