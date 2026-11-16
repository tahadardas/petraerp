using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>
/// Durable posting request registration for idempotency.
/// Scoped composite primary key: (CompanyId, BranchId, Operation, IdempotencyKey).
/// Fingerprint prevents semantic reuse of the same key with different parameters.
/// This table is append-only on registration; CompletedAtUtc is set exactly once on completion.
/// </summary>
internal sealed class PostingRequestRow
{
    public Guid IdempotencyKey { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ActorUserId { get; set; }

    /// <summary>Operation: 1=PostJournal, 2=PostJournalReversal.</summary>
    public byte Operation { get; set; }

    public Guid JournalId { get; set; }
    public long ExpectedRevision { get; set; }
    public required string ExpectedVersionBase64 { get; set; }

    public Guid? OriginalJournalId { get; set; }
    public long? OriginalExpectedRevision { get; set; }
    public string? OriginalVersionBase64 { get; set; }

    public required string Reason { get; set; }

    /// <summary>SHA-256 hex of the semantic request payload — used to detect parameter mismatch on replay.</summary>
    public required string FingerprintHex { get; set; }

    public DateTimeOffset RegisteredAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// ID of the journal entry produced on success; null until completed.
    /// On a replay match the stored outcome is returned without re-executing.
    /// </summary>
    public Guid? OutcomeJournalId { get; set; }
}
