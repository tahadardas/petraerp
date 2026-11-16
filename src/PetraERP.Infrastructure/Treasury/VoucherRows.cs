using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Sales;

namespace PetraERP.Infrastructure.Treasury;

internal sealed class VoucherRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public byte Kind { get; set; }
    public Guid PatternRevisionId { get; set; }
    public Guid? PartyId { get; set; }
    public byte SettlementMethod { get; set; }
    public string? VoucherNumber { get; set; }
    public DateTimeOffset VoucherDateUtc { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public decimal Amount { get; set; }
    public byte Status { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? ApprovalReason { get; set; }
    public Guid? PartyControlAccountId { get; set; }
    public string? Notes { get; set; }
    public string? SourceNumber { get; set; }
    public DateOnly? SourceDate { get; set; }

    /// <summary>The client key of the successful posting; a retry with it replays, another key is rejected.</summary>
    public string? PostingKey { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public string? ReversalReason { get; set; }

    public Guid CreatedByUserId { get; set; }
    public Guid? PostedByUserId { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<VoucherAllocationRow> Allocations { get; set; } = [];
    public ICollection<VoucherLineRow> Lines { get; set; } = [];
    public ICollection<VoucherAllocationPlanRow> AllocationPlans { get; set; } = [];
    public PartyRow? Party { get; set; }
    public PatternRevisionRow? PatternRevision { get; set; }
}

/// <summary>Receipt money settled against one posted credit sales invoice.</summary>
internal sealed class VoucherAllocationRow
{
    public Guid Id { get; set; }
    public Guid VoucherId { get; set; }
    public Guid SalesInvoiceId { get; set; }
    public decimal Amount { get; set; }

    public VoucherRow? Voucher { get; set; }
    public SalesInvoiceRow? Invoice { get; set; }
}

/// <summary>Payment distribution: a supplier (its payable account) or a direct expense/other account.</summary>
internal sealed class VoucherLineRow
{
    public Guid Id { get; set; }
    public Guid VoucherId { get; set; }
    public Guid? PartyId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }

    public VoucherRow? Voucher { get; set; }
}
