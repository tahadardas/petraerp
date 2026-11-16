using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Treasury;

public enum SettlementStateFilter { All = 0, Open = 1, Partial = 2, Settled = 3 }
public sealed record InvoiceSettlementInputDto
{
    public Guid InvoiceId { get; init; }
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)] public string InvoiceAmount { get; init; } = string.Empty;
    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)] public string SettlementAmount { get; init; } = string.Empty;
    [Required, RegularExpression(FinancialDecimalText.RatePattern)] public string CrossRate { get; init; } = "1.00000000";
}
public sealed record InvoiceSettlementDto
{
    public Guid Id { get; init; }
    public Guid InvoiceId { get; init; }
    public string? InvoiceNumber { get; init; }
    public DateTimeOffset InvoiceDateUtc { get; init; }
    public string InvoiceCurrency { get; init; } = string.Empty;
    public string InvoiceAmount { get; init; } = string.Empty;
    public string SettlementAmount { get; init; } = string.Empty;
    public string CrossRate { get; init; } = string.Empty;
}
public sealed record SettlementBatchDto
{
    public bool IsInitial { get; init; }
    public Guid? ReversalJournalId { get; init; }
    public DateTimeOffset? ReversedAtUtc { get; init; }
    public Guid? ReversedBy { get; init; }
    public string? ReversalReason { get; init; }
    public Guid Id { get; init; }
    public DateTimeOffset DateUtc { get; init; }
    public int State { get; init; }
    public string Reason { get; init; } = string.Empty;
    public Guid? JournalId { get; init; }
    public string Version { get; init; } = string.Empty;
    public IReadOnlyList<InvoiceSettlementDto> Applications { get; init; } = [];
}
public sealed record SettlementInvoiceDto
{
    public Guid InvoiceId { get; init; }
    public Guid PartyId { get; init; }
    public string? Number { get; init; }
    public DateTimeOffset DateUtc { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public string ExchangeRate { get; init; } = string.Empty;
    public string Total { get; init; } = string.Empty;
    public string Returned { get; init; } = string.Empty;
    public string Paid { get; init; } = string.Empty;
    public string Outstanding { get; init; } = string.Empty;
    public string OutstandingBase { get; init; } = string.Empty;
    public SettlementStateFilter State { get; init; }
}
public sealed record SettlementInvoicePageResponse
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public IReadOnlyList<SettlementInvoiceDto> Items { get; init; } = [];
}
public sealed record VoucherActionRequest
{
    [Required] public string ExpectedVersion { get; init; } = string.Empty;
    [Required, MaxLength(500)] public string Reason { get; init; } = string.Empty;
}
public sealed record ApplyAdvanceRequest
{
    [Required] public string ExpectedVersion { get; init; } = string.Empty;
    [Required, MaxLength(128)] public string IdempotencyKey { get; init; } = string.Empty;
    [Required, MaxLength(500)] public string Reason { get; init; } = string.Empty;
    public DateTimeOffset DateUtc { get; init; }
    [Required] public IReadOnlyList<InvoiceSettlementInputDto> Lines { get; init; } = [];
}
