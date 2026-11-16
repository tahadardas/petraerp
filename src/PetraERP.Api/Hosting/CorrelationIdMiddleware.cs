using System.Diagnostics;

namespace PetraERP.Api.Hosting;

/// <summary>
/// Assigns every request a correlation identifier: a well-formed client <c>X-Correlation-Id</c> is honoured
/// (for end-to-end support traces), otherwise the W3C trace id is used. It is echoed in the response header,
/// in every problem response and in the logging scope.
/// </summary>
internal sealed class CorrelationIdMiddleware
{
    internal const string HeaderName = "X-Correlation-Id";
    private static readonly object ItemKey = new();

    private readonly RequestDelegate next;
    private readonly ILogger<CorrelationIdMiddleware> logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsWellFormed(supplied)
            ? supplied
            : Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;
        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(static state =>
        {
            var (httpContext, value) = ((HttpContext, string))state;
            httpContext.Response.Headers[HeaderName] = value;
            return Task.CompletedTask;
        }, (context, correlationId));

        using (BeginCorrelationScope(logger, correlationId))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    internal static string Get(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(ItemKey, out var value) && value is string correlationId
            ? correlationId
            : Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;
    }

    private static bool IsWellFormed(string value)
    {
        if (value.Length is < 8 or > 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static IDisposable? BeginCorrelationScope(ILogger logger, string correlationId) =>
        logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
}
