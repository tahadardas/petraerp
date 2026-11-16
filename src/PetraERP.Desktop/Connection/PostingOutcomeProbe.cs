using System.Net.Http;
using System.Runtime.ExceptionServices;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Connection;

/// <summary>After a keyed post whose response was lost for good, asks the server what it durably recorded for the key.</summary>
internal static class PostingOutcomeProbe
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(8);

    internal static async Task<T> RunAsync<T>(string? idempotencyKey, Func<Task<T>> post,
        Func<CancellationToken, Task<PostingRequestStatusResponse>> queryStatus, CancellationToken cancellationToken)
    {
        try
        {
            return await post().ConfigureAwait(false);
        }
        catch (Exception exception) when (!string.IsNullOrWhiteSpace(idempotencyKey) && !cancellationToken.IsCancellationRequested
            && (exception is HttpRequestException || exception is TimeoutException || exception is TaskCanceledException { InnerException: TimeoutException }))
        {
            PostingRequestStatusResponse? status;
            try
            {
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probe.CancelAfter(StatusTimeout);
                status = await queryStatus(probe.Token).ConfigureAwait(false);
            }
            catch (PetraApiException api) when (api.StatusCode == 404)
            {
                // Never reached the durable register: nothing was posted and nothing is half-done.
                throw new PostingOutcomeException(PostingRequestStateCode.NotCompleted, Guid.Empty, idempotencyKey!, exception);
            }
            catch (Exception failure) when (failure is HttpRequestException or TimeoutException or OperationCanceledException or PetraApiException)
            {
                status = null;
            }

            // The status question failed too: the user keeps the original "outcome unknown" error rather than a guess.
            if (status is null) ExceptionDispatchInfo.Capture(exception).Throw();
            throw new PostingOutcomeException(status!.State, status.DocumentId, idempotencyKey!, exception);
        }
    }
}
