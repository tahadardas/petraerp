using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PetraERP.Desktop.Connection;

/// <summary>
/// A SQL deadlock victim (1205) or lock timeout is rolled back whole and the API answers 503 <c>server.transient_failure</c>.
/// This handler replays such a response at most twice, and only for reads or for write requests that carry a *valid*
/// idempotency key: a well-formed <c>Idempotency-Key</c> header (a non-empty UUID, the format the server requires) or a
/// top-level JSON body property <c>idempotencyKey</c> that is a non-empty, bounded, printable string. The body is parsed,
/// not searched for text, so a payload that merely mentions the word is never treated as keyed. Replays reuse the same key
/// and the same bytes, stay inside a total time budget, and are never attempted for non-JSON bodies or large uploads.
/// </summary>
internal sealed class TransientRetryHandler(HttpMessageHandler inner, IReadOnlyList<TimeSpan>? backoff = null) : DelegatingHandler(inner)
{
    private const string TransientCode = "server.transient_failure";
    private static readonly TimeSpan[] DefaultBackoff = [TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(1200)];
    private const int MaximumKeyLength = 128;
    private const long MaximumReplayableBody = 1_048_576;
    private static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(8);
    private readonly IReadOnlyList<TimeSpan> delays = backoff ?? DefaultBackoff;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var replayable = await IsReplayableAsync(request, cancellationToken).ConfigureAwait(false);
        var started = Stopwatch.GetTimestamp();
        var attempt = 0;
        while (true)
        {
            using var clone = attempt == 0 ? null : await CloneAsync(request, cancellationToken).ConfigureAwait(false);
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(clone ?? request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (replayable && attempt < delays.Count && IsLostResponse(exception, cancellationToken)
                && Stopwatch.GetElapsedTime(started) + delays[attempt] <= TotalBudget)
            {
                // The request may have committed on the server while the answer was lost (timeout, reset connection). Replaying
                // the SAME key and body is safe because the server returns the stored receipt instead of acting twice; a new
                // key would be the dangerous thing, and is never generated here.
                await Task.Delay(delays[attempt++], cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!replayable || attempt >= delays.Count || !await IsTransientFailureAsync(response, cancellationToken).ConfigureAwait(false)) return response;
            // The user must not wait indefinitely for a retry chain; the last transient answer is returned as it is.
            if (Stopwatch.GetElapsedTime(started) + delays[attempt] > TotalBudget) return response;
            response.Dispose();
            await Task.Delay(delays[attempt++], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A transport failure or an HttpClient timeout - not the user's own cancellation.</summary>
    private static bool IsLostResponse(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && (exception is HttpRequestException || exception is TaskCanceledException { InnerException: TimeoutException } || exception is TimeoutException);

    private static async Task<bool> IsReplayableAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null) return request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;
        if (request.Method != HttpMethod.Post && request.Method != HttpMethod.Put && request.Method != HttpMethod.Patch) return false;
        await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);

        if (request.Headers.TryGetValues("Idempotency-Key", out var values))
        {
            var header = values.ToArray();
            if (header.Length == 1 && Guid.TryParse(header[0], out var key) && key != Guid.Empty) return true;
        }

        // Only a JSON object can carry a body key; multipart, form and binary uploads are never replayed.
        var mediaType = request.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !(mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
            return false;
        var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.LongLength > MaximumReplayableBody) return false;
        return HasValidBodyKey(bytes);
    }

    private static bool HasValidBodyKey(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.NameEquals("idempotencyKey") && !property.Name.Equals("idempotencyKey", StringComparison.OrdinalIgnoreCase)) continue;
                if (property.Value.ValueKind != JsonValueKind.String) return false;
                var value = property.Value.GetString();
                return !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumKeyLength && value.All(static c => !char.IsControl(c) && !char.IsWhiteSpace(c));
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<bool> IsTransientFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.ServiceUnavailable) return false;
        await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return body.Contains(TransientCode, StringComparison.Ordinal);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
        foreach (var header in request.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = content;
        }
        return clone;
    }
}
