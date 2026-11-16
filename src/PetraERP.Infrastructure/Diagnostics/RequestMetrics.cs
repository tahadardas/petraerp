using System.Diagnostics;

namespace PetraERP.Infrastructure.Diagnostics;

/// <summary>
/// Per-request performance counters flowing with the async context. The HTTP pipeline opens a scope per request;
/// <see cref="SqlTimingInterceptor"/> adds the database time of every command executed inside it. No SQL text,
/// parameters or row data are ever recorded - only durations and counts.
/// </summary>
public sealed class RequestMetrics
{
    private static readonly AsyncLocal<RequestMetrics?> CurrentScope = new();

    private long sqlTicks;
    private int sqlCommands;

    /// <summary>The metrics of the request being executed, or null outside a request scope.</summary>
    public static RequestMetrics? Current => CurrentScope.Value;

    /// <summary>Total database command time in milliseconds (time until each reader/result was available).</summary>
    public double SqlMilliseconds => (double)Interlocked.Read(ref sqlTicks) / TimeSpan.TicksPerMillisecond;

    /// <summary>Number of database commands executed.</summary>
    public int SqlCommandCount => Volatile.Read(ref sqlCommands);

    /// <summary>Starts a metrics scope for the current async flow.</summary>
    public static RequestMetrics Begin()
    {
        var metrics = new RequestMetrics();
        CurrentScope.Value = metrics;
        return metrics;
    }

    /// <summary>Ends the scope of the current async flow.</summary>
    public static void End() => CurrentScope.Value = null;

    internal void AddSql(TimeSpan duration)
    {
        Interlocked.Add(ref sqlTicks, duration.Ticks);
        Interlocked.Increment(ref sqlCommands);
    }
}
