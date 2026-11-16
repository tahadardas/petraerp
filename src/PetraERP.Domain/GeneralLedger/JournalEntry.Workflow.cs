using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.GeneralLedger;

public sealed partial class JournalEntry
{
    public JournalEntry Revise(DateOnly accountingDate, string description, IEnumerable<JournalLine> lines,
        JournalActionContext context)
    {
        EnsureState(JournalEntryState.Draft);
        if (ReversesEntryId is not null)
        {
            throw RuleViolation.Create(RuleViolation.JournalStateInvalid, "Reversal draft content cannot be revised.");
        }

        var snapshot = SnapshotLines(CompanyId, Id, lines, null);
        var text = DomainText.Required(description, MaximumDescriptionLength, nameof(description), multiline: true);
        return Evolve(JournalEntryState.Draft, JournalAuditAction.Revised, context, accountingDate, text, snapshot);
    }

    public JournalEntry Validate(JournalValidationContext validation, JournalActionContext context)
    {
        EnsureState(JournalEntryState.Draft);
        ArgumentNullException.ThrowIfNull(validation);
        _ = validation.Validate(this);
        return Evolve(JournalEntryState.Validated, JournalAuditAction.Validated, context);
    }

    public JournalEntry Approve(JournalActionContext context)
    {
        EnsureState(JournalEntryState.Validated);
        return Evolve(JournalEntryState.Approved, JournalAuditAction.Approved, context);
    }

    public JournalEntry ReturnToDraft(JournalActionContext context)
    {
        if (State is not (JournalEntryState.Validated or JournalEntryState.Approved))
        {
            throw RuleViolation.Create(RuleViolation.JournalStateInvalid, "Only validated or approved journals may return to Draft.");
        }

        return Evolve(JournalEntryState.Draft, JournalAuditAction.ReturnedToDraft, context);
    }

    public JournalEntry Post(JournalValidationContext validation, JournalPostingNumber number, JournalActionContext context)
    {
        if (ReversesEntryId is not null)
        {
            throw RuleViolation.Create(RuleViolation.JournalStateInvalid, "Use paired reversal posting to update both journals together.");
        }

        return PostCore(validation, number, context);
    }

    private JournalEntry PostCore(JournalValidationContext validation, JournalPostingNumber number, JournalActionContext context)
    {
        EnsureState(JournalEntryState.Approved);
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(number);
        var period = validation.Validate(this);
        if (number.CompanyId != CompanyId || number.BranchId != BranchId || number.FiscalYearId != period.FiscalYearId)
        {
            throw new ArgumentException("Journal number must match company, branch and posting fiscal year.", nameof(number));
        }

        return Evolve(JournalEntryState.Posted, JournalAuditAction.Posted, context, number: number, periodId: period.Id);
    }
}
