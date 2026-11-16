using PetraERP.Domain.Organizations;

namespace PetraERP.Domain.GeneralLedger;

public sealed partial class JournalEntry
{
    public JournalEntry CreateReversalDraft(BranchScope scope, DateOnly accountingDate, string description,
        JournalActionContext context)
    {
        EnsureState(JournalEntryState.Posted);
        EnsureScope(scope);
        EnsureActionTime(context);
        return NewDraft(scope, accountingDate, description, Lines.Select(static line => line.Reverse()), context, Id, null);
    }

    public JournalEntry CreateCorrectionDraft(BranchScope scope, DateOnly accountingDate, string description,
        IEnumerable<JournalLine> lines, JournalActionContext context)
    {
        EnsureState(JournalEntryState.Reversed);
        EnsureScope(scope);
        EnsureActionTime(context);
        return NewDraft(scope, accountingDate, description, lines, context, null, Id);
    }

    /// <summary>Returns the pair to commit atomically; this method does not persist either entry.</summary>
    public JournalReversal PostReversal(JournalEntry original, JournalValidationContext validation,
        JournalPostingNumber number, JournalActionContext context)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(number);
        original.EnsureState(JournalEntryState.Posted);
        if (ReversesEntryId != original.Id || CompanyId != original.CompanyId || BranchId != original.BranchId
            || Lines.Count != original.Lines.Count)
        {
            throw new ArgumentException("Reversal must reference the matching posted journal.", nameof(original));
        }

        if (number == original.PostingNumber)
        {
            throw new ArgumentException("Reversal cannot reuse the original journal number.", nameof(number));
        }

        var originals = original.Lines.ToDictionary(static line => line.Id);
        var used = new HashSet<Guid>();
        foreach (var line in Lines)
        {
            if (line.ReversesLineId is not { } originalId || !used.Add(originalId)
                || !originals.TryGetValue(originalId, out var source) || !line.IsExactReversalOf(source))
            {
                throw new ArgumentException("Reversal must swap every original financial line exactly once.", nameof(original));
            }
        }

        var reversal = PostCore(validation, number, context);
        var reversed = original.Evolve(JournalEntryState.Reversed, JournalAuditAction.Reversed, context, reversedByEntryId: Id);
        return new JournalReversal(reversed, reversal);
    }

    private void EnsureScope(BranchScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.CompanyId != CompanyId || scope.BranchId != BranchId)
        {
            throw new ArgumentException("Correction and reversal must use the original company and branch.", nameof(scope));
        }
    }
}
