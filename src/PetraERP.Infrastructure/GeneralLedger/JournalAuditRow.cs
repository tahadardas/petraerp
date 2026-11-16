using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>
/// Durable audit trail for journal lifecycle events.
/// Action values match <see cref="PetraERP.Domain.GeneralLedger.JournalAuditAction"/> ordinals.
/// State values match <see cref="PetraERP.Domain.GeneralLedger.JournalEntryState"/> ordinals.
/// This table is append-only; no row may be updated or deleted after insertion.
/// </summary>
internal sealed class JournalAuditRow
{
    public Guid EventId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid JournalId { get; set; }
    public long Revision { get; set; }
    public byte Action { get; set; }
    public byte? PreviousState { get; set; }
    public byte NewState { get; set; }

    // Actor / correlation from JournalActionContext
    public Guid ActorUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }

    // Optional cross-entry link (reversal, correction)
    public Guid? RelatedEntryId { get; set; }

    // Insertion timestamp — immutable, set by DB default
    public DateTimeOffset CreatedAtUtc { get; set; }

    // Navigation
    public JournalEntryRow? Journal { get; set; }
}
