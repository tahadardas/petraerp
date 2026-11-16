namespace PetraERP.Application.GeneralLedger.Posting;

/// <summary>
/// Authoritative server service for executing and recording durable financial postings.
/// Manages atomic idempotency registration, serializable lock ordering, domain validation,
/// atomic sequence number allocation, and audit persistence.
/// </summary>
public interface IPostingService
{
    /// <summary>
    /// Executes a durable posting request or returns the recorded outcome on replay.
    /// </summary>
    /// <param name="request">Validated posting request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Immutable posting receipt.</returns>
    Task<PostingReceipt> PostAsync(PostingRequest request, CancellationToken cancellationToken = default);
}
