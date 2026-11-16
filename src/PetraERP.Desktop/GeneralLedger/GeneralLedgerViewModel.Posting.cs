using PetraERP.ApiClient;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.GeneralLedger;

public sealed partial class GeneralLedgerViewModel
{
    /// <summary>The receipt of the last successful posting in this view (server-issued sequence).</summary>
    public PostingReceiptResponse? LastPostingReceipt { get; private set; }

    /// <summary>True after an ambiguous posting failure: retrying the same action reuses the same Idempotency-Key.</summary>
    public bool HasPendingPostingAttempt => postingAttempts.HasPendingAttempt;

    private async Task PostJournalAsync()
    {
        var promptToken = context.Token;
        if (currentJournal is not { } journal || !CanPostJournal || !HasActionReason) return;
        if (!await confirmation.ConfirmAsync("D_ConfirmPostJournal").ConfigureAwait(true) || promptToken.IsCancellationRequested || currentJournal?.Id != journal.Id) return;

        var intent = postingAttempts.PendingIntentFor(journal.Id) is { OriginalJournalId: null } pending
                     && pending.ExpectedVersion == journal.Version && pending.Reason == ActionReason.Trim()
            ? pending
            : new PostingIntent(journal.Id, journal.Version, null, null, ActionReason.Trim());
        await RunAsync(OperationKind.Post, () => SubmitPostingAsync(intent, key => client.PostJournalAsync(currentCompanyId, currentBranchId, journal.Id,
            new PostJournalRequest { ExpectedVersion = intent.ExpectedVersion, Reason = intent.Reason }, key, context.Token))).ConfigureAwait(true);
    }

    private async Task PostReversalAsync()
    {
        var promptToken = context.Token;
        if (currentJournal is not { ReversesEntryId: { } originalId } journal || !CanPostReversal || !HasActionReason) return;
        if (!await confirmation.ConfirmAsync("D_ConfirmPostReversal").ConfigureAwait(true) || promptToken.IsCancellationRequested || currentJournal?.Id != journal.Id) return;

        await RunAsync(OperationKind.Post, async () =>
        {
            var reason = ActionReason.Trim();
            var intent = postingAttempts.PendingIntentFor(journal.Id) is { OriginalJournalId: not null } pending
                         && pending.ExpectedVersion == journal.Version && pending.Reason == reason
                ? pending
                : await NewReversalIntentAsync(journal, originalId, reason).ConfigureAwait(true);
            await SubmitPostingAsync(intent, key => client.PostJournalReversalAsync(currentCompanyId, currentBranchId, journal.Id,
                new PostJournalReversalRequest
                {
                    ExpectedVersion = intent.ExpectedVersion,
                    OriginalJournalId = intent.OriginalJournalId!.Value,
                    OriginalExpectedVersion = intent.OriginalExpectedVersion!,
                    Reason = intent.Reason,
                }, key, context.Token)).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task<PostingIntent> NewReversalIntentAsync(JournalDetailsResponse reversal, Guid originalId, string reason)
    {
        // The original's version is read fresh from the server for a new logical attempt.
        var original = await client.GetJournalAsync(currentCompanyId, currentBranchId, originalId, context.Token).ConfigureAwait(true);
        return new PostingIntent(reversal.Id, reversal.Version, original.Id, original.Version, reason);
    }

    private async Task SubmitPostingAsync(PostingIntent intent, Func<Guid, Task<PostingReceiptResponse>> send)
    {
        var key = postingAttempts.KeyFor(intent);
        PostingReceiptResponse receipt;
        try
        {
            receipt = await send(key).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !context.IsCancellationRequested)
        {
            postingAttempts.Complete(intent, exception);
            OnPropertyChanged(nameof(HasPendingPostingAttempt));
            throw;
        }

        postingAttempts.Complete(intent, null);
        LastPostingReceipt = receipt;
        OnPropertyChanged(nameof(LastPostingReceipt));
        OnPropertyChanged(nameof(HasPendingPostingAttempt));

        // The server is authoritative: reload the posted journal (state, number, version, audit) and the list.
        var posted = await client.GetJournalAsync(currentCompanyId, currentBranchId, intent.JournalId, context.Token).ConfigureAwait(true);
        DisplayJournal(posted);
        await LoadJournalsAsync(Page, context.Token).ConfigureAwait(true);
    }
}
