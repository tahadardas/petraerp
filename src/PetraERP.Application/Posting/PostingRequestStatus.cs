namespace PetraERP.Application.Posting;

public enum PostingRequestState
{
    /// <summary>The key is in the durable register and its transaction committed: the document is posted. Never post again.</summary>
    Completed = 1,

    /// <summary>The key was registered but never completed (still running, or rolled back). Retrying with the same key is safe.</summary>
    NotCompleted = 2,
}

public enum PostingRequestKind
{
    Journal = 1,
    SalesInvoice = 2,
    PurchaseInvoice = 3,
    Voucher = 4,
}

/// <summary>What the server durably knows about an idempotency key, so a client that lost the response can decide without re-posting.</summary>
public sealed record PostingRequestStatus(
    string IdempotencyKey, PostingRequestKind Kind, byte Operation, Guid DocumentId, PostingRequestState State,
    Guid? OutcomeJournalId, DateTimeOffset RegisteredAtUtc, DateTimeOffset? CompletedAtUtc);

public interface IPostingRequestStatusService
{
    /// <summary>
    /// Returns the status of a request the actor registered in this company and branch, or null. A key registered by another user,
    /// company or branch is indistinguishable from an unknown key, so the register cannot be probed.
    /// </summary>
    Task<PostingRequestStatus?> GetAsync(Guid companyId, Guid branchId, Guid actorUserId, string idempotencyKey, CancellationToken cancellationToken);
}
