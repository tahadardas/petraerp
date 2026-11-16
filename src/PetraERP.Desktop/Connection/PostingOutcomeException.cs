using PetraERP.ApiClient;

namespace PetraERP.Desktop.Connection;

/// <summary>
/// The response of a keyed post was lost (connection dropped, timeout) and every automatic retry failed too, but the server could be
/// asked what it durably recorded for the key. The user must act on that answer, not on a guess: a completed post is never repeated.
/// </summary>
public sealed class PostingOutcomeException(PostingRequestStateCode state, Guid documentId, string idempotencyKey, Exception inner)
    : Exception(state == PostingRequestStateCode.Completed
        ? "The server posted this document before the connection was lost; do not post it again."
        : "The server did not complete this post; it is safe to try again.", inner)
{
    public PostingRequestStateCode State { get; } = state;
    public Guid DocumentId { get; } = documentId;
    public string IdempotencyKey { get; } = idempotencyKey;
    public bool Posted => State == PostingRequestStateCode.Completed;
}
