#pragma warning disable CA1707

using PetraERP.Desktop.Caching;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// Gate B / P0: the reference cache must never answer for the wrong fiscal year or workspace, must not let one caller's
/// cancellation cancel everybody, and must not let an answer that was already superseded come back to life.
/// </summary>
public sealed class ReferenceDataCacheTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly Guid Company = Guid.NewGuid();

    private static CacheScope Scope(Guid workspace, Guid year, Guid? branch = null) => new(Company, year, workspace, branch);

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now = now.Add(by);
    }

    [Fact]
    public async Task SameCompanyDifferentFiscalYearsNeverShareAnEntry()
    {
        var cache = new ReferenceDataCache(TimeProvider.System);
        var ws2025 = Scope(Guid.NewGuid(), Guid.NewGuid());
        var ws2026 = Scope(Guid.NewGuid(), Guid.NewGuid());

        var a = await cache.GetOrCreateAsync(ws2025, "Accounts", "tree", _ => Task.FromResult("2025 chart"), null, Cancellation);
        var b = await cache.GetOrCreateAsync(ws2026, "Accounts", "tree", _ => Task.FromResult("2026 chart"), null, Cancellation);

        Assert.Equal("2025 chart", a);
        Assert.Equal("2026 chart", b);
        Assert.Equal("2025 chart", await cache.GetOrCreateAsync(ws2025, "Accounts", "tree", _ => Task.FromResult("WRONG"), null, Cancellation));
    }

    [Fact]
    public async Task BranchAndPermissionFingerprintAreAlsoPartOfTheKey()
    {
        var cache = new ReferenceDataCache(TimeProvider.System);
        var workspace = Guid.NewGuid();
        var year = Guid.NewGuid();
        var branchA = Scope(workspace, year, Guid.NewGuid());
        var branchB = Scope(workspace, year, Guid.NewGuid());
        var restricted = branchA with { PermissionFingerprint = "view-only" };

        var calls = 0;
        Task<int> Next(CancellationToken _) => Task.FromResult(Interlocked.Increment(ref calls));
        var first = await cache.GetOrCreateAsync(branchA, "Items", "k", Next, null, Cancellation);
        var second = await cache.GetOrCreateAsync(branchB, "Items", "k", Next, null, Cancellation);
        var third = await cache.GetOrCreateAsync(restricted, "Items", "k", Next, null, Cancellation);
        var again = await cache.GetOrCreateAsync(branchA, "Items", "k", Next, null, Cancellation);

        Assert.Equal([1, 2, 3, 1], new[] { first, second, third, again });
    }

    [Fact]
    public async Task ConcurrentCallersShareOneFetch()
    {
        var cache = new ReferenceDataCache(TimeProvider.System);
        var scope = Scope(Guid.NewGuid(), Guid.NewGuid());
        var gate = new TaskCompletionSource();
        var calls = 0;
        async Task<string> Fetch(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await gate.Task.ConfigureAwait(false);
            return "value";
        }

        var callers = Enumerable.Range(0, 100).Select(_ => cache.GetOrCreateAsync(scope, "Parties", "all", Fetch, null, Cancellation)).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.All(results, r => Assert.Equal("value", r));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OneCallersCancellationDoesNotCancelTheOthers()
    {
        var cache = new ReferenceDataCache(TimeProvider.System);
        var scope = Scope(Guid.NewGuid(), Guid.NewGuid());
        var gate = new TaskCompletionSource();
        var fetchToken = CancellationToken.None;
        async Task<string> Fetch(CancellationToken token)
        {
            fetchToken = token;
            await gate.Task.WaitAsync(token).ConfigureAwait(false);
            return "value";
        }

        using var firstCaller = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var first = cache.GetOrCreateAsync(scope, "Units", "all", Fetch, null, firstCaller.Token);
        var second = cache.GetOrCreateAsync(scope, "Units", "all", Fetch, null, Cancellation);

        await firstCaller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(fetchToken.IsCancellationRequested);   // the second caller still wants the answer
        gate.SetResult();

        Assert.Equal("value", await second.ConfigureAwait(true));
    }

    [Fact]
    public async Task TheSharedFetchIsCancelledOnlyWhenNobodyIsWaitingAnyMore()
    {
        var cache = new ReferenceDataCache(TimeProvider.System);
        var scope = Scope(Guid.NewGuid(), Guid.NewGuid());
        var started = new TaskCompletionSource();
        var fetchToken = CancellationToken.None;
        async Task<string> Fetch(CancellationToken token)
        {
            fetchToken = token;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return "never";
        }

        using var only = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var call = cache.GetOrCreateAsync(scope, "Units", "all", Fetch, null, only.Token);
        await started.Task.ConfigureAwait(true);
        await only.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);

        Assert.True(fetchToken.IsCancellationRequested);
    }

    [Fact]
    public async Task InvalidationDuringAFetchPreventsTheOldAnswerFromBeingStored()
    {
        var cache = new ReferenceDataCache(TimeProvider.System);
        var scope = Scope(Guid.NewGuid(), Guid.NewGuid());
        var gate = new TaskCompletionSource();
        var version = "old";
        async Task<string> Fetch(CancellationToken _)
        {
            var seen = version;
            await gate.Task.ConfigureAwait(false);
            return seen;
        }

        var inFlight = cache.GetOrCreateAsync(scope, "Accounts", "tree", Fetch, null, Cancellation);
        version = "new";                                  // the data changed while the fetch was running
        cache.InvalidateCategory(scope, "Accounts");
        gate.SetResult();

        await Assert.ThrowsAsync<StaleWorkspaceResultException>(() => inFlight);
        var fresh = await cache.GetOrCreateAsync(scope, "Accounts", "tree", _ => Task.FromResult("new"), null, Cancellation);
        Assert.Equal("new", fresh);                       // the retired "old" answer did not resurrect itself
    }

    [Fact]
    public async Task SwitchingWorkspaceRejectsAnswersStillInFlightButKeepsOtherEntries()
    {
        var cache = new ReferenceDataCache(TimeProvider.System);
        var year2025 = Scope(Guid.NewGuid(), Guid.NewGuid());
        var year2026 = Scope(Guid.NewGuid(), Guid.NewGuid());
        await cache.GetOrCreateAsync(year2026, "Items", "k", _ => Task.FromResult("2026 items"), null, Cancellation);

        var gate = new TaskCompletionSource();
        var inFlight = cache.GetOrCreateAsync(year2025, "Items", "slow", async _ => { await gate.Task.ConfigureAwait(false); return "2025 items"; }, null, Cancellation);
        var before = cache.ContextEpoch;
        cache.AdvanceContext();                           // the user switched to another workspace
        gate.SetResult();

        await Assert.ThrowsAsync<StaleWorkspaceResultException>(() => inFlight);
        Assert.True(cache.ContextEpoch > before);
        Assert.Equal("2026 items", await cache.GetOrCreateAsync(year2026, "Items", "k", _ => Task.FromResult("WRONG"), null, Cancellation));
    }

    [Fact]
    public async Task InvalidateCategoryOnlyAffectsItsOwnWorkspace()
    {
        var cache = new ReferenceDataCache(TimeProvider.System);
        var a = Scope(Guid.NewGuid(), Guid.NewGuid());
        var b = Scope(Guid.NewGuid(), Guid.NewGuid());
        await cache.GetOrCreateAsync(a, "Accounts", "t", _ => Task.FromResult("a1"), null, Cancellation);
        await cache.GetOrCreateAsync(b, "Accounts", "t", _ => Task.FromResult("b1"), null, Cancellation);

        cache.InvalidateCategory(a, "Accounts");

        Assert.Equal("a2", await cache.GetOrCreateAsync(a, "Accounts", "t", _ => Task.FromResult("a2"), null, Cancellation));
        Assert.Equal("b1", await cache.GetOrCreateAsync(b, "Accounts", "t", _ => Task.FromResult("WRONG"), null, Cancellation));
    }

    [Fact]
    public async Task WorkspaceTypeAndGlobalInvalidationsClearWhatTheyPromise()
    {
        var cache = new ReferenceDataCache(TimeProvider.System);
        var a = Scope(Guid.NewGuid(), Guid.NewGuid());
        var b = Scope(Guid.NewGuid(), Guid.NewGuid());
        foreach (var scope in new[] { a, b })
        {
            await cache.GetOrCreateAsync(scope, "Accounts", "t", _ => Task.FromResult("accounts"), null, Cancellation);
            await cache.GetOrCreateAsync(scope, "Items", "t", _ => Task.FromResult("items"), null, Cancellation);
        }

        cache.InvalidateWorkspace(a.WorkspaceId);
        Assert.Equal("fresh", await cache.GetOrCreateAsync(a, "Items", "t", _ => Task.FromResult("fresh"), null, Cancellation));
        Assert.Equal("items", await cache.GetOrCreateAsync(b, "Items", "t", _ => Task.FromResult("WRONG"), null, Cancellation));

        cache.InvalidateReferenceType("Accounts");
        Assert.Equal("fresh", await cache.GetOrCreateAsync(b, "Accounts", "t", _ => Task.FromResult("fresh"), null, Cancellation));
        Assert.Equal("items", await cache.GetOrCreateAsync(b, "Items", "t", _ => Task.FromResult("WRONG"), null, Cancellation));

        cache.InvalidateAll();
        Assert.Equal("fresh", await cache.GetOrCreateAsync(b, "Items", "t", _ => Task.FromResult("fresh"), null, Cancellation));
    }

    [Fact]
    public async Task EntriesExpireByTtlAndFailedFetchesAreNotCached()
    {
        var time = new ManualTime();
        var cache = new ReferenceDataCache(time);
        var scope = Scope(Guid.NewGuid(), Guid.NewGuid());
        var calls = 0;
        Task<int> Fetch(CancellationToken _) => Task.FromResult(Interlocked.Increment(ref calls));

        Assert.Equal(1, await cache.GetOrCreateAsync(scope, "Units", "k", Fetch, TimeSpan.FromMinutes(1), Cancellation));
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, await cache.GetOrCreateAsync(scope, "Units", "k", Fetch, TimeSpan.FromMinutes(1), Cancellation));
        time.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(2, await cache.GetOrCreateAsync(scope, "Units", "k", Fetch, TimeSpan.FromMinutes(1), Cancellation));

        var failing = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrCreateAsync<int>(scope, "Units", "boom", _ => throw new InvalidOperationException("server down"), null, Cancellation));
        Assert.Equal("server down", failing.Message);
        Assert.Equal(7, await cache.GetOrCreateAsync(scope, "Units", "boom", _ => Task.FromResult(7), null, Cancellation));
    }
}
