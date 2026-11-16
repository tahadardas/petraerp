namespace PetraERP.Contracts.Posting;

public enum PostingRequestStateCode { Completed = 1, NotCompleted = 2 }

public enum PostingRequestKindCode { Journal = 1, SalesInvoice = 2, PurchaseInvoice = 3, Voucher = 4 }

public enum PostingRecommendedAction
{
    /// <summary>The document is posted; do not post again, reload it.</summary>
    DoNotRepost = 1,

    /// <summary>The request did not complete; repeating it with the same Idempotency-Key is safe.</summary>
    RetryWithSameKey = 2,
}

public sealed record PostingRequestStatusResponse
{
    public required string IdempotencyKey { get; init; }
    public required PostingRequestKindCode Kind { get; init; }
    public required PostingRequestStateCode State { get; init; }
    public required PostingRecommendedAction RecommendedAction { get; init; }
    /// <summary>The journal or invoice the key was registered for.</summary>
    public required Guid DocumentId { get; init; }
    public Guid? OutcomeJournalId { get; init; }
    public required DateTimeOffset RegisteredAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
}
