using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>
/// Persistent journal entry header. State values match
/// <see cref="PetraERP.Domain.GeneralLedger.JournalEntryState"/> ordinals:
/// 0=Draft, 1=Validated, 2=Approved, 3=Posted.
/// </summary>
internal sealed class JournalEntryRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly AccountingDate { get; set; }
    public required string Description { get; set; }
    public byte State { get; set; }

    // Populated on posting
    public long? PostingSequence { get; set; }
    public Guid? PostingFiscalYearId { get; set; }
    public Guid? PostingPeriodId { get; set; }

    // Reversal / correction links (both nullable)
    public Guid? ReversesEntryId { get; set; }
    public Guid? ReversedByEntryId { get; set; }
    public Guid? CorrectsEntryId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigation
    public List<JournalLineRow> Lines { get; set; } = [];
    public List<JournalAuditRow> AuditEvents { get; set; } = [];
}
