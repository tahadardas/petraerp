namespace PetraERP.Domain.GeneralLedger;

public sealed class JournalReversal
{
    internal JournalReversal(JournalEntry originalEntry, JournalEntry reversalEntry)
    {
        OriginalEntry = originalEntry;
        ReversalEntry = reversalEntry;
    }

    public JournalEntry OriginalEntry { get; }
    public JournalEntry ReversalEntry { get; }

    public static JournalReversal Reconstitute(JournalEntry originalEntry, JournalEntry reversalEntry)
    {
        ArgumentNullException.ThrowIfNull(originalEntry);
        ArgumentNullException.ThrowIfNull(reversalEntry);
        return new JournalReversal(originalEntry, reversalEntry);
    }
}
