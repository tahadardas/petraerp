using System.Net.Http;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.GeneralLedger;

/// <summary>One logical posting request: same journal(s), same versions, same reason.</summary>
public sealed record PostingIntent(Guid JournalId, string ExpectedVersion, Guid? OriginalJournalId, string? OriginalExpectedVersion, string Reason);

/// <summary>
/// Owns Idempotency-Key reuse. A fresh UUID is created for a new logical attempt. When the outcome is ambiguous
/// (the request may have reached the server: transport failure, timeout or a transient 5xx), the same key is kept for a
/// retry of the identical intent so the server replays the original receipt instead of posting twice. Any definitive
/// outcome (success or a server rejection) releases the key; a changed intent always gets a new key.
/// </summary>
public sealed class PostingAttemptTracker
{
    private readonly Func<Guid> newKey;
    private (PostingIntent Intent, Guid Key)? pending;

    public PostingAttemptTracker()
        : this(Guid.NewGuid)
    {
    }

    public PostingAttemptTracker(Func<Guid> newKey) => this.newKey = newKey ?? throw new ArgumentNullException(nameof(newKey));

    public bool HasPendingAttempt => pending is not null;

    public Guid KeyFor(PostingIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (pending is { } current && current.Intent == intent)
        {
            return current.Key;
        }

        var key = newKey();
        if (key == Guid.Empty) throw new InvalidOperationException("Idempotency keys must be nonempty.");
        pending = (intent, key);
        return key;
    }

    /// <summary>Records the outcome. Returns true when the key is retained for a safe retry.</summary>
    public bool Complete(PostingIntent intent, Exception? failure)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (failure is not null && IsAmbiguous(failure) && pending is { } current && current.Intent == intent)
        {
            return true;
        }

        pending = null;
        return false;
    }

    /// <summary>The intent of an ambiguous earlier attempt on this journal, so a retry resends identical content.</summary>
    public PostingIntent? PendingIntentFor(Guid journalId) => pending is { } current && current.Intent.JournalId == journalId ? current.Intent : null;

    public void Reset() => pending = null;

    public static bool IsAmbiguous(Exception exception) => exception switch
    {
        HttpRequestException => true,
        TimeoutException => true,
        TaskCanceledException { InnerException: TimeoutException } => true,
        PetraApiException api => api.StatusCode is 408 or >= 500,
        _ => false,
    };
}
