using PetraERP.Domain.GeneralLedger;

namespace PetraERP.Application.GeneralLedger.Posting;

/// <summary>A completion intent; it becomes replay evidence only after atomic durable commit.</summary>
public sealed class PostingReceipt
{
    private PostingReceipt(PostingRequest request, JournalEntry posted)
    {
        Binding = PostingRequestBinding.Bind(request);
        JournalId = posted.Id;
        OriginalJournalId = request.OriginalJournalId;
        PostingNumber = posted.PostingNumber!;
    }

    public PostingRequestBinding Binding { get; }
    public PostingRequestScope Scope => Binding.Scope;
    public Guid IdempotencyKey => Binding.IdempotencyKey;
    public PostingOperation Operation => Binding.Operation;
    public PostingFingerprint Fingerprint => Binding.Fingerprint;
    public Guid JournalId { get; }
    public Guid? OriginalJournalId { get; }
    public JournalPostingNumber PostingNumber { get; }

    public static PostingReceipt ForPost(PostingRequest request, JournalEntry posted)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(posted);
        if (request.Operation != PostingOperation.PostJournal || posted.ReversesEntryId is not null)
        {
            throw new ArgumentException("Ordinary receipt requires an ordinary journal posting request.", nameof(request));
        }

        ValidatePosted(request, posted);
        return new PostingReceipt(request, posted);
    }

    public static PostingReceipt ForReversal(PostingRequest request, JournalReversal result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        var original = result.OriginalEntry;
        var reversal = result.ReversalEntry;
        if (request.Operation != PostingOperation.PostJournalReversal
            || request.OriginalJournalId != original.Id || reversal.ReversesEntryId != original.Id
            || original.ReversedByEntryId != reversal.Id || original.State != JournalEntryState.Reversed
            || original.CompanyId != request.Scope.CompanyId || original.BranchId != request.Scope.BranchId)
        {
            throw new ArgumentException("Reversal receipt requires the matching pair of journal results.", nameof(result));
        }

        ValidatePosted(request, reversal);
        ValidateActorAndReason(request, original);
        return new PostingReceipt(request, reversal);
    }

    public PostingReplayDecision EvaluateReplay(PostingRequest incoming)
    {
        return Binding.Matches(incoming)
            ? PostingReplayDecision.ReturnRecordedResult
            : PostingReplayDecision.RejectDifferentRequest;
    }

    private static void ValidatePosted(PostingRequest request, JournalEntry posted)
    {
        if (posted.State != JournalEntryState.Posted || posted.PostingNumber is null || posted.PostingPeriodId is null
            || posted.Id != request.JournalId || posted.CompanyId != request.Scope.CompanyId
            || posted.BranchId != request.Scope.BranchId)
        {
            throw new ArgumentException("Receipt must reference the matching posted journal and scope.", nameof(posted));
        }

        ValidateActorAndReason(request, posted);
    }

    private static void ValidateActorAndReason(PostingRequest request, JournalEntry journal)
    {
        var context = journal.Changes[^1].Context;
        if (context.ActorUserId != request.Scope.ActorUserId || context.Reason != request.Reason.Value)
        {
            throw new ArgumentException("Journal action provenance must match the posting request.", nameof(journal));
        }
    }
}
