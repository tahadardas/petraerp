using PetraERP.Application.Common;
using PetraERP.Domain.Treasury;

namespace PetraERP.Application.Treasury;

public enum SettlementStateFilter { All = 0, Open = 1, Partial = 2, Settled = 3 }
public sealed record InvoiceSettlementCommand(Guid InvoiceId, decimal InvoiceAmount, decimal SettlementAmount, decimal CrossRate);
public sealed record ApplyAdvanceCommand(ConcurrencyToken ExpectedVersion, string IdempotencyKey, string Reason, DateTimeOffset DateUtc,
    IReadOnlyList<InvoiceSettlementCommand> Lines);
public sealed record VoucherActionCommand(ConcurrencyToken ExpectedVersion, string Reason);
public sealed record SettlementInvoiceResult(Guid InvoiceId, Guid PartyId, string? Number, DateTimeOffset DateUtc, string CurrencyCode,
    decimal ExchangeRate, decimal Total, decimal Returned, decimal Paid, decimal Outstanding, decimal OutstandingBase, SettlementStateFilter State);
public sealed record SettlementInvoicePageResult(int Page, int PageSize, int TotalCount, IReadOnlyList<SettlementInvoiceResult> Items);
public sealed record InvoiceSettlementResult(Guid Id, Guid InvoiceId, string? InvoiceNumber, DateTimeOffset InvoiceDateUtc,
    string InvoiceCurrency, decimal InvoiceAmount, decimal SettlementAmount, decimal CrossRate);
public sealed record SettlementBatchResult(Guid Id, DateTimeOffset DateUtc, int State, string Reason, Guid? JournalId, byte[] Version,
    IReadOnlyList<InvoiceSettlementResult> Applications)
{
    public Guid? ReversalJournalId { get; init; }
    public DateTimeOffset? ReversedAtUtc { get; init; }
    public Guid? ReversedBy { get; init; }
    public string? ReversalReason { get; init; }
    public bool IsInitial { get; init; }
}

public interface ISettlementService
{
    Task<SettlementInvoicePageResult> InvoicesAsync(Guid company, Guid branch, Guid party, VoucherKind kind, string? currency,
        SettlementStateFilter state, int page, int pageSize, CancellationToken token);
    Task<VoucherDetailResult> ApproveAsync(Guid company, Guid branch, Guid voucherId, Guid actor, VoucherActionCommand command, CancellationToken token);
    Task<VoucherDetailResult> ApplyAdvanceAsync(Guid company, Guid branch, Guid voucherId, Guid actor, ApplyAdvanceCommand command, CancellationToken token);
    Task<VoucherDetailResult> ReverseSettlementAsync(Guid company, Guid branch, Guid voucherId, Guid batchId, Guid actor, VoucherActionCommand command, CancellationToken token);
}
