using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PetraERP.Api.Configuration;
using PetraERP.Api.Errors;

namespace PetraERP.Api.Hosting;

/// <summary>
/// Per-client-address fixed windows. Authentication endpoints have a tight budget (brute-force protection in
/// addition to per-account lockout); all other API calls share a generous budget against runaway clients.
/// </summary>
internal static class RateLimitPolicies
{
    internal const string Authentication = "petra:rate:authentication";

    internal static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = static async (context, cancellationToken) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                ApiProblems.SetRetryAfter(context.HttpContext.Response, Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)));
            }

            await ApiProblems.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, ApiErrorCodes.RequestRateLimited)
                .ConfigureAwait(false);
        };
        options.AddPolicy(Authentication, static httpContext =>
        {
            var limits = httpContext.RequestServices.GetRequiredService<IOptions<PetraSecurityOptions>>().Value.RateLimits;
            return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.AuthenticationPermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(static httpContext =>
        {
            if (!httpContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                return RateLimitPartition.GetNoLimiter("non-api");
            }

            var limits = httpContext.RequestServices.GetRequiredService<IOptions<PetraSecurityOptions>>().Value.RateLimits;
            return RateLimitPartition.GetFixedWindowLimiter("api:" + PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.ApiPermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });
    }

    private static string PartitionKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
