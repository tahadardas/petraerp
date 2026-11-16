using System.Diagnostics;
using PetraERP.Infrastructure.Diagnostics;

namespace PetraERP.Api.Hosting;

/// <summary>
/// Measures every request end to end: total time and database time. The result is returned in the standard
/// <c>Server-Timing</c> response header (read by the desktop client for its own per-screen log) and written to
/// the log together with the correlation scope. Slow requests are logged as warnings. Only the route template,
/// method, status and durations are recorded - never query strings, bodies, tokens or amounts.
/// </summary>
internal sealed partial class RequestTimingMiddleware
{
    internal const double SlowRequestMilliseconds = 500;

    private readonly RequestDelegate next;
    private readonly ILogger<RequestTimingMiddleware> logger;

    public RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var metrics = RequestMetrics.Begin();
        var started = Stopwatch.GetTimestamp();
        context.Response.OnStarting(static state =>
        {
            var (httpContext, requestMetrics, startedAt) = ((HttpContext, RequestMetrics, long))state;
            var total = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            httpContext.Response.Headers["Server-Timing"] = FormattableString.Invariant(
                $"total;dur={total:F1}, db;dur={requestMetrics.SqlMilliseconds:F1};desc=\"{requestMetrics.SqlCommandCount} cmd\"");
            return Task.CompletedTask;
        }, (context, metrics, started));

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            var total = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            RequestMetrics.End();
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            if (total >= SlowRequestMilliseconds)
            {
                LogSlow(logger, context.Request.Method, route, context.Response.StatusCode, total, metrics.SqlMilliseconds, metrics.SqlCommandCount);
            }
            else
            {
                LogRequest(logger, context.Request.Method, route, context.Response.StatusCode, total, metrics.SqlMilliseconds, metrics.SqlCommandCount);
            }
        }
    }

    [LoggerMessage(EventId = 7001, Level = LogLevel.Debug,
        Message = "request {Method} {Route} -> {Status} total={TotalMs:F1}ms sql={SqlMs:F1}ms cmds={SqlCommands}")]
    private static partial void LogRequest(ILogger logger, string method, string route, int status, double totalMs, double sqlMs, int sqlCommands);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Warning,
        Message = "slow request {Method} {Route} -> {Status} total={TotalMs:F1}ms sql={SqlMs:F1}ms cmds={SqlCommands}")]
    private static partial void LogSlow(ILogger logger, string method, string route, int status, double totalMs, double sqlMs, int sqlCommands);
}
