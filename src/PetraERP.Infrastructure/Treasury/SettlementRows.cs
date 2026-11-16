namespace PetraERP.Infrastructure.Treasury;

/// <summary>Editable invoice allocation instructions. Only a never-posted draft may replace them.</summary>
internal sealed class VoucherAllocationPlanRow
{
    public Guid Id { get; set; }
    public Guid VoucherId { get; set; }
    public Guid? SalesInvoiceId { get; set; }
    public Guid? PurchaseInvoiceId { get; set; }
    public Guid? OpenItemId { get; set; }
    public decimal InvoiceAmount { get; set; }
    public decimal SettlementAmount { get; set; }
    public decimal CrossRate { get; set; }
    public VoucherRow? Voucher { get; set; }
}

/// <summary>Immutable application payload plus versioned lifecycle. Reversals retain all applications.</summary>
internal sealed class SettlementBatchRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid VoucherId { get; set; }
    public bool IsInitial { get; set; }
    public int State { get; set; } = 1;
    public DateTimeOffset DateUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestKey { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public Guid ActorId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public Guid? JournalId { get; set; }
    public Guid? ReversalJournalId { get; set; }
    public Guid? ReversedBy { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public string? ReversalReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public VoucherRow? Voucher { get; set; }
    public ICollection<SettlementApplicationRow> Applications { get; set; } = [];
}

internal sealed class SettlementApplicationRow
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid VoucherId { get; set; }
    public Guid? SalesInvoiceId { get; set; }
    public Guid? PurchaseInvoiceId { get; set; }
    public Guid? OpenItemId { get; set; }
    public string InvoiceCurrency { get; set; } = string.Empty;
    public string SettlementCurrency { get; set; } = string.Empty;
    public decimal InvoiceAmount { get; set; }
    public decimal SettlementAmount { get; set; }
    public decimal CrossRate { get; set; }
    public decimal InvoiceRate { get; set; }
    public decimal SettlementRate { get; set; }
    public decimal BookedBaseAmount { get; set; }
    public decimal SettlementBaseAmount { get; set; }
    public Guid AccountId { get; set; }
    public SettlementBatchRow? Batch { get; set; }
    public VoucherRow? Voucher { get; set; }
}
