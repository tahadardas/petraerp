using PetraERP.Api.Hosting;
using PetraERP.Application.Security;

namespace PetraERP.Api.Security;

internal static class HttpContextSecurityExtensions
{
    private static readonly object SessionKey = new();

    internal static void SetValidatedSession(this HttpContext httpContext, SessionPrincipal session) =>
        httpContext.Items[SessionKey] = session;

    internal static bool TryGetValidatedSession(this HttpContext httpContext, out SessionPrincipal session)
    {
        if (httpContext.Items.TryGetValue(SessionKey, out var value) && value is SessionPrincipal principal)
        {
            session = principal;
            return true;
        }

        session = null!;
        return false;
    }

    /// <summary>The database-validated session of an authorized endpoint. Never derived from request data.</summary>
    internal static SessionPrincipal RequireSession(this HttpContext httpContext) =>
        httpContext.TryGetValidatedSession(out var session)
            ? session
            : throw new InvalidOperationException("The endpoint requires an authenticated, validated session.");

    internal static ClientContext ToClientContext(this HttpContext httpContext) =>
        ClientContext.Create(httpContext.Connection.RemoteIpAddress?.ToString(), httpContext.Request.Headers.UserAgent.ToString(),
            CorrelationIdMiddleware.Get(httpContext));

    internal static Guid? RouteGuid(this HttpContext httpContext, string name) =>
        httpContext.Request.RouteValues.TryGetValue(name, out var value)
        && Guid.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var parsed)
            ? parsed
            : null;
}
