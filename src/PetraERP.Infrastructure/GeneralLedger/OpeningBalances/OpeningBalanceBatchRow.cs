using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger.OpeningBalances;

public enum OpeningBalanceBatchState : byte
{
    Draft = 1,
    Validated = 2,
    Approved = 3,
    Posted = 4,
    Reversed = 5,
}

public sealed class OpeningBalanceBatchRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FiscalYearId { get; set; }
    public Guid? BranchId { get; set; }
    public DateOnly OpeningDate { get; set; }
    public required string SourceType { get; set; } = "Manual"; // Manual, Rollover, MigrationCutover, MigrationHistory
    public string? SourceReference { get; set; }
    public string? SourceFingerprint { get; set; }
    public required string OriginalCurrency { get; set; }
    public required string BaseCurrency { get; set; }
    public byte ApprovalState { get; set; } = (byte)OpeningBalanceBatchState.Draft;
    public Guid ActorId { get; set; }
    public Guid? ApprovedByActorId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? PostedJournalId { get; set; }
    public Guid? ReversalJournalId { get; set; }
    public required string IdempotencyKey { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<OpeningBalanceLineRow> Lines { get; set; } = [];
}

public sealed class OpeningBalanceLineRow
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal DebitOriginal { get; set; }
    public decimal CreditOriginal { get; set; }
    public decimal ExchangeRate { get; set; }
    public decimal DebitBase { get; set; }
    public decimal CreditBase { get; set; }
    public string? Description { get; set; }
    public int LineOrder { get; set; }

    public OpeningBalanceBatchRow? Batch { get; set; }
}
