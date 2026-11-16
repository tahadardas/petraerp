namespace PetraERP.Domain.GeneralLedger;

/// <summary>Immutable audit intent; persistence must commit it with the journal change.</summary>
public sealed class JournalAuditEvent
{
    internal JournalAuditEvent(Guid companyId, Guid branchId, Guid journalId, long revision,
        JournalAuditAction action, JournalEntryState? previousState, JournalEntryState newState,
        JournalActionContext context, Guid? relatedEntryId)
        : this(Guid.CreateVersion7(), companyId, branchId, journalId, revision, action, previousState, newState, context, relatedEntryId)
    {
    }

    private JournalAuditEvent(Guid eventId, Guid companyId, Guid branchId, Guid journalId, long revision,
        JournalAuditAction action, JournalEntryState? previousState, JournalEntryState newState,
        JournalActionContext context, Guid? relatedEntryId)
    {
        EventId = eventId;
        CompanyId = companyId;
        BranchId = branchId;
        JournalId = journalId;
        Revision = revision;
        Action = action;
        PreviousState = previousState;
        NewState = newState;
        Context = context;
        RelatedEntryId = relatedEntryId;
    }

    public static JournalAuditEvent Reconstitute(Guid eventId, Guid companyId, Guid branchId, Guid journalId, long revision,
        JournalAuditAction action, JournalEntryState? previousState, JournalEntryState newState,
        JournalActionContext context, Guid? relatedEntryId)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new JournalAuditEvent(eventId, companyId, branchId, journalId, revision, action, previousState, newState, context, relatedEntryId);
    }

    public Guid EventId { get; }
    public Guid CompanyId { get; }
    public Guid BranchId { get; }
    public Guid JournalId { get; }
    public long Revision { get; }
    public JournalAuditAction Action { get; }
    public JournalEntryState? PreviousState { get; }
    public JournalEntryState NewState { get; }
    public JournalActionContext Context { get; }
    public Guid? RelatedEntryId { get; }
}
