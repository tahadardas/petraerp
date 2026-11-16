using System.Collections.ObjectModel;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.GeneralLedger;

/// <summary>Immutable manual-journal snapshots; application/database integration is a separate requirement.</summary>
public sealed partial class JournalEntry
{
    public const int MaximumDescriptionLength = 1000;

    private JournalEntry(Guid id, Guid companyId, Guid branchId, DateOnly accountingDate, string description,
        ReadOnlyCollection<JournalLine> lines, JournalEntryState state, ReadOnlyCollection<JournalAuditEvent> changes,
        JournalPostingNumber? postingNumber, Guid? postingPeriodId, Guid? reversesEntryId,
        Guid? reversedByEntryId, Guid? correctsEntryId)
    {
        Id = id;
        CompanyId = companyId;
        BranchId = branchId;
        AccountingDate = accountingDate;
        Description = description;
        Lines = lines;
        State = state;
        Changes = changes;
        PostingNumber = postingNumber;
        PostingPeriodId = postingPeriodId;
        ReversesEntryId = reversesEntryId;
        ReversedByEntryId = reversedByEntryId;
        CorrectsEntryId = correctsEntryId;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public Guid BranchId { get; }
    public DateOnly AccountingDate { get; }
    public string Description { get; }
    public IReadOnlyList<JournalLine> Lines { get; }
    public JournalEntryState State { get; }
    public IReadOnlyList<JournalAuditEvent> Changes { get; }
    public long Revision => Changes[^1].Revision;
    public JournalPostingNumber? PostingNumber { get; }
    public Guid? PostingPeriodId { get; }
    public Guid? ReversesEntryId { get; }
    public Guid? ReversedByEntryId { get; }
    public Guid? CorrectsEntryId { get; }

    public static JournalEntry CreateManualDraft(BranchScope scope, DateOnly accountingDate, string description,
        IEnumerable<JournalLine> lines, JournalActionContext context) =>
        NewDraft(scope, accountingDate, description, lines, context, null, null);

    private static JournalEntry NewDraft(BranchScope scope, DateOnly accountingDate, string description,
        IEnumerable<JournalLine> lines, JournalActionContext context, Guid? reversesEntryId, Guid? correctsEntryId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(context);
        var id = Guid.CreateVersion7();
        var snapshot = SnapshotLines(scope.CompanyId, id, lines, reversesEntryId);
        var text = DomainText.Required(description, MaximumDescriptionLength, nameof(description), multiline: true);
        var created = new JournalAuditEvent(scope.CompanyId, scope.BranchId, id, 1, JournalAuditAction.Created,
            null, JournalEntryState.Draft, context, reversesEntryId ?? correctsEntryId);
        return new JournalEntry(id, scope.CompanyId, scope.BranchId, accountingDate, text, snapshot,
            JournalEntryState.Draft, Array.AsReadOnly(new[] { created }), null, null, reversesEntryId, null, correctsEntryId);
    }

    public static JournalEntry Reconstitute(Guid id, Guid companyId, Guid branchId, DateOnly accountingDate, string description,
        IEnumerable<JournalLine> lines, JournalEntryState state, IEnumerable<JournalAuditEvent> changes,
        JournalPostingNumber? postingNumber, Guid? postingPeriodId, Guid? reversesEntryId,
        Guid? reversedByEntryId, Guid? correctsEntryId)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(changes);
        var lineList = lines.ToList();
        var changeList = changes.ToList();
        return new JournalEntry(id, companyId, branchId, accountingDate, description,
            Array.AsReadOnly(lineList.ToArray()), state, Array.AsReadOnly(changeList.ToArray()),
            postingNumber, postingPeriodId, reversesEntryId, reversedByEntryId, correctsEntryId);
    }

    private static ReadOnlyCollection<JournalLine> SnapshotLines(Guid companyId, Guid journalId,
        IEnumerable<JournalLine> lines, Guid? reversesEntryId)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var snapshot = lines.ToArray();
        var ids = new HashSet<Guid>();
        if (snapshot.Any(line => line is null || line.CompanyId != companyId || !ids.Add(line.Id)
            || (reversesEntryId is null && line.ReversesLineId is not null)))
        {
            throw new ArgumentException("Journal lines must be nonnull, unique and owned by the company; reversal lines need a reversal journal.", nameof(lines));
        }

        return Array.AsReadOnly(snapshot.Select(line => line.AssignTo(journalId)).ToArray());
    }

    private JournalEntry Evolve(JournalEntryState state, JournalAuditAction action, JournalActionContext context,
        DateOnly? date = null, string? description = null, ReadOnlyCollection<JournalLine>? lines = null,
        JournalPostingNumber? number = null, Guid? periodId = null, Guid? reversedByEntryId = null)
    {
        EnsureActionTime(context);
        var changed = new JournalAuditEvent(CompanyId, BranchId, Id, checked(Revision + 1), action, State, state,
            context, reversedByEntryId ?? ReversesEntryId ?? CorrectsEntryId);
        return new JournalEntry(Id, CompanyId, BranchId, date ?? AccountingDate, description ?? Description,
            lines ?? Array.AsReadOnly(Lines.ToArray()), state, Array.AsReadOnly(Changes.Append(changed).ToArray()),
            number ?? PostingNumber, periodId ?? PostingPeriodId, ReversesEntryId,
            reversedByEntryId ?? ReversedByEntryId, CorrectsEntryId);
    }

    private void EnsureState(JournalEntryState required)
    {
        if (State != required)
        {
            throw RuleViolation.Create(RuleViolation.JournalStateInvalid, $"This journal operation requires state {required}.");
        }
    }

    private void EnsureActionTime(JournalActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.OccurredAtUtc < Changes[^1].Context.OccurredAtUtc)
        {
            throw new ArgumentException("Journal actions cannot precede the last action timestamp.", nameof(context));
        }
    }
}
