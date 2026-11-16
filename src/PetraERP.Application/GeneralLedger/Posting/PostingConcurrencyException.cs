namespace PetraERP.Application.GeneralLedger.Posting;

/// <summary>
/// Thrown when the expected database rowversion for a journal entry does not match its current stored version.
/// </summary>
public sealed class PostingConcurrencyException : InvalidOperationException
{
    public PostingConcurrencyException(Guid journalId, string message)
        : base(message)
    {
        JournalId = journalId;
    }

    public Guid JournalId { get; }
}
