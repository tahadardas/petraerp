using PetraERP.ApiClient;

namespace PetraERP.Desktop.GeneralLedger;

/// <summary>What the current user may attempt on a journal. The server remains authoritative for every action.</summary>
public sealed record JournalCommandAvailability(bool Edit, bool Validate, bool Delete, bool Approve, bool ReturnToDraft, bool Post,
    bool PostReversal, bool CreateReversal)
{
    public static readonly JournalCommandAvailability None = new(false, false, false, false, false, false, false, false);
}

/// <summary>
/// The only Desktop mapping from journal state + permissions to commands. It compares the typed transport
/// <see cref="JournalState"/> by member, never by number (the generated client numbers members from zero while the
/// server Domain numbers them from one; only names travel on the wire).
/// </summary>
public static class JournalCommandPolicy
{
    public const string Manage = "Ledger.Journals.Manage";
    public const string Approve = "Ledger.Journals.Approve";
    public const string PostPermission = "Ledger.Journals.Post";
    public const string Reverse = "Ledger.Journals.Reverse";

    /// <param name="state">Null for a new unsaved draft.</param>
    /// <param name="isReversalDraft">The journal reverses another journal (its content is fixed).</param>
    /// <param name="isReversed">Another journal already reverses this one.</param>
    public static JournalCommandAvailability Evaluate(JournalState? state, bool isReversalDraft, bool isReversed, IReadOnlySet<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var manage = permissions.Contains(Manage);
        return state switch
        {
            null => new(Edit: manage, false, false, false, false, false, false, false),
            JournalState.Draft => new(Edit: manage && !isReversalDraft, Validate: manage, Delete: manage, false, false, false, false, false),
            JournalState.Validated => new(false, false, false, Approve: permissions.Contains(Approve), ReturnToDraft: manage, false, false, false),
            JournalState.Approved => new(false, false, false, false, ReturnToDraft: manage,
                Post: !isReversalDraft && permissions.Contains(PostPermission),
                PostReversal: isReversalDraft && permissions.Contains(Reverse), false),
            JournalState.Posted => new(false, false, false, false, false, false, false, CreateReversal: manage && !isReversed),
            JournalState.Reversed => JournalCommandAvailability.None,
            _ => JournalCommandAvailability.None,
        };
    }
}
