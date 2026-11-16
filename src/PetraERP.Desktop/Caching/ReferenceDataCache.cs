using System.Collections.Concurrent;

namespace PetraERP.Desktop.Caching;

/// <summary>
/// Everything that decides whether a cached reference answer may be reused. A company-only key mixed fiscal years and
/// workspaces of the same company; this scope is immutable and every field takes part in the key.
/// </summary>
public sealed record CacheScope(Guid CompanyId, Guid FiscalYearId, Guid WorkspaceId, Guid? BranchId = null, string? PermissionFingerprint = null)
{
    internal string WorkspacePart => WorkspaceId.ToString("N");

    internal string ScopePart =>
        $"{CompanyId:N}:{FiscalYearId:N}:{BranchId?.ToString("N") ?? "-"}:{PermissionFingerprint ?? "-"}";
}

/// <summary>
/// A result that finished after the workspace, user or reference data changed underneath it. Callers treat it exactly like a
/// cancellation: the answer was valid for a context the user has already left and must not reach the screen.
/// </summary>
public sealed class StaleWorkspaceResultException() : OperationCanceledException("The answer belongs to a workspace or data version that is no longer current.");

/// <summary>
/// Workspace-scoped reference data cache with single-flight coalescing, TTL eviction and generation checks. Sensitive,
/// volatile financial balances (cash, bank, stock and party balances) are never stored here.
/// </summary>
public interface IReferenceDataCache
{
    /// <summary>Monotonic counter bumped when the workspace is switched, the user signs out or permissions change.</summary>
    long ContextEpoch { get; }

    Task<T> GetOrCreateAsync<T>(CacheScope scope, string category, string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>Drops one category of one workspace (after an add, edit, delete or disable in that workspace).</summary>
    void InvalidateCategory(CacheScope scope, string category);

    void InvalidateWorkspace(Guid workspaceId);

    /// <summary>Drops a category in every workspace (a schema or policy change that affects all of them).</summary>
    void InvalidateReferenceType(string category);

    void InvalidateAll();

    /// <summary>Rejects every answer that is still in flight, without dropping entries that are keyed to other workspaces.</summary>
    void AdvanceContext();
}

public sealed class ReferenceDataCache(TimeProvider timeProvider) : IReferenceDataCache
{
    private readonly record struct Generation(long Context, long Workspace, long Type, long WorkspaceType);

    private sealed record Entry(object Value, DateTimeOffset ExpiresAtUtc, Generation Generation);

    private sealed class Flight(Generation generation)
    {
        public Generation Generation { get; } = generation;

        public CancellationTokenSource Cancellation { get; } = new();

        private readonly TaskCompletionSource<object?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<object?> Task => completion.Task;

        public void Complete(Task<object?> work)
        {
            if (work.IsCanceled) completion.TrySetCanceled();
            else if (work.IsFaulted) completion.TrySetException(work.Exception!.InnerExceptions);
            else completion.TrySetResult(work.Result);
        }

        public int Waiters { get; set; }
    }

    private readonly object gate = new();
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Flight> flights = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> generations = new(StringComparer.Ordinal);
    private readonly TimeSpan defaultTtl = TimeSpan.FromMinutes(10);
    private long contextEpoch;

    public static ReferenceDataCache Instance { get; } = new(TimeProvider.System);

    public long ContextEpoch => Interlocked.Read(ref contextEpoch);

    private static string WorkspaceKey(Guid workspace) => "ws:" + workspace.ToString("N");

    private static string TypeKey(string category) => "type:" + category;

    private static string WorkspaceTypeKey(Guid workspace, string category) => $"wt:{workspace:N}:{category}";

    private static string Prefix(Guid workspace, string category) => $"{workspace:N}|{category}|";

    private static string BuildKey(CacheScope scope, string category, string key) => $"{Prefix(scope.WorkspaceId, category)}{scope.ScopePart}|{key}";

    private Generation Capture(Guid workspace, string category) => new(
        Interlocked.Read(ref contextEpoch),
        generations.GetValueOrDefault(WorkspaceKey(workspace)),
        generations.GetValueOrDefault(TypeKey(category)),
        generations.GetValueOrDefault(WorkspaceTypeKey(workspace, category)));

    /// <summary>A stored entry stays valid across workspace switches (it is keyed by workspace); only data changes retire it.</summary>
    private static bool EntryIsCurrent(Generation stored, Generation current) =>
        stored.Workspace == current.Workspace && stored.Type == current.Type && stored.WorkspaceType == current.WorkspaceType;

    public async Task<T> GetOrCreateAsync<T>(
        CacheScope scope,
        string category,
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = BuildKey(scope, category, key);
        var generation = Capture(scope.WorkspaceId, category);
        if (entries.TryGetValue(cacheKey, out var entry) && entry.ExpiresAtUtc > timeProvider.GetUtcNow()
            && EntryIsCurrent(entry.Generation, generation) && entry.Value is T cached)
        {
            return cached;
        }

        Flight flight;
        var started = false;
        lock (gate)
        {
            // A flight started under an older generation is never joined: its answer would be rejected anyway.
            if (!flights.TryGetValue(cacheKey, out flight!) || !flight.Generation.Equals(generation))
            {
                flight = new Flight(generation);
                flights[cacheKey] = flight;
                started = true;
            }

            flight.Waiters++;
        }

        // The fetch starts outside the lock, on the caller's thread until its first await; no Task.Run wrapper is needed.
        if (started) _ = RunAsync(cacheKey, scope.WorkspaceId, category, factory, ttl ?? defaultTtl, flight).ContinueWith(
            static (work, state) => ((Flight)state!).Complete(work), flight, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        try
        {
            // The caller's token only ends this caller's wait; the shared fetch lives as long as anyone still wants it.
            var output = await flight.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!Capture(scope.WorkspaceId, category).Equals(generation)) throw new StaleWorkspaceResultException();
            return (T)output!;
        }
        finally
        {
            lock (gate)
            {
                flight.Waiters--;
                if (flight.Waiters == 0 && !flight.Task.IsCompleted)
                {
                    // Nobody is waiting any more: stop the shared fetch instead of letting it run for no one.
                    try { flight.Cancellation.Cancel(); }
                    catch (ObjectDisposedException) { /* the fetch finished between the check and the cancel */ }
                    if (flights.TryGetValue(cacheKey, out var current) && ReferenceEquals(current, flight)) flights.Remove(cacheKey);
                }
            }
        }
    }

    private async Task<object?> RunAsync<T>(string cacheKey, Guid workspace, string category, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, Flight flight)
    {
        try
        {
            var result = await factory(flight.Cancellation.Token).ConfigureAwait(false);
            lock (gate)
            {
                // Store only if nothing was invalidated or switched while the fetch was running and this flight is still the
                // registered one; otherwise an old answer would resurrect itself after the invalidation that retired it.
                if (result is not null && Capture(workspace, category).Equals(flight.Generation)
                    && flights.TryGetValue(cacheKey, out var registered) && ReferenceEquals(registered, flight))
                {
                    entries[cacheKey] = new Entry(result, timeProvider.GetUtcNow().Add(ttl), flight.Generation);
                }
            }

            return result;
        }
        finally
        {
            lock (gate)
            {
                if (flights.TryGetValue(cacheKey, out var registered) && ReferenceEquals(registered, flight)) flights.Remove(cacheKey);
            }

            flight.Cancellation.Dispose();
        }
    }

    public void InvalidateCategory(CacheScope scope, string category)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(category);
        Bump(WorkspaceTypeKey(scope.WorkspaceId, category));
        Remove(Prefix(scope.WorkspaceId, category));
    }

    public void InvalidateWorkspace(Guid workspaceId)
    {
        Bump(WorkspaceKey(workspaceId));
        Remove($"{workspaceId:N}|");
    }

    public void InvalidateReferenceType(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        Bump(TypeKey(category));
        var marker = $"|{category}|";
        foreach (var key in entries.Keys)
            if (key.Contains(marker, StringComparison.Ordinal)) entries.TryRemove(key, out _);
    }

    public void InvalidateAll()
    {
        AdvanceContext();
        lock (gate)
        {
            entries.Clear();
            // Waiters keep their own flight object; clearing the table only stops new callers from joining a retired one.
            flights.Clear();
        }
    }

    public void AdvanceContext() => Interlocked.Increment(ref contextEpoch);

    private void Bump(string generationKey)
    {
        generations.AddOrUpdate(generationKey, 1, static (_, value) => value + 1);
        lock (gate) flights.Clear();
    }

    private void Remove(string prefix)
    {
        foreach (var key in entries.Keys)
            if (key.StartsWith(prefix, StringComparison.Ordinal)) entries.TryRemove(key, out _);
    }
}
