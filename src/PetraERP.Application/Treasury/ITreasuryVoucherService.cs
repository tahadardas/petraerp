using PetraERP.Application.Common;
using PetraERP.Domain.Treasury;

namespace PetraERP.Application.Treasury;

public sealed record VoucherAllocationCommand(Guid SalesInvoiceId, decimal Amount);

/// <summary>A payment distribution line: exactly one of a supplier (its payable) or a direct account.</summary>
public sealed record VoucherLineCommand(Guid? PartyId, Guid? AccountId, decimal Amount, Guid? CostCenterId = null, string? Description = null);

public sealed record SaveVoucherCommand(
    VoucherKind Kind,
    Guid PatternRevisionId,
    Guid? PartyId,
    SettlementMethod SettlementMethod,
    DateTimeOffset VoucherDateUtc,
    string? CurrencyCode,
    decimal? ExchangeRate,
    decimal Amount,
    string? Notes,
    IReadOnlyList<VoucherAllocationCommand> Allocations,
    IReadOnlyList<VoucherLineCommand> Lines)
{
    /// <summary>Required on update: the version the caller read.</summary>
    public ConcurrencyToken? ExpectedVersion { get; init; }
    public IReadOnlyList<InvoiceSettlementCommand> InvoiceAllocations { get; init; } = [];
    public Guid ActorUserId { get; init; }

    /// <summary>Optional caller-supplied stable identity (derived from the request key) so a retried create resolves to the same row.</summary>
    public Guid? DocumentId { get; init; }

    /// <summary>The number and date printed on the source (paper) document this voucher records (Al-Ameen "رقم الأصل / تاريخ الأصل").</summary>
    public string? SourceNumber { get; init; }
    public DateOnly? SourceDate { get; init; }
}

public sealed record PostVoucherCommand(ConcurrencyToken ExpectedVersion, string IdempotencyKey, string? ManualNumber = null);

public sealed record ReverseVoucherCommand(ConcurrencyToken ExpectedVersion, string Reason);

public sealed record VoucherSearchQuery(
    VoucherKind? Kind = null,
    Guid? PartyId = null,
    VoucherStatus? Status = null,
    DateTimeOffset? FromDateUtc = null,
    DateTimeOffset? ToDateUtc = null,
    Guid? SalesInvoiceId = null,
    int Page = 1,
    int PageSize = 50,
    Guid? JournalEntryId = null);

public sealed record VoucherAllocationResult(Guid Id, Guid SalesInvoiceId, string? InvoiceNumber, DateTimeOffset InvoiceDateUtc, decimal Amount);

public sealed record VoucherLineResult(Guid Id, Guid? PartyId, string? PartyName, Guid? AccountId, string? AccountCode,
    Guid? CostCenterId, decimal Amount, string? Description, int DisplayOrder);

public sealed record VoucherSummaryResult(Guid Id, VoucherKind Kind, string? VoucherNumber, DateTimeOffset VoucherDateUtc,
    Guid? PartyId, string? PartyName, string CurrencyCode, decimal Amount, VoucherStatus Status, Guid? JournalEntryId, byte[] RowVersion);

public sealed record VoucherDetailResult(
    Guid Id,
    Guid CompanyId,
    Guid BranchId,
    VoucherKind Kind,
    Guid PatternRevisionId,
    string PatternCode,
    string PatternName,
    Guid? PartyId,
    string? PartyCode,
    string? PartyName,
    SettlementMethod SettlementMethod,
    string? VoucherNumber,
    DateTimeOffset VoucherDateUtc,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal Amount,
    decimal AllocatedAmount,
    decimal AdvanceAmount,
    VoucherStatus Status,
    string? Notes,
    Guid? JournalEntryId,
    Guid? ReversalJournalEntryId,
    string? ReversalReason,
    DateTimeOffset? PostedAtUtc,
    DateTimeOffset? ReversedAtUtc,
    byte[] RowVersion,
    IReadOnlyList<VoucherAllocationResult> Allocations,
    IReadOnlyList<VoucherLineResult> Lines)
{
    public IReadOnlyList<InvoiceSettlementResult> InvoiceAllocations { get; init; } = [];
    public IReadOnlyList<SettlementBatchResult> SettlementBatches { get; init; } = [];

    public string? SourceNumber { get; init; }
    public DateOnly? SourceDate { get; init; }

    /// <summary>Who created, last changed, posted and reversed the voucher (Al-Ameen shows these on every voucher).</summary>
    public VoucherAuditResult? Audit { get; init; }
}

/// <summary>Creation, last-change, posting and reversal trail of a voucher; names are the Arabic display names of the users.</summary>
public sealed record VoucherAuditResult(DateTimeOffset CreatedAtUtc, string? CreatedBy, DateTimeOffset UpdatedAtUtc,
    string? PostedBy, DateTimeOffset? PostedAtUtc, string? ReversedBy, DateTimeOffset? ReversedAtUtc);

public sealed record VoucherPageResult(int Page, int PageSize, int TotalCount, IReadOnlyList<VoucherSummaryResult> Items);

/// <summary>A posted credit invoice with its authoritative paid and outstanding balance (invoice currency).</summary>
public sealed record OutstandingInvoiceResult(Guid InvoiceId, string? InvoiceNumber, DateTimeOffset InvoiceDateUtc, string CurrencyCode,
    decimal GrandTotal, decimal ReturnedAmount, decimal PaidAmount, decimal Outstanding);

/// <summary>Receipt and payment vouchers posted atomically through the canonical ledger.</summary>
public interface ITreasuryVoucherService
{
    Task<VoucherPageResult> ListAsync(Guid companyId, Guid branchId, VoucherSearchQuery query, CancellationToken cancellationToken = default);

    Task<VoucherDetailResult> GetAsync(Guid companyId, Guid branchId, Guid voucherId, CancellationToken cancellationToken = default);

    Task<VoucherDetailResult> CreateDraftAsync(Guid companyId, Guid branchId, Guid userId, SaveVoucherCommand command,
        CancellationToken cancellationToken = default);

    Task<VoucherDetailResult> UpdateDraftAsync(Guid companyId, Guid branchId, Guid voucherId, SaveVoucherCommand command,
        CancellationToken cancellationToken = default);

    Task DeleteDraftAsync(Guid companyId, Guid branchId, Guid voucherId, ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken = default);

    Task<VoucherDetailResult> PostAsync(Guid companyId, Guid branchId, Guid voucherId, Guid userId, PostVoucherCommand command,
        CancellationToken cancellationToken = default);

    Task<VoucherDetailResult> ReverseAsync(Guid companyId, Guid branchId, Guid voucherId, Guid userId, ReverseVoucherCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Posted credit invoices of a customer in this branch and currency that still have an outstanding balance.</summary>
    Task<IReadOnlyList<OutstandingInvoiceResult>> GetOutstandingInvoicesAsync(Guid companyId, Guid branchId, Guid customerId,
        string currencyCode, CancellationToken cancellationToken = default);
}
