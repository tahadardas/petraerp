using System.Diagnostics;

namespace PetraERP.Api.Security;

/// <summary>What the admission gate is doing now and has done since the process started.</summary>
public sealed record WriteGateSnapshot(
    int MaxConcurrent, int MaxPerCompany, int MaxWaitSeconds, int InFlight, int Waiting, long Admitted, long TimedOut,
    long WaitP50Ms, long WaitP95Ms, long WaitMaxMs, int CompaniesTracked);

/// <summary>
/// Admission control for the idempotent financial write endpoints. Without it, 150 simultaneous posting requests each wanted
/// several pooled connections and exhausted the pool (measured: 52 of 150 requests failed with 500 "timeout obtaining a
/// connection from the pool"). Requests beyond the limit wait in line (bounded) instead of failing.
/// <para>
/// Fairness: a company may hold at most <c>MaxPerCompany</c> of the slots, and it waits for that company allowance BEFORE it
/// takes a global slot, so a busy company's queue never occupies slots a quiet company needs. The gate lives in one process;
/// it does not coordinate several API instances (each instance limits its own pool use, which is what it protects).
/// </para>
/// </summary>
internal sealed class WriteAdmission : IDisposable
{
    private const int SampleWindow = 512;

    private readonly SemaphoreSlim global;
    private readonly int maxPerCompany;
    private readonly TimeSpan maxWait;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> companies = new();
    private readonly double[] samples = new double[SampleWindow];
    private readonly Lock sampleLock = new();
    private int sampleCount;
    private int inFlight;
    private int waiting;
    private long admitted;
    private long timedOut;
    private double waitMax;

    internal WriteAdmission(int maxConcurrent, int maxPerCompany, TimeSpan maxWait)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrent, 1);
        MaxConcurrent = maxConcurrent;
        this.maxPerCompany = Math.Clamp(maxPerCompany, 1, maxConcurrent);
        this.maxWait = maxWait;
        global = new SemaphoreSlim(maxConcurrent);
    }

    internal int MaxConcurrent { get; }

    public void Dispose()
    {
        global.Dispose();
        foreach (var company in companies.Values) company.Dispose();
    }

    internal async Task<IDisposable> EnterAsync(Guid company, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var perCompany = companies.GetOrAdd(company, _ => new SemaphoreSlim(maxPerCompany));
        Interlocked.Increment(ref waiting);
        var haveCompany = false;
        try
        {
            if (!await perCompany.WaitAsync(maxWait, cancellationToken).ConfigureAwait(false)) throw TimedOut();
            haveCompany = true;
            var remaining = maxWait - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero || !await global.WaitAsync(remaining, cancellationToken).ConfigureAwait(false)) throw TimedOut();
        }
        catch
        {
            if (haveCompany) perCompany.Release();
            Interlocked.Decrement(ref waiting);
            throw;
        }

        Interlocked.Decrement(ref waiting);
        Interlocked.Increment(ref inFlight);
        Interlocked.Increment(ref admitted);
        Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return new Releaser(this, perCompany);
    }

    internal WriteGateSnapshot Snapshot()
    {
        double[] copy;
        lock (sampleLock) copy = samples.AsSpan(0, Math.Min(sampleCount, SampleWindow)).ToArray();
        Array.Sort(copy);
        double Percentile(double fraction) => copy.Length == 0 ? 0 : copy[Math.Min(copy.Length - 1, (int)Math.Ceiling(fraction * copy.Length) - 1)];
        return new(MaxConcurrent, maxPerCompany, (int)maxWait.TotalSeconds, Volatile.Read(ref inFlight), Volatile.Read(ref waiting),
            Interlocked.Read(ref admitted), Interlocked.Read(ref timedOut), (long)Math.Round(Percentile(0.5)), (long)Math.Round(Percentile(0.95)), (long)Math.Round(Volatile.Read(ref waitMax)), companies.Count);
    }

    private ServerBusyException TimedOut()
    {
        Interlocked.Increment(ref timedOut);
        return new ServerBusyException();
    }

    private void Record(double waitedMs)
    {
        lock (sampleLock)
        {
            samples[sampleCount++ % SampleWindow] = waitedMs;
            if (waitedMs > waitMax) waitMax = waitedMs;
        }
    }

    private sealed class Releaser(WriteAdmission owner, SemaphoreSlim perCompany) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0) return;
            Interlocked.Decrement(ref owner.inFlight);
            owner.global.Release();
            perCompany.Release();
        }
    }
}

/// <summary>
/// Process-wide gate with operator settings: PETRAERP_MAX_CONCURRENT_WRITES (default 24, 1-200),
/// PETRAERP_MAX_WRITES_PER_COMPANY (default 75% of the total, at least 1) and PETRAERP_WRITE_QUEUE_SECONDS (default 60, 1-600).
/// </summary>
internal static class FinancialWriteGate
{
    internal static WriteAdmission Shared { get; } = Create();

    internal static Task<IDisposable> EnterAsync(Guid company, CancellationToken cancellationToken) => Shared.EnterAsync(company, cancellationToken);

    private static WriteAdmission Create()
    {
        var total = Read("PETRAERP_MAX_CONCURRENT_WRITES", 1, 200, 24);
        var perCompany = Read("PETRAERP_MAX_WRITES_PER_COMPANY", 1, 200, Math.Max(1, total * 3 / 4));
        var wait = Read("PETRAERP_WRITE_QUEUE_SECONDS", 1, 600, 60);
        return new WriteAdmission(total, perCompany, TimeSpan.FromSeconds(wait));
    }

    private static int Read(string name, int minimum, int maximum, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var configured) && configured >= minimum && configured <= maximum ? configured : fallback;
}
