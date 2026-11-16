namespace PetraERP.Application.GeneralLedger.Posting;

/// <summary>
/// Thrown when an idempotency key was previously registered with a different semantic fingerprint.
/// </summary>
public sealed class PostingIdempotencyConflictException : InvalidOperationException
{
    public PostingIdempotencyConflictException(Guid key, string message)
        : base(message)
    {
        IdempotencyKey = key;
    }

    public Guid IdempotencyKey { get; }
}
