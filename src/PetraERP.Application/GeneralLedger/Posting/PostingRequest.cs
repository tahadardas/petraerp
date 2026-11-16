using PetraERP.Domain.Primitives;

namespace PetraERP.Application.GeneralLedger.Posting;

public sealed class PostingRequest
{
    private PostingRequest(PostingRequestScope scope, Guid key, Guid journalId, ExpectedJournalVersion version,
        AuditReason reason, PostingOperation operation, Guid? originalJournalId, ExpectedJournalVersion? originalVersion)
    {
        Scope = scope;
        IdempotencyKey = key;
        JournalId = journalId;
        ExpectedVersion = version;
        Reason = reason;
        Operation = operation;
        OriginalJournalId = originalJournalId;
        OriginalExpectedVersion = originalVersion;
        Fingerprint = PostingFingerprint.Calculate(this);
    }

    public PostingRequestScope Scope { get; }
    public Guid IdempotencyKey { get; }
    public Guid JournalId { get; }
    public ExpectedJournalVersion ExpectedVersion { get; }
    public AuditReason Reason { get; }
    public PostingOperation Operation { get; }
    public Guid? OriginalJournalId { get; }
    public ExpectedJournalVersion? OriginalExpectedVersion { get; }
    public PostingFingerprint Fingerprint { get; }

    public static PostingRequest Post(PostingRequestScope scope, Guid key, Guid journalId,
        ExpectedJournalVersion version, AuditReason reason)
    {
        Validate(scope, key, journalId, version, reason);
        return new PostingRequest(scope, key, journalId, version, reason, PostingOperation.PostJournal, null, null);
    }

    public static PostingRequest Reverse(PostingRequestScope scope, Guid key, Guid reversalJournalId,
        ExpectedJournalVersion reversalVersion, Guid originalJournalId, ExpectedJournalVersion originalVersion, AuditReason reason)
    {
        Validate(scope, key, reversalJournalId, reversalVersion, reason);
        ArgumentNullException.ThrowIfNull(originalVersion);
        if (originalJournalId == Guid.Empty || originalJournalId == reversalJournalId)
        {
            throw new ArgumentException("A different, nonempty original journal ID is required.", nameof(originalJournalId));
        }

        return new PostingRequest(scope, key, reversalJournalId, reversalVersion, reason,
            PostingOperation.PostJournalReversal, originalJournalId, originalVersion);
    }

    private static void Validate(PostingRequestScope scope, Guid key, Guid journalId,
        ExpectedJournalVersion version, AuditReason reason)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(reason);
        if (key == Guid.Empty)
        {
            throw new ArgumentException("A nonempty idempotency UUID is required.", nameof(key));
        }

        if (journalId == Guid.Empty)
        {
            throw new ArgumentException("Journal ID is required.", nameof(journalId));
        }
    }
}
