using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace PetraERP.Api.Security;

/// <summary>
/// Marks an endpoint whose whole unit of work is idempotent by a durable key, so that after a SQL deadlock victim rollback
/// the API may run the entire unit again on a fresh context. Never put it on an endpoint without durable idempotency.
/// </summary>
internal sealed class DeadlockRetryMetadata
{
    internal static readonly DeadlockRetryMetadata Instance = new();
}

internal static class DeadlockRetryEndpointExtensions
{
    internal static RouteHandlerBuilder RetryOnDeadlock(this RouteHandlerBuilder builder) => builder.WithMetadata(DeadlockRetryMetadata.Instance);
}

/// <summary>Bounded exponential backoff with jitter. Two retries keep the worst case near a second, never an open-ended wait.</summary>
internal static class DeadlockRetryPolicy
{
    private const int DefaultRetries = 2;

    /// <summary>Retries per request (0-5). The environment variable PETRAERP_DEADLOCK_RETRIES lets operators and benchmarks turn it down or off.</summary>
    internal static int MaximumRetries { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("PETRAERP_DEADLOCK_RETRIES"), out var configured) && configured is >= 0 and <= 5 ? configured : DefaultRetries;

    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(80);

    internal static TimeSpan Delay(int attempt) =>
        BaseDelay * Math.Pow(2, attempt) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 80));

    /// <summary>
    /// 1205 (deadlock victim, rolled back whole) or a connection-pool wait that timed out (nothing was sent, or the unit is
    /// replayed whole under its durable key). A lock/command timeout (-2) or a dropped connection may have committed, so it is
    /// never replayed here.
    /// </summary>
    internal static bool IsDeadlockVictim(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 } || IsPoolTimeout(current)) return true;
        return false;
    }

    internal static bool IsPoolTimeout(Exception exception) =>
        exception is InvalidOperationException && exception.Message.Contains("obtaining a connection from the pool", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A key proves idempotency only if it is well formed: a UUID header or a bounded printable top-level body string.</summary>
internal static class IdempotencyKeyProbe
{
    private const int MaximumKeyLength = 128;
    private const long MaximumBody = 1_048_576;

    internal static async Task<bool> HasValidKeyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Headers.TryGetValue("Idempotency-Key", out var header) && header.Count == 1
            && Guid.TryParse(header[0], out var key) && key != Guid.Empty)
        {
            request.EnableBuffering();
            return true;
        }

        if (request.ContentLength is null or > MaximumBody || request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            return false;
        request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals("idempotencyKey", StringComparison.OrdinalIgnoreCase)) continue;
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
        finally
        {
            request.Body.Position = 0;
        }
    }
}

internal static partial class DeadlockRetryLog
{
    [LoggerMessage(EventId = 5100, Level = LogLevel.Warning, Message = "SQL deadlock victim: re-running the whole idempotent unit (retry {Attempt}) for {Path}")]
    internal static partial void Retrying(ILogger logger, int attempt, string path);
}

/// <summary>The API is saturated and the request waited too long for a turn; the client may retry with the same key.</summary>
internal sealed class ServerBusyException() : Exception("The server is busy; retry shortly.");

